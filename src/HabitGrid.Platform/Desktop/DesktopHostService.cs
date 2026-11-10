using System.Runtime.InteropServices;

namespace HabitGrid.Platform.Desktop;

/// <summary>
/// Quản lý việc ghim cửa sổ widget vào desktop Windows.
///
/// Ba chế độ:
/// 1. <see cref="DesktopPinningMode.DesktopLevel"/>: can thiệp cây cửa sổ WorkerW của Explorer.
/// 2. <see cref="DesktopPinningMode.BottomMost"/>: đẩy cửa sổ xuống đáy z-order (dự phòng ổn định).
/// 3. <see cref="DesktopPinningMode.AlwaysOnTop"/>: cửa sổ luôn trên cùng.
///
/// Chế độ 1 có thể thất bại trên một số bản Windows 11 (24H2 trở lên đã đổi cấu trúc cây WorkerW).
/// Khi đó dịch vụ tự động chuyển sang chế độ 2 và báo lại qua <see cref="PinningChanged"/>.
/// </summary>
public sealed class DesktopHostService
{
    private readonly object _syncRoot = new();
    private readonly IScreenProvider _screenProvider;

    private readonly HashSet<IntPtr> _aboveWindows = [];
    private IntPtr _attachedWindow;
    private DesktopPinningMode _currentMode = DesktopPinningMode.BottomMost;
    private IntPtr _foregroundHook;
    private WindowInterop.WinEventProc? _foregroundCallback;

    public DesktopHostService(IScreenProvider? screenProvider = null)
    {
        _screenProvider = screenProvider ?? new WindowsScreenProvider();
    }

    /// <summary>
    /// Ép kiểu ghim thay cho <see cref="DesktopPinningMode.DesktopLevel"/>, lấy từ biến môi trường <c>HABITGRID_PIN</c>
    /// (<c>above</c> = ngay trên lớp desktop, <c>top</c> = luôn trên cùng, <c>bottom</c> = đáy z-order). Dùng để chẩn đoán trên máy
    /// mà việc ghim làm con của Progman không hiện hình; không đặt biến thì giữ hành vi mặc định.
    /// </summary>
    public static DesktopPinningMode? PinOverride { get; set; } = Environment.GetEnvironmentVariable("HABITGRID_PIN")?.Trim().ToLowerInvariant() switch
    {
        "above" => DesktopPinningMode.AboveDesktop,
        "top" => DesktopPinningMode.AlwaysOnTop,
        "bottom" => DesktopPinningMode.BottomMost,
        _ => null
    };

    /// <summary>Chế độ ghim hiện tại.</summary>
    public DesktopPinningMode CurrentMode => _currentMode;

    /// <summary>Cửa sổ đang được quản lý.</summary>
    public IntPtr AttachedWindow => _attachedWindow;

    /// <summary>Được gọi khi chế độ ghim thay đổi (kể cả khi fallback).</summary>
    public event EventHandler<DesktopHostResult>? PinningChanged;

    /// <summary>
    /// Ghim cửa sổ theo chế độ yêu cầu. Nếu chế độ Desktop-Level thất bại sẽ tự chuyển sang Bottom-Most.
    /// </summary>
    public DesktopHostResult Attach(IntPtr hwnd, DesktopPinningMode requestedMode)
    {
        if (hwnd == IntPtr.Zero)
        {
            return DesktopHostResult.Failed(requestedMode, "Handle cửa sổ không hợp lệ.");
        }

        if (requestedMode == DesktopPinningMode.DesktopLevel && PinOverride is { } forced)
        {
            requestedMode = forced;
        }

        lock (_syncRoot)
        {
            _attachedWindow = hwnd;
            _currentMode = requestedMode;

            switch (requestedMode)
            {
                case DesktopPinningMode.AboveDesktop:
                    PlaceAboveDesktop(hwnd);
                    _aboveWindows.Add(hwnd);
                    EnsureForegroundHook();
                    var above = DesktopHostResult.Ok(DesktopPinningMode.AboveDesktop);
                    RaisePinningChanged(above);
                    return above;

                case DesktopPinningMode.DesktopLevel:
                    if (TryAttachDesktopLevel(hwnd))
                    {
                        var success = DesktopHostResult.Ok(DesktopPinningMode.DesktopLevel);
                        RaisePinningChanged(success);
                        return success;
                    }

                    // Chế độ 1 thất bại -> tự chuyển sang chế độ 2.
                    PushToBottom(hwnd);
                    _currentMode = DesktopPinningMode.BottomMost;

                    var fallback = DesktopHostResult.Fallback(
                        DesktopPinningMode.DesktopLevel,
                        DesktopPinningMode.BottomMost,
                        "Không tìm thấy WorkerW phù hợp trên phiên bản Windows này. Đã chuyển sang chế độ Bottom-Most.");

                    RaisePinningChanged(fallback);
                    return fallback;

                case DesktopPinningMode.BottomMost:
                    PushToBottom(hwnd);
                    EnsureForegroundHook();
                    var bottom = DesktopHostResult.Ok(DesktopPinningMode.BottomMost);
                    RaisePinningChanged(bottom);
                    return bottom;

                case DesktopPinningMode.AlwaysOnTop:
                    SetAlwaysOnTop(hwnd, true);
                    var top = DesktopHostResult.Ok(DesktopPinningMode.AlwaysOnTop);
                    RaisePinningChanged(top);
                    return top;

                default:
                    return DesktopHostResult.Failed(requestedMode, "Chế độ ghim không hợp lệ.");
            }
        }
    }

    /// <summary>
    /// Thử ghim cửa sổ ở mức desktop (nằm trên hình nền, dưới mọi cửa sổ).
    /// </summary>
    public bool TryAttachDesktopLevel(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var progman = WindowInterop.FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            return false;
        }

        // Buộc Explorer tạo cửa sổ WorkerW trung gian.
        WindowInterop.SendMessageTimeout(
            progman,
            WindowInterop.WM_SPAWN_WORKER,
            new IntPtr(0xD),
            new IntPtr(0x1),
            WindowInterop.SMTO_ABORTIFHUNG,
            1000,
            out _);

        var host = FindDesktopHost(progman);
        if (host == IntPtr.Zero)
        {
            return false;
        }

        // Lưu vị trí trên màn hình trước khi đổi cha để giữ nguyên vị trí hiển thị.
        var screenRect = GetScreenRect(hwnd);

        // SetParent trả về CHA CŨ, mà với một cửa sổ top-level cha cũ là NULL.
        // Vì vậy không thể dùng giá trị trả về để phân biệt thành công/thất bại,
        // phải kiểm tra GetLastError.
        WindowInterop.SetLastError(0);
        WindowInterop.SetParent(hwnd, host);

        if (Marshal.GetLastWin32Error() != 0)
        {
            return false;
        }

        ApplyChildWindowStyles(hwnd);
        RestorePosition(hwnd, host, screenRect);

        return true;
    }

    /// <summary>Đặt cửa sổ cấp cao ngay trên Progman (hình nền + icon) và dưới mọi cửa sổ khác.</summary>
    private static void PlaceAboveDesktop(IntPtr hwnd)
    {
        ApplyToolWindowStyles(hwnd);
        var progman = WindowInterop.FindWindow("Progman", null);

        WindowInterop.SetWindowPos(
            hwnd,
            progman,
            0,
            0,
            0,
            0,
            WindowInterop.SWP_NOMOVE | WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOACTIVATE | WindowInterop.SWP_SHOWWINDOW);
    }

    /// <summary>Đẩy cửa sổ xuống đáy z-order.</summary>
    public void PushToBottom(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        ApplyToolWindowStyles(hwnd);

        WindowInterop.SetWindowPos(
            hwnd,
            WindowInterop.HWND_BOTTOM,
            0,
            0,
            0,
            0,
            WindowInterop.SWP_NOMOVE | WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOACTIVATE);
    }

    /// <summary>Bật/tắt chế độ luôn nằm trên cùng.</summary>
    public void SetAlwaysOnTop(IntPtr hwnd, bool enabled)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        ApplyToolWindowStyles(hwnd);

        WindowInterop.SetWindowPos(
            hwnd,
            enabled ? WindowInterop.HWND_TOPMOST : WindowInterop.HWND_NOTOPMOST,
            0,
            0,
            0,
            0,
            WindowInterop.SWP_NOMOVE | WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Bật/tắt chế độ click-through (chuột xuyên qua widget) dùng cho chế độ xem tĩnh.
    /// </summary>
    public void SetClickThrough(IntPtr hwnd, bool enabled)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var exStyle = WindowInterop.GetWindowLongPtr(hwnd, WindowInterop.GWL_EXSTYLE).ToInt64();

        if (enabled)
        {
            exStyle |= WindowInterop.WS_EX_TRANSPARENT;
        }
        else
        {
            exStyle &= ~WindowInterop.WS_EX_TRANSPARENT;
        }

        WindowInterop.SetWindowLongPtr(hwnd, WindowInterop.GWL_EXSTYLE, new IntPtr(exStyle));
        ApplyFrameChanged(hwnd);
    }

    /// <summary>
    /// Ghim lại cửa sổ theo chế độ hiện tại.
    /// Cần gọi khi Explorer khởi động lại (sự kiện TaskbarCreated) hoặc khi đổi độ phân giải/DPI.
    /// </summary>
    public DesktopHostResult EnsurePinned(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return DesktopHostResult.Failed(_currentMode, "Handle cửa sổ không hợp lệ.");
        }

        return Attach(hwnd, _currentMode);
    }

    /// <summary>Gỡ cửa sổ khỏi desktop và trả về style cửa sổ bình thường.</summary>
    public void Detach(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        lock (_syncRoot)
        {
            WindowInterop.SetParent(hwnd, IntPtr.Zero);

            var style = WindowInterop.GetWindowLongPtr(hwnd, WindowInterop.GWL_STYLE).ToInt64();
            style &= ~WindowInterop.WS_CHILD;
            style |= WindowInterop.WS_POPUP;
            WindowInterop.SetWindowLongPtr(hwnd, WindowInterop.GWL_STYLE, new IntPtr(style));

            var exStyle = WindowInterop.GetWindowLongPtr(hwnd, WindowInterop.GWL_EXSTYLE).ToInt64();
            exStyle &= ~WindowInterop.WS_EX_NOACTIVATE;
            exStyle &= ~WindowInterop.WS_EX_TRANSPARENT;
            WindowInterop.SetWindowLongPtr(hwnd, WindowInterop.GWL_EXSTYLE, new IntPtr(exStyle));

            ApplyFrameChanged(hwnd);

            _attachedWindow = IntPtr.Zero;
        }
    }

    /// <summary>Tìm màn hình đang chứa cửa sổ.</summary>
    public ScreenDescriptor? GetScreenForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !WindowInterop.GetWindowRect(hwnd, out var rect))
        {
            return null;
        }

        return _screenProvider.GetScreenAt(rect.Left + (rect.Width / 2), rect.Top + (rect.Height / 2));
    }

    /// <summary>
    /// Tìm cửa sổ làm cha mới cho widget.
    /// <para>
    /// Windows 11 24H2 trở lên: <c>SHELLDLL_DefView</c> là con TRỰC TIẾP của <c>Progman</c>
    /// (không còn nằm trong WorkerW). Gắn widget làm con của <c>Progman</c> — tức là anh em
    /// với lớp icon — giúp widget nằm trên hình nền mà VẪN NHẬN ĐƯỢC CHUỘT
    /// (giải quyết rủi ro "input trap" của POC).
    /// </para>
    /// <para>
    /// Windows 10 / Windows 11 23H2: lớp icon nằm trong một WorkerW, WorkerW đứng ngay sau nó
    /// trong z-order (thấp hơn) là lớp hình nền — dùng WorkerW đó làm cha.
    /// </para>
    /// </summary>
    private static IntPtr FindDesktopHost(IntPtr progman)
    {
        // Cấu trúc 24H2: SHELLDLL_DefView là con trực tiếp của Progman.
        var defViewUnderProgman = WindowInterop.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

        if (defViewUnderProgman != IntPtr.Zero)
        {
            return progman;
        }

        // Cấu trúc cũ: tìm WorkerW chứa lớp icon, rồi lấy WorkerW đứng ngay dưới nó.
        var behindShellView = IntPtr.Zero;

        WindowInterop.EnumWindows((hwnd, _) =>
        {
            var shellView = WindowInterop.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);

            if (shellView != IntPtr.Zero)
            {
                // EnumWindows duyệt theo z-order từ trên xuống -> WorkerW nằm sau là handle
                // tìm được với childAfter = hwnd.
                behindShellView = WindowInterop.FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
                return false;
            }

            return true;
        }, IntPtr.Zero);

        if (behindShellView != IntPtr.Zero)
        {
            return behindShellView;
        }

        // WorkerW nằm ngay dưới Progman (một số bản dàn xếp khác).
        var workerBelowProgman = WindowInterop.FindWindowEx(IntPtr.Zero, progman, "WorkerW", null);

        if (workerBelowProgman != IntPtr.Zero)
        {
            return workerBelowProgman;
        }

        return progman;
    }

    private static void ApplyChildWindowStyles(IntPtr hwnd)
    {
        var style = WindowInterop.GetWindowLongPtr(hwnd, WindowInterop.GWL_STYLE).ToInt64();
        style &= ~WindowInterop.WS_POPUP;
        style |= WindowInterop.WS_CHILD;
        WindowInterop.SetWindowLongPtr(hwnd, WindowInterop.GWL_STYLE, new IntPtr(style));

        ApplyToolWindowStyles(hwnd);
        ApplyFrameChanged(hwnd);
    }

    private static void ApplyToolWindowStyles(IntPtr hwnd)
    {
        var exStyle = WindowInterop.GetWindowLongPtr(hwnd, WindowInterop.GWL_EXSTYLE).ToInt64();
        exStyle |= WindowInterop.WS_EX_TOOLWINDOW | WindowInterop.WS_EX_NOACTIVATE;
        WindowInterop.SetWindowLongPtr(hwnd, WindowInterop.GWL_EXSTYLE, new IntPtr(exStyle));
    }

    private static void ApplyFrameChanged(IntPtr hwnd)
    {
        WindowInterop.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            WindowInterop.SWP_NOMOVE | WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOZORDER | WindowInterop.SWP_FRAMECHANGED | WindowInterop.SWP_NOACTIVATE);
    }

    private static WindowInterop.NativeRect GetScreenRect(IntPtr hwnd)
    {
        return WindowInterop.GetWindowRect(hwnd, out var rect)
            ? rect
            : new WindowInterop.NativeRect();
    }

    /// <summary>Giữ nguyên vị trí hiển thị sau khi đổi cha sang WorkerW (toạ độ trở thành tương đối với cha).</summary>
    private static void RestorePosition(IntPtr hwnd, IntPtr host, WindowInterop.NativeRect screenRect)
    {
        if (!WindowInterop.GetWindowRect(host, out var hostRect))
        {
            return;
        }

        var point = new WindowInterop.NativePoint
        {
            X = screenRect.Left - hostRect.Left,
            Y = screenRect.Top - hostRect.Top
        };

        WindowInterop.ScreenToClient(host, ref point);

        WindowInterop.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            point.X,
            point.Y,
            screenRect.Width,
            screenRect.Height,
            WindowInterop.SWP_NOZORDER | WindowInterop.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Hook EVENT_SYSTEM_FOREGROUND để đẩy widget về đáy z-order mỗi khi cửa sổ khác được kích hoạt.
    /// </summary>
    private void EnsureForegroundHook()
    {
        if (_foregroundHook != IntPtr.Zero)
        {
            return;
        }

        _foregroundCallback = OnForegroundChanged;

        _foregroundHook = WindowInterop.SetWinEventHook(
            WindowInterop.EVENT_SYSTEM_FOREGROUND,
            WindowInterop.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _foregroundCallback,
            0,
            0,
            WindowInterop.WINEVENT_OUTOFCONTEXT | WindowInterop.WINEVENT_SKIPOWNPROCESS);
    }

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // Chế độ AboveDesktop: bấm vào desktop có thể kéo Progman lên; đặt lại các widget ngay trên nó.
        foreach (var window in _aboveWindows.ToList())
        {
            PlaceAboveDesktop(window);
        }

        if (_currentMode == DesktopPinningMode.BottomMost && _attachedWindow != IntPtr.Zero)
        {
            WindowInterop.SetWindowPos(
                _attachedWindow,
                WindowInterop.HWND_BOTTOM,
                0,
                0,
                0,
                0,
                WindowInterop.SWP_NOMOVE | WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOACTIVATE);
        }
    }

    private void RaisePinningChanged(DesktopHostResult result)
    {
        PinningChanged?.Invoke(this, result);
    }
}
