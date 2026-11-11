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

        var buddy = await Data().BuddyAsync();
        var goals = sections.HasFlag(HubSection.Goals) ? await Data().GoalsAsync() : null;
        var grid = sections.HasFlag(HubSection.Grid) ? await Data().MultiGridAsync() : null;
        _hub.Update(new HubData(buddy, _quick.Items, goals, grid));
    }

    private async Task<HubWidgetWindow> CreateHubAsync()
    {
        var window = new HubWidgetWindow { EditMode = _editApplied, OnToggle = item => ActAsync(item, 0), FocusProvider = FocusSnapshotNow };
        window.Show();
        window.StartLiveActivities();
        window.Moved += async (_, _) =>
        {
            SnapToGrid(window);
            await SavePositionAsync(HubPositionKey, string.Create(CultureInfo.InvariantCulture, $"{(int)window.Left},{(int)window.Top}"));
        };

        var saved = await LoadPositionAsync(HubPositionKey);

        if (saved?.Split(',') is [var left, var top] && double.TryParse(left, CultureInfo.InvariantCulture, out var l) && double.TryParse(top, CultureInfo.InvariantCulture, out var t) && IsPointVisible((int)l, (int)t))
        {
            window.Left = l;
            window.Top = t;
        }
        else
        {
            var slot = await DefaultSlotAsync("hub");
            window.Left = slot.Left;
            window.Top = slot.Top;
        }

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
        return new FocusSnapshot(label, Math.Clamp(fraction, 0, 1), habit, timer.IsPaused, isBreak);
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

    /// <summary>Đổi chế độ hiển thị (chỉ dùng cho tự kiểm tra trên CI).</summary>
    public async Task SetLayoutForTestAsync(WidgetLayoutMode mode)
    {
        await _settings.SetAsync(WidgetLayoutModes.SettingKey, WidgetLayoutModes.ToStored(mode));
        await ApplyLayoutModeAsync(initial: false);
    }
}
