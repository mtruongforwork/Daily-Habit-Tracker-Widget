using System.Runtime.InteropServices;
using System.Text;

namespace HabitGrid.Platform.Desktop;

/// <summary>Có ứng dụng toàn màn hình (trò chơi, video, trình chiếu, trình duyệt F11…) đang ở phía trước không.</summary>
public interface IFullscreenDetector
{
    bool IsFullscreenAppActive();
}

/// <summary>
/// Quy tắc thuần để nhận ra cửa sổ toàn màn hình, tách khỏi Win32 để kiểm thử được: cửa sổ phủ kín cả màn hình VÀ không có thanh tiêu đề
/// (cửa sổ phóng to bình thường vẫn có thanh tiêu đề nên không bị tính nhầm, kể cả khi thanh tác vụ tự ẩn). Desktop và thanh tác vụ không tính.
/// </summary>
public static class FullscreenRules
{
    private static readonly string[] ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "SHELLDLL_DefView"];

    public static bool IsFullscreen(ScreenRect window, ScreenRect monitor, string? className, bool hasCaption, bool isMinimized, bool isVisible)
    {
        if (!isVisible || isMinimized || hasCaption)
        {
            return false;
        }

        if (className is not null && ShellClasses.Contains(className, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return window.Left <= monitor.Left && window.Top <= monitor.Top && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
    }
}

/// <summary>
/// Chống nhấp nháy khi tín hiệu "toàn màn hình" chập chờn (chuyển cửa sổ bằng Alt+Tab): ẩn ngay khi thấy toàn màn hình,
/// chỉ hiện lại sau <c>showAfter</c> lần liên tiếp không thấy.
/// </summary>
public sealed class FullscreenGate
{
    private readonly int _showAfter;
    private int _clear;

    public FullscreenGate(int showAfter = 2)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(showAfter, 1);
        _showAfter = showAfter;
    }

    /// <summary>True khi widget đang phải ẩn.</summary>
    public bool Suppressed { get; private set; }

    /// <summary>Đưa kết quả đo mới vào; trả về true nếu <see cref="Suppressed"/> vừa đổi.</summary>
    public bool Update(bool fullscreenNow)
    {
        var before = Suppressed;

        if (fullscreenNow)
        {
            _clear = 0;
            Suppressed = true;
        }
        else if (Suppressed && ++_clear >= _showAfter)
        {
            Suppressed = false;
            _clear = 0;
        }

        return before != Suppressed;
    }
}

/// <summary>Hiện thực trên Windows: cửa sổ phía trước phủ kín màn hình không thanh tiêu đề, hoặc Windows báo chế độ bận/toàn màn hình/trình chiếu.</summary>
public sealed class WindowsFullscreenDetector : IFullscreenDetector
{
    private const int GwlStyle = -16;
    private const long WsCaption = 0x00C00000;
    private const uint MonitorDefaultToNearest = 2;

    public bool IsFullscreenAppActive()
    {
        try
        {
            return ForegroundCoversMonitor() || NotificationStateIsFullscreen();
        }
        catch (Exception)
        {
            // Không đo được thì coi như không có app toàn màn hình: widget vẫn hiện.
            return false;
        }
    }

    private static bool ForegroundCoversMonitor()
    {
        var hwnd = GetForegroundWindow();

        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        GetWindowThreadProcessId(hwnd, out var pid);

        if (pid == Environment.ProcessId || !GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };

        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        var name = new StringBuilder(64);
        GetClassName(hwnd, name, name.Capacity);
        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();

        return FullscreenRules.IsFullscreen(
            new ScreenRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top),
            new ScreenRect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top),
            name.ToString(),
            hasCaption: (style & WsCaption) == WsCaption,
            isMinimized: IsIconic(hwnd),
            isVisible: IsWindowVisible(hwnd));
    }

    /// <summary>QUNS_BUSY (2), QUNS_RUNNING_D3D_FULL_SCREEN (3), QUNS_PRESENTATION_MODE (4).</summary>
    private static bool NotificationStateIsFullscreen() =>
        SHQueryUserNotificationState(out var state) == 0 && state is 2 or 3 or 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);
}
