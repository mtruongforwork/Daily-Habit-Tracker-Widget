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

    /// <summary>Khóa cài đặt bật/tắt kính mờ của widget ("0" = tắt; mặc định bật).</summary>
    public const string GlassKey = "widgets.glass";

    /// <summary>Đọc cài đặt kính mờ; đổi thì dựng lại cửa sổ để cửa sổ mới dùng đúng kiểu (kiểu cửa sổ không đổi lúc đang chạy được).</summary>
    private async Task ApplyGlassAsync(bool initial)
    {
        var glass = await _settings.GetAsync(GlassKey) != "0";
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
        var window = new HubWidgetWindow { EditMode = _editApplied, OnToggle = item => ActAsync(item, 0) };
        window.Show();
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
