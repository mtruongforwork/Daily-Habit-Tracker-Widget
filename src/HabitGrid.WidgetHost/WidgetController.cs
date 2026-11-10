using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using CommunityToolkit.WinUI.Notifications;
using HabitGrid.Core.Domain;
using HabitGrid.Core.Services;
using HabitGrid.Data;
using HabitGrid.Data.V3;
using HabitGrid.Platform.Desktop;
using HabitGrid.Presentation.Services;
using HabitGrid.UI.Widgets;
using Microsoft.Data.Sqlite;
using V1 = HabitGrid.Core.Models;

namespace HabitGrid.WidgetHost;

/// <summary>
/// Quản lý các widget ghim desktop: đọc CSDL v3, dựng/làm mới cửa sổ, ghi check-in qua cùng repository với app chính.
/// Làm mới khi CSDL đổi (PRAGMA data_version, kể cả do tiến trình khác ghi) và khi qua ngày logic.
/// </summary>
public sealed partial class WidgetController : IDisposable
{
    public const int MaxWidgets = 3;

    private readonly HabitGridDatabase _database;
    private readonly HabitRepository _habits;
    private readonly DayLogRepository _logs;
    private readonly SettingsStore _settings;
    private readonly CheckInService _checkIn;
    private readonly IScreenProvider _screen = new WindowsScreenProvider();
    private readonly DesktopHostService _desktop;
    private readonly PaletteGenerator _palette = new();
    private readonly Dictionary<Guid, (WidgetWindow Window, WidgetViewModel Model)> _widgets = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly SqliteConnection _watch;
    private long _dataVersion = -1;
    private DateOnly _today;
    private bool _refreshing;
    private bool _visible = true;
    private readonly QuickCheckInModel _quick;
    private TodayListWindow? _todayList;
    private HotKey? _hotKey;
    private HotKey? _focusHotKey;
    private ShellEvents? _shell;
    private readonly AutoStart _autoStart = new("HabitGridWidgets");
    private bool _rebuildRequested;
    private bool _replaceRequested;
    private bool _needsRepin;
    private int _pinRetries;
    private int _pollCount;
    private DateTime _rebuildNotBefore = DateTime.MinValue;

    public WidgetController(HabitGridDatabase database)
    {
        _database = database;
        var clock = new HlcClock(Environment.MachineName + ":widgets");
        var bus = new DataChangeBus();
        _habits = new HabitRepository(database, clock, bus);
        _logs = new DayLogRepository(database, clock, bus);
        _settings = new SettingsStore(database);
        _checkIn = new CheckInService(_habits, _logs, _settings);
        _quick = new QuickCheckInModel(_habits, _logs, settings: _settings);
        _desktop = new DesktopHostService(_screen);
        _watch = database.OpenConnection();
        _timer.Tick += async (_, _) => await PollAsync();

        Toasts = new ToastScheduler(_habits, _logs, _settings);
        var focusService = new FocusService(_habits, _logs, new FocusRepository(database, clock, bus), new DayProvider(TimeSpan.Zero), _settings);
        FocusService = focusService;
        Focus = new FocusController(focusService, _settings);
        Focus.Completed += async (_, _) => await RefreshAsync();
    }

    /// <summary>Lịch toast nhắc nhở (SPEC-V2-09 §1).</summary>
    public ToastScheduler Toasts { get; }

    /// <summary>Bộ hẹn giờ Focus (SPEC-V2-09 §2).</summary>
    public FocusController Focus { get; }

    public FocusService FocusService { get; }

    public FocusWindow? FocusWindow { get; private set; }

    /// <summary>Phím tắt Focus đã đăng ký (Win+Alt+F hoặc Ctrl+Alt+F); null nếu tắt hoặc bị chiếm.</summary>
    public string? FocusHotKeyDescription => _focusHotKey?.Description;

    /// <summary>Mở app chính (nút "Mở HabitGrid" trên toast).</summary>
    public Action? OpenMainApp { get; init; }

    public IReadOnlyCollection<WidgetWindow> Windows => _widgets.Values.Select(w => w.Window).ToList();

    public IReadOnlyCollection<WidgetViewModel> Models => _widgets.Values.Select(w => w.Model).ToList();

    public DateOnly Today => _today;

    public TodayListWindow? TodayList => _todayList;

    public QuickCheckInWindow? QuickWindow { get; private set; }

    /// <summary>Phím tắt Quick check-in đã đăng ký được (Win+Alt+H hoặc Ctrl+Alt+H); null nếu bị chiếm.</summary>
    public string? HotKeyDescription => _hotKey?.Description;

    /// <summary>Nguồn trạng thái "chế độ sắp xếp" (menu khay); đọc mỗi nhịp làm mới.</summary>
    public Func<bool>? EditMode { get; init; }

    private bool _editApplied;

    public async Task StartAsync()
    {
        _today = CurrentDay();
        await _quick.LoadAsync(_today);
        await CreateAuxiliaryWindowsAsync();
        await RefreshAsync();

        _shell = new ShellEvents();
        _shell.ExplorerRestarted += (_, _) =>
        {
            // Explorer vừa dựng lại WorkerW; chờ vài giây cho xong rồi dựng lại toàn bộ widget (cái cũ đã bị hủy cùng WorkerW).
            _rebuildRequested = true;
            _rebuildNotBefore = DateTime.UtcNow.AddSeconds(2);
        };
        _shell.DisplayChanged += (_, _) => _replaceRequested = true;
        await ApplyAutoStartAsync();

        await Focus.LoadAsync();
        FocusWindow = new FocusWindow(Focus, () => FocusService.CandidatesAsync());
        FocusWindow.ApplyTheme(IsSystemDark());

        if (Focus.Timer is { Phase: HabitGrid.Core.Domain.Focus.FocusPhase.Work })
        {
            // Đang có phiên dở (WidgetHost vừa khởi động lại): hiện lại mini-player.
            await FocusWindow.PresentAsync();
        }

        await ApplyHotKeysAsync();
        await Toasts.ReplanAsync(DateTime.Now);
        await SyncMomentWidgetAsync();
        await SyncExtraWidgetsAsync();

        _dataVersion = ReadDataVersion();
        _timer.Start();
    }

    /// <summary>Giả lập Explorer khởi động lại (tự kiểm tra trên CI): coi như nhận được TaskbarCreated.</summary>
    public void SimulateExplorerRestart() => _rebuildRequested = true;

    public void ToggleVisibility()
    {
        _visible = !_visible;

        foreach (var window in new System.Windows.Window?[] { _todayList, _momentWidget })
        {
            if (window is null)
            {
                continue;
            }

            if (_visible)
            {
                window.Show();
            }
            else
            {
                window.Hide();
            }
        }

        foreach (var (window, _) in _widgets.Values)
        {
            if (_visible)
            {
                window.Show();
            }
            else
            {
                window.Hide();
            }
        }
    }

    /// <summary>Ghim lại mọi widget; trả về true nếu tất cả ghim đúng lớp desktop (không phải dự phòng).</summary>
    public bool ReattachAll()
    {
        var ok = true;

        foreach (var (window, _) in _widgets.Values)
        {
            ok &= !window.ApplyPinning(DesktopPinningMode.DesktopLevel).UsedFallback;
        }

        if (_todayList is not null)
        {
            ok &= !_todayList.Pin(_desktop, DesktopPinningMode.DesktopLevel).UsedFallback;
        }

        if (_momentWidget is not null)
        {
            ok &= !_momentWidget.Pin(_desktop, DesktopPinningMode.DesktopLevel).UsedFallback;
        }

        ok &= ReattachExtras();

        _needsRepin = !ok;
        return ok;
    }

    /// <summary>Số cửa sổ widget còn sống trên tổng số đang quản lý (gồm Today List).</summary>
    public (int Alive, int Total) WindowHealth()
    {
        var handles = _widgets.Values.Select(w => new WindowInteropHelper(w.Window).Handle).ToList();

        if (_todayList is not null)
        {
            handles.Add(new WindowInteropHelper(_todayList).Handle);
        }

        return (handles.Count(h => h != IntPtr.Zero && IsWindow(h)), handles.Count);
    }

    /// <summary>Mọi widget lưới đều là cửa sổ con của lớp desktop (WorkerW/Progman).</summary>
    public bool AllPinnedToDesktop() =>
        _widgets.Values.Select(w => new WindowInteropHelper(w.Window).Handle)
            .All(h => h != IntPtr.Zero && GetParent(h) != IntPtr.Zero);

    /// <summary>Dựng lại toàn bộ widget từ đầu: dùng khi Explorer khởi động lại làm các cửa sổ con bị hủy.</summary>
    public async Task RebuildAllAsync()
    {
        _rebuildRequested = false;

        foreach (var (window, _) in _widgets.Values)
        {
            TryClose(window);
        }

        _widgets.Clear();

        if (_todayList is not null)
        {
            TryClose(_todayList);
            _todayList = null;
        }

        ResetMomentWidget();
        ResetExtras();
        await CreateTodayListAsync();
        await RefreshAsync();
        await SyncMomentWidgetAsync();
        _pinRetries = 0;
        ReattachAll();
    }

    private static void TryClose(System.Windows.Window window)
    {
        try
        {
            window.Close();
        }
        catch (Exception)
        {
            // Cửa sổ đã bị hủy cùng Explorer.
        }
    }

    /// <summary>Đưa widget ngoài vùng nhìn thấy (rút màn hình phụ) về màn hình chính; cắm lại thì về vị trí đã lưu.</summary>
    private async Task ReplaceAllAsync()
    {
        _replaceRequested = false;

        foreach (var (habitId, entry) in _widgets.ToList())
        {
            var saved = await LoadPositionAsync(PositionKey(habitId));
            var window = entry.Window;
            var handle = new WindowInteropHelper(window).Handle;

            if (saved?.Split(',') is [var left, var top] && int.TryParse(left, out var l) && int.TryParse(top, out var t) && IsPointVisible(l, t))
            {
                window.SetScreenPosition(l, t);
            }
            else if (!IsOnAnyScreen(handle))
            {
                window.SetScreenPosition(60, 80);
            }
        }

        await RepositionExtrasAsync();

        if (_todayList is { } list)
        {
            var saved = await _settings.GetAsync(TodayListKey);

            if (saved?.Split(',') is [var left, var top] && double.TryParse(left, CultureInfo.InvariantCulture, out var l) && double.TryParse(top, CultureInfo.InvariantCulture, out var t) && IsPointVisible((int)l, (int)t))
            {
                list.Left = l;
                list.Top = t;
            }
            else if (!IsOnAnyScreen(new WindowInteropHelper(list).Handle))
            {
                list.Left = System.Windows.SystemParameters.WorkArea.Right - list.Width - 40;
                list.Top = 80;
            }
        }
    }

    /// <summary>Khớp <c>TaskId</c> trong AppxManifest.WinUI.xml.</summary>
    public const string PackagedStartupTaskId = "HabitGridWidgetsStartup";

    private async Task ApplyAutoStartAsync()
    {
        try
        {
            // Mặc định bật; cài đặt "widgets.autostart" = "0" (do app chính ghi) thì gỡ.
            var enabled = await _settings.GetAsync("widgets.autostart") != "0";

            // Trong gói MSIX: dùng StartupTask của gói (khóa Run trỏ vào WindowsApps sẽ chạy KHÔNG có danh tính gói và đọc nhầm CSDL).
            if (HabitGrid.Core.Services.PackageIdentity.IsPackaged)
            {
                var task = await global::Windows.ApplicationModel.StartupTask.GetAsync(PackagedStartupTaskId);

                if (enabled && task.State == global::Windows.ApplicationModel.StartupTaskState.Disabled)
                {
                    await task.RequestEnableAsync();
                }
                else if (!enabled && task.State == global::Windows.ApplicationModel.StartupTaskState.Enabled)
                {
                    task.Disable();
                }

                return;
            }

            var exe = Environment.ProcessPath;

            if (exe is not null)
            {
                _autoStart.Apply(enabled, exe);
            }
        }
        catch (Exception)
        {
            // Đăng ký khởi động là tiện ích, không được làm hỏng widget.
        }
    }

    private static bool IsOnAnyScreen(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var rect) && MonitorFromRect(ref rect, 0) != IntPtr.Zero;

    private static bool IsPointVisible(int x, int y)
    {
        var rect = new NativeRect { Left = x, Top = y, Right = x + 40, Bottom = y + 40 };
        return MonitorFromRect(ref rect, 0) != IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);

    public void Dispose()
    {
        _timer.Stop();
        _shell?.Dispose();
        _hotKey?.Dispose();
        _focusHotKey?.Dispose();
        Focus.Dispose();
        FocusWindow?.Close();
        _todayList?.Close();
        _momentWidget?.Close();
        ResetExtras();
        QuickWindow?.Close();

        foreach (var (window, _) in _widgets.Values)
        {
            window.Close();
        }

        _widgets.Clear();
        _watch.Dispose();
    }

    /// <summary>Gọi khi cần làm mới ngay (sau check-in, sau khi thức dậy từ sleep).</summary>
    public async Task RefreshAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;

        try
        {
            var today = CurrentDay();
            var dayChanged = today != _today;
            _today = today;

            // Free: một widget lưới; Plus: tối đa ba (WidgetRules).
            var maxGrids = HabitGrid.Core.Domain.Widgets.WidgetRules.MaxGrids(await IsPlusAsync());
            var habits = (await _habits.GetAllActiveAsync())
                .Where(h => h.Kind != HabitKind.Quit)
                .OrderBy(h => h.SortKey, StringComparer.Ordinal)
                .Take(Math.Min(MaxWidgets, maxGrids))
                .ToList();

            foreach (var stale in _widgets.Keys.Where(id => habits.All(h => h.Id != id)).ToList())
            {
                _widgets[stale].Window.Close();
                _widgets.Remove(stale);
            }

            var dark = IsSystemDark();
            await _quick.LoadAsync(today);
            _todayList?.ApplyTheme(dark);
            QuickWindow?.ApplyTheme(dark);
            QuickWindow?.Refresh();

            for (var index = 0; index < habits.Count; index++)
            {
                var habit = habits[index];
                var logs = await _logs.GetAllForHabitAsync(habit.Id);
                var values = WidgetMapper.ToValues(habit, logs);

                if (_widgets.TryGetValue(habit.Id, out var existing))
                {
                    if (!dayChanged)
                    {
                        existing.Model.UpdateValues(values);
                        continue;
                    }

                    existing.Window.Close();
                }

                await CreateAsync(habit, values, dark, index);
            }

            // Dữ liệu đổi (check-in, sửa thói quen, qua ngày): lập lại lịch nhắc; xong trước giờ nhắc thì toast bị gỡ.
            await Toasts.ReplanAsync(DateTime.Now);

            // Check-in/đổi dữ liệu: widget Plus cũng phải cập nhật ngay.
            await SyncExtraWidgetsAsync();
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>Thao tác từ Today List/Quick check-in: delta 0 = bật-tắt/đếm +1, khác 0 = tăng/giảm.</summary>
    public async Task ActAsync(QuickItem item, int delta)
    {
        if (delta == 0)
        {
            await _quick.ToggleAsync(item);
        }
        else
        {
            await _quick.AdjustAsync(item, delta);
        }

        await RefreshAsync();
    }

    private async Task CreateTodayListAsync()
    {
        var list = new TodayListWindow(_quick, ActAsync);
        _todayList = list;
        list.ApplyTheme(IsSystemDark());
        list.Show();
        list.Moved += async (_, _) =>
            await _settings.SetAsync(TodayListKey, string.Create(CultureInfo.InvariantCulture, $"{(int)list.Left},{(int)list.Top}"));

        var saved = await _settings.GetAsync(TodayListKey);

        if (saved?.Split(',') is [var left, var top] && double.TryParse(left, CultureInfo.InvariantCulture, out var l) && double.TryParse(top, CultureInfo.InvariantCulture, out var t))
        {
            list.Left = l;
            list.Top = t;
        }
        else
        {
            list.Left = System.Windows.SystemParameters.WorkArea.Right - list.Width - 40;
            list.Top = 80;
        }

        if (list.Pin(_desktop, DesktopPinningMode.DesktopLevel).UsedFallback)
        {
            _needsRepin = true;
        }
        else
        {
            await _settings.SetAsync("flag.widget_pinned", "1");
        }
    }

    private async Task CreateAuxiliaryWindowsAsync()
    {
        await CreateTodayListAsync();

        QuickWindow = new QuickCheckInWindow(_quick, day => _quick.LoadAsync(day), async (item, delta) =>
        {
            await ActAsync(item, delta);
            await _settings.SetAsync("flag.quick_checkin_used", "1");
        });
        QuickWindow.ApplyTheme(IsSystemDark());
        QuickWindow.Prewarm();
    }

    /// <summary>Đăng ký/gỡ phím tắt toàn cục theo cài đặt (tắt được trong Cài đặt của app chính).</summary>
    private async Task ApplyHotKeysAsync()
    {
        var quick = await _settings.GetAsync(ReminderSettingsStore.QuickHotKeyEnabledKey) != "0";
        var focus = await _settings.GetAsync(ReminderSettingsStore.FocusHotKeyEnabledKey) != "0";

        if (quick && _hotKey is null)
        {
            _hotKey = new HotKey('H');
            _hotKey.Pressed += async (_, _) =>
            {
                try
                {
                    if (QuickWindow is not null)
                    {
                        await QuickWindow.PresentAsync(CurrentDay());
                    }
                }
                catch (Exception)
                {
                    // Không để lỗi hiển thị làm sập tiến trình nền.
                }
            };
        }
        else if (!quick && _hotKey is not null)
        {
            _hotKey.Dispose();
            _hotKey = null;
        }

        if (focus && _focusHotKey is null)
        {
            _focusHotKey = new HotKey('F');
            _focusHotKey.Pressed += async (_, _) => await ToggleFocusAsync();
        }
        else if (!focus && _focusHotKey is not null)
        {
            _focusHotKey.Dispose();
            _focusHotKey = null;
        }

        await _settings.SetAsync("hotkey.quick.registered", _hotKey?.Description ?? string.Empty);
        await _settings.SetAsync("hotkey.focus.registered", _focusHotKey?.Description ?? string.Empty);
    }

    /// <summary>Win+Alt+F: hiện mini-player và bắt đầu/tạm dừng/tiếp tục.</summary>
    public async Task ToggleFocusAsync()
    {
        try
        {
            await Focus.ToggleAsync();

            if (FocusWindow is not null)
            {
                await FocusWindow.PresentAsync();
            }
        }
        catch (Exception)
        {
            // Không để lỗi Focus làm sập tiến trình nền.
        }
    }

    /// <summary>Nút bấm trên toast (kể cả khi Windows vừa khởi động WidgetHost chỉ để xử lý nút).</summary>
    public async Task HandleToastAsync(ToastArguments args)
    {
        if (await Toasts.HandleAsync(args, OpenMainApp))
        {
            await RefreshAsync();
        }
    }

    public Task OnSuspendAsync() => Focus.OnSuspendAsync();

    public async Task OnResumeAsync()
    {
        await RefreshAsync();

        if (Focus.PausedBySleep && FocusWindow is not null)
        {
            await FocusWindow.PresentAsync();
        }
    }

    /// <summary>Lệnh từ app chính qua bảng settings (đơn giản, không cần named pipe): đọc rồi xóa.</summary>
    private async Task ProcessHostCommandAsync()
    {
        var command = await _settings.GetAsync(ReminderSettingsStore.HostCommandKey);

        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        await _settings.RemoveAsync(ReminderSettingsStore.HostCommandKey);
        var parts = command.Split('|');

        switch (parts[0])
        {
            case "focus.start":
                Guid? habit = parts.Length > 1 && Guid.TryParse(parts[1], out var id) ? id : null;
                int? minutes = parts.Length > 2 && int.TryParse(parts[2], CultureInfo.InvariantCulture, out var m) ? m : null;
                await Focus.StartAsync(minutes: minutes, habitId: habit, keepHabit: false);

                if (FocusWindow is not null)
                {
                    await FocusWindow.PresentAsync();
                }

                break;
            case "focus.toggle":
                await ToggleFocusAsync();
                break;
            case "focus.stop":
                await Focus.StopAsync();
                break;
            case "focus.show":
                if (FocusWindow is not null)
                {
                    await FocusWindow.PresentAsync();
                }

                break;
            case "quick.show":
                if (QuickWindow is not null)
                {
                    await QuickWindow.PresentAsync(CurrentDay());
                }

                break;
        }
    }

    private const string TodayListKey = "widget.pos.todaylist";

    /// <summary>Check-in một ngày rồi làm mới. Dùng cho widget bấm ô.</summary>
    public async Task CheckInAsync(Guid habitId, DateOnly day)
    {
        await _checkIn.ToggleAsync(habitId, day);
        await RefreshAsync();
    }

    private async Task CreateAsync(Habit habit, Dictionary<DateOnly, int> values, bool dark, int index)
    {
        var shades = _palette.GenerateFromHex(habit.ColorHue, dark);
        var model = new WidgetViewModel(WidgetMapper.ToWidgetHabit(habit), values, shades, _today, V1.WidgetSize.Large, dark);
        var window = new WidgetWindow(_screen);

        window.Bind(model);
        window.UseDesktopHost(_desktop);
        window.ApplyThemeColors(dark);
        window.Show();

        model.MarkRequested += async (_, day) => await CheckInAsync(habit.Id, day);
        model.CloseRequested += (_, _) => window.Hide();
        window.PositionChanged += async (_, args) =>
            await SavePositionAsync(PositionKey(habit.Id), string.Create(CultureInfo.InvariantCulture, $"{args.Left},{args.Top}"));

        var saved = await LoadPositionAsync(PositionKey(habit.Id));

        if (saved?.Split(',') is [var left, var top] && int.TryParse(left, out var l) && int.TryParse(top, out var t))
        {
            window.SetScreenPosition(l, t);
        }
        else
        {
            window.SetScreenPosition(60 + index * 36, 80 + index * 160);
        }

        if (window.ApplyPinning(DesktopPinningMode.DesktopLevel).UsedFallback)
        {
            _needsRepin = true;
        }

        _widgets[habit.Id] = (window, model);
    }

    private static string PositionKey(Guid id) => "widget.pos." + id.ToString("N");

    private async Task PollAsync()
    {
        try
        {
            if (EditMode?.Invoke() is { } edit && edit != _editApplied)
            {
                _editApplied = edit;

                foreach (var (window, _) in _widgets.Values)
                {
                    window.SetEditMode(edit);
                }

                if (_todayList is not null)
                {
                    _todayList.EditMode = edit;
                }

                if (_momentWidget is not null)
                {
                    _momentWidget.EditMode = edit;
                }

                foreach (var extra in _extras.Values)
                {
                    extra.EditMode = edit;
                }
            }

            _pollCount++;

            // Explorer khởi động lại: widget (con của WorkerW cũ) đã bị hủy → dựng lại. Cũng bắt trường hợp
            // không nhận được TaskbarCreated bằng cách kiểm tra handle còn sống.
            var (alive, total) = WindowHealth();

            if ((_rebuildRequested && DateTime.UtcNow >= _rebuildNotBefore) || alive < total)
            {
                await RebuildAllAsync();
                return;
            }

            // WorkerW chưa sẵn sàng lúc ghim (Explorer vừa khởi động): thử lại vài lần.
            if (_needsRepin && _pinRetries < 15)
            {
                _pinRetries++;
                ReattachAll();
            }

            if (_replaceRequested)
            {
                await ReplaceAllAsync();
            }

            // Cài đặt chạy cùng Windows có thể đổi từ app chính: kiểm tra mỗi ~30 giây.
            if (_pollCount % 15 == 0)
            {
                await ApplyAutoStartAsync();
                await ApplyHotKeysAsync();
                await SyncMomentWidgetAsync();
                await SyncExtraWidgetsAsync();
            }

            await ProcessHostCommandAsync();

            var version = ReadDataVersion();

            if (version != _dataVersion || CurrentDay() != _today)
            {
                _dataVersion = version;
                await RefreshAsync();
            }
        }
        catch (Exception)
        {
            // Lần sau thử lại; widget giữ nguyên nội dung cũ.
        }
    }

    private long ReadDataVersion()
    {
        using var command = _watch.CreateCommand();
        command.CommandText = "PRAGMA data_version";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static DateOnly CurrentDay() => new DayProvider(TimeSpan.Zero).GetToday();

    private static bool IsSystemDark()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }
}
