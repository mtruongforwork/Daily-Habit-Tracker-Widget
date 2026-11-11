using System.Globalization;
using HabitGrid.Core.Domain.Widgets;
using HabitGrid.Platform.Desktop;

namespace HabitGrid.WidgetHost;

/// <summary>
/// Chế độ "một widget gộp" (mặc định): thay cho các widget riêng lẻ, chỉ có một cửa sổ nhỏ gọn mở rộng được.
/// Chọn chế độ trong Cài đặt → Widget (<c>widgets.layout</c>). Free thấy Buddy + Hôm nay; Plus thêm Mục tiêu và lưới thói quen.
/// </summary>
public sealed partial class WidgetController
{
    private const string HubPositionKey = "widget.pos.hub";
    private HubWidgetWindow? _hub;
    private WidgetPlacementMode _placement;
    private bool _placementApplied;
    private string? _islandSignature;
    private WidgetLayoutMode? _appliedLayout;
    private bool? _appliedGlass;

    /// <summary>Khóa cài đặt kính mờ của widget ("1" = kính mờ Acrylic; mặc định là kiểu Dynamic Island).</summary>
    public const string GlassKey = "widgets.glass";

    /// <summary>Đọc cài đặt kính mờ; đổi thì dựng lại cửa sổ để cửa sổ mới dùng đúng kiểu (kiểu cửa sổ không đổi lúc đang chạy được).</summary>
    private async Task ApplyGlassAsync(bool initial)
    {
        // Mặc định kiểu Dynamic Island (thẻ gần đen, bo tròn); "1" = kính mờ Acrylic.
        var glass = await _settings.GetAsync(GlassKey) == "1";
        ExtraWidgetWindow.UseGlass = glass;

        if (_appliedGlass == glass)
        {
            return;
        }

        var changed = _appliedGlass is not null;
        _appliedGlass = glass;

        if (changed && !initial)
        {
            await RebuildAllAsync();
        }
    }

    /// <summary>Chế độ hiển thị mặc định (tự kiểm tra trên CI dùng Separate để kiểm các widget riêng).</summary>
    public WidgetLayoutMode DefaultLayout { get; init; } = WidgetLayoutMode.Hub;

    public WidgetLayoutMode LayoutMode => _appliedLayout ?? DefaultLayout;

    public HubWidgetWindow? Hub => _hub;

    /// <summary>Đọc chế độ hiển thị từ cài đặt; đổi thì dựng lại toàn bộ cửa sổ.</summary>
    private async Task ApplyLayoutModeAsync(bool initial)
    {
        var stored = await _settings.GetAsync(WidgetLayoutModes.SettingKey);
        var mode = stored is null ? DefaultLayout : WidgetLayoutModes.Parse(stored);

        if (_appliedLayout == mode)
        {
            return;
        }

        var changed = _appliedLayout is not null;
        _appliedLayout = mode;

        if (changed && !initial)
        {
            await RebuildAllAsync();
        }
    }

    /// <summary>Vị trí đang áp dụng của widget gộp (giữa mép trên = kiểu "Dynamic Island").</summary>
    public WidgetPlacementMode Placement => _placement;

    /// <summary>
    /// Đọc vị trí từ cài đặt (<c>widgets.placement</c>); đổi thì áp cho widget gộp và đặt lại vị trí. Giữa mép trên luôn nằm trên cùng
    /// (xem <see cref="ApplyPinChoiceAsync"/>), nên phải gọi trước khi áp kiểu ghim.
    /// </summary>
    private async Task ApplyPlacementAsync(bool initial)
    {
        var placement = WidgetPlacements.Parse(await _settings.GetAsync(WidgetPlacements.SettingKey));

        if (_placementApplied && _placement == placement)
        {
            return;
        }

        _placementApplied = true;
        _placement = placement;

        if (_hub is not null)
        {
            _hub.Placement = placement;
            await PositionHubAsync();
        }
    }

    /// <summary>Chữ ký của màn hình chính (kích thước DIP + DPI): đổi độ phân giải, DPI hoặc cắm/rút màn hình làm chữ ký đổi.</summary>
    private string PrimarySignature()
    {
        var dpi = _hub is null ? 1.0 : System.Windows.Media.VisualTreeHelper.GetDpi(_hub).DpiScaleX;
        return string.Create(CultureInfo.InvariantCulture, $"{System.Windows.SystemParameters.PrimaryScreenWidth:0.#}x{System.Windows.SystemParameters.PrimaryScreenHeight:0.#}@{dpi:0.###}");
    }

    /// <summary>Đặt widget gộp: giữa mép trên màn hình chính (cách 6 px vật lý) ở chế độ Dynamic Island, ngược lại về vị trí đã lưu hoặc mặc định.</summary>
    private async Task PositionHubAsync()
    {
        if (_hub is not { } hub)
        {
            return;
        }

        if (_placement == WidgetPlacementMode.TopCenter)
        {
            // Màn hình chính luôn có gốc (0,0) theo DIP của WPF; dùng DIP thẳng để khỏi lệch khi DPI hệ thống khác DPI màn hình.
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(hub).DpiScaleX;
            var widthPx = (int)Math.Round(hub.NominalSize.Width * dpi);
            var screenPx = (int)Math.Round(System.Windows.SystemParameters.PrimaryScreenWidth * dpi);
            var slot = WidgetLayout.TopCenter(0, 0, screenPx, widthPx);
            hub.PlaceAt(slot.Left / dpi, slot.Top / dpi);
            _islandSignature = PrimarySignature();
            return;
        }
        var saved = await LoadPositionAsync(HubPositionKey);

        if (saved?.Split(',') is [var left, var top] && double.TryParse(left, CultureInfo.InvariantCulture, out var l) && double.TryParse(top, CultureInfo.InvariantCulture, out var t) && IsPointVisible((int)l, (int)t))
        {
            hub.PlaceAt(l, t);
        }
        else
        {
            var slot = await DefaultSlotAsync("hub");
            hub.PlaceAt(slot.Left, slot.Top);
        }
    }

    private readonly HashSet<string> _remindersShown = [];
    private readonly List<(string Id, DateTime DueAt, HabitGrid.Core.Domain.Reminders.ToastKind Kind, IReadOnlyList<Guid> Ids, DateOnly Day)> _islandSnoozed = [];

    /// <summary>
    /// Nhắc nhở đến giờ thì bung ra từ viên thuốc Island với hai nút "Xong" / "Để sau". Toast Windows vẫn được lên lịch như cũ làm dự phòng
    /// (khi widget không chạy hoặc đang bận); bấm nút trên viên thuốc thì toast cùng tag bị xoá khỏi Trung tâm thông báo.
    /// </summary>
    private async Task ShowDueIslandRemindersAsync(DateTime now)
    {
        if (_placement != WidgetPlacementMode.TopCenter || _hub is not { } hub || !hub.CanPeek)
        {
            return;
        }

        var due = Toasts.LastPlan
            .Where(p => p.Kind != HabitGrid.Core.Domain.Reminders.ToastKind.MorningSummary && now >= p.At && now < p.At.AddMinutes(3) && !_remindersShown.Contains(p.Id))
            .Select(p => (p.Id, p.Kind, Ids: p.HabitIds, Day: DateOnly.FromDateTime(p.At)))
            .Concat(_islandSnoozed.Where(s => now >= s.DueAt && !_remindersShown.Contains(s.Id)).Select(s => (s.Id, s.Kind, s.Ids, s.Day)))
            .FirstOrDefault();

        if (due.Id is null)
        {
            return;
        }

        // Chỉ nhắc những việc còn chưa xong hôm nay.
        var open = _quick.Items.Where(i => due.Ids.Contains(i.Id) && !i.IsDone && !i.IsSkipped).ToList();

        if (open.Count == 0)
        {
            _remindersShown.Add(due.Id);
            return;
        }

        var ids = open.Select(i => i.Id).ToList();
        var title = ToastScheduler.TitleFor(due.Kind, open.Select(i => i.Name).ToList());
        var csv = string.Join(',', ids);
        var day = due.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var id = due.Id;
        var kind = due.Kind;

        async Task ActAsync(string action)
        {
            var args = new CommunityToolkit.WinUI.Notifications.ToastArguments()
                .Add("action", action).Add("ids", csv).Add("day", day).Add("kind", kind.ToString());
            await Toasts.HandleAsync(args, OpenMainApp);

            try
            {
                CommunityToolkit.WinUI.Notifications.ToastNotificationManagerCompat.History.Remove(id, ToastScheduler.Group);
            }
            catch (Exception)
            {
                // Toast chưa hiện/đã bị xoá: không sao.
            }

            if (action == "snooze")
            {
                _islandSnoozed.Add((id + ":later:" + now.Ticks, DateTime.Now.AddMinutes(30), kind, ids, due.Day));
            }

            await RefreshAsync();
        }

        if (hub.ShowReminder(title, () => ActAsync("done"), () => ActAsync("snooze")))
        {
            _remindersShown.Add(due.Id);

            if (_remindersShown.Count > 300)
            {
                _remindersShown.Clear();
            }
        }
    }

    /// <summary>Mỗi nhịp: màn hình chính đổi (độ phân giải/DPI/cắm màn hình) thì tính lại vị trí giữa mép trên.</summary>
    private async Task EnsureIslandPositionAsync()
    {
        if (_placement != WidgetPlacementMode.TopCenter || _hub is null)
        {
            return;
        }

        if (_islandSignature != PrimarySignature())
        {
            await PositionHubAsync();
        }
    }

    /// <summary>Tạo/cập nhật widget gộp (hoặc đóng nếu không ở chế độ Hub).</summary>
    private async Task SyncHubAsync()
    {
        if (LayoutMode != WidgetLayoutMode.Hub)
        {
            CloseHub();
            return;
        }

        var plus = await IsPlusAsync();
        var sections = WidgetLayoutModes.Sections(plus);

        if (_hub is null)
        {
            _hub = await CreateHubAsync();
        }

        _hub.Placement = _placement;
        var buddy = await Data().BuddyAsync();
        var goals = sections.HasFlag(HubSection.Goals) ? await Data().GoalsAsync() : null;
        var grid = sections.HasFlag(HubSection.Grid) ? await Data().MultiGridAsync() : null;
        _hub.Update(new HubData(buddy, _quick.Items, goals, grid));
    }

    private async Task<HubWidgetWindow> CreateHubAsync()
    {
        var window = new HubWidgetWindow { Placement = _placement, EditMode = _editApplied, OnToggle = item => ActAsync(item, 0), OnAdjust = ActAsync, FocusProvider = FocusSnapshotNow };
        window.Show();
        window.StartLiveActivities();
        window.Moved += async (_, _) =>
        {
            SnapToGrid(window);
            await SavePositionAsync(HubPositionKey, string.Create(CultureInfo.InvariantCulture, $"{(int)window.Left},{(int)window.Top}"));
        };

        _hub = window;
        await PositionHubAsync();

        if (window.Pin(_desktop, DesktopPinningMode.DesktopLevel).UsedFallback)
        {
            _needsRepin = true;
        }

        return window;
    }

    /// <summary>Phiên Focus hiện tại (nếu đang làm hoặc nghỉ) cho đồng hồ trên viên thuốc.</summary>
    private FocusSnapshot? FocusSnapshotNow()
    {
        if (Focus.Timer is not { } timer || timer.Phase is not (HabitGrid.Core.Domain.Focus.FocusPhase.Work or HabitGrid.Core.Domain.Focus.FocusPhase.Break))
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var isBreak = timer.Phase == HabitGrid.Core.Domain.Focus.FocusPhase.Break;
        var habit = timer.HabitId is { } id ? _quick.Items.FirstOrDefault(i => i.Id == id)?.Name : null;
        var shown = timer.Remaining(now) ?? timer.Elapsed(now);
        var total = TimeSpan.FromSeconds(isBreak ? timer.State.BreakSeconds : timer.State.WorkSeconds);
        var fraction = timer.Remaining(now) is { } left && total > TimeSpan.Zero ? 1 - (left.TotalSeconds / total.TotalSeconds) : 0;
        var clock = $"{(int)shown.TotalMinutes:00}:{shown.Seconds:00}";
        var label = (isBreak ? "☕ " : "⏱ ") + clock + (habit is null ? string.Empty : "  " + habit) + (timer.IsPaused ? "  ⏸" : string.Empty);
        return new FocusSnapshot(label, Math.Clamp(fraction, 0, 1), habit, timer.IsPaused, isBreak, clock);
    }

    private void CloseHub()
    {
        if (_hub is null)
        {
            return;
        }

        TryClose(_hub);
        _hub = null;
    }

    /// <summary>Đổi vị trí widget gộp (chỉ dùng cho tự kiểm tra trên CI): ghi cài đặt rồi áp ngay như nhịp poll.</summary>
    public async Task SetPlacementForTestAsync(WidgetPlacementMode placement)
    {
        await _settings.SetAsync(WidgetPlacements.SettingKey, WidgetPlacements.ToStored(placement));
        await ApplyPlacementAsync(initial: false);
        await ApplyPinChoiceAsync(initial: false);
        await SyncHubAsync();
    }

    /// <summary>Đổi chế độ hiển thị (chỉ dùng cho tự kiểm tra trên CI).</summary>
    public async Task SetLayoutForTestAsync(WidgetLayoutMode mode)
    {
        await _settings.SetAsync(WidgetLayoutModes.SettingKey, WidgetLayoutModes.ToStored(mode));
        await ApplyLayoutModeAsync(initial: false);
    }
}
