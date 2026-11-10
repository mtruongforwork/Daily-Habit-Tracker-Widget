using System.Globalization;
using System.IO;
using HabitGrid.Core.Services;
using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Moments;
using HabitGrid.Platform.Desktop;
using HabitGrid.Presentation.Services;

namespace HabitGrid.WidgetHost;

/// <summary>Widget khoảnh khắc (Plus): bật khi app chính báo có Plus (<c>plan.isPlus</c>) và người dùng không tắt trong Cài đặt.</summary>
public sealed partial class WidgetController
{
    public const string MomentWidgetEnabledKey = "widgets.moment.enabled";
    private const string MomentPositionKey = "widget.pos.moment";

    private MomentWidgetWindow? _momentWidget;
    private MomentService? _moments;

    /// <summary>Thư mục dữ liệu (ảnh nằm ở <c>{DataDir}/moments</c>).</summary>
    public string? DataDir { get; init; }

    public MomentWidgetWindow? MomentWidget => _momentWidget;

    /// <summary>Tạo/đóng widget theo quyền + cài đặt, rồi cập nhật ảnh. Gọi khi khởi động, khi dữ liệu đổi và mỗi ~30 giây.</summary>
    public async Task SyncMomentWidgetAsync()
    {
        if (DataDir is null)
        {
            return;
        }

        var allowed = await _settings.GetAsync(PlusService.PlusFlagKey) == "1"
                      && await _settings.GetAsync(MomentWidgetEnabledKey) != "0";

        if (!allowed)
        {
            if (_momentWidget is not null)
            {
                _momentWidget.Close();
                _momentWidget = null;
            }

            return;
        }

        _moments ??= new MomentService(new MomentStore(Path.Combine(DataDir, "moments")), new FreeEntitlements(), new DayProvider(TimeSpan.Zero));

        if (_momentWidget is null)
        {
            await CreateMomentWidgetAsync();
        }

        var habits = (await _habits.GetAllActiveAsync()).ToDictionary(h => h.Id, h => h.Name);
        var active = habits.Keys.ToList();
        _momentWidget!.Update(_moments.Latest(active), _moments.OneMonthAgo(CurrentDay(), active), habits);
    }

    private async Task CreateMomentWidgetAsync()
    {
        var widget = new MomentWidgetWindow { EditMode = _editApplied };
        _momentWidget = widget;
        widget.Show();
        widget.Moved += async (_, _) =>
            await _settings.SetAsync(MomentPositionKey, string.Create(CultureInfo.InvariantCulture, $"{(int)widget.Left},{(int)widget.Top}"));

        var saved = await _settings.GetAsync(MomentPositionKey);

        if (saved?.Split(',') is [var left, var top] && double.TryParse(left, CultureInfo.InvariantCulture, out var l) && double.TryParse(top, CultureInfo.InvariantCulture, out var t))
        {
            widget.Left = l;
            widget.Top = t;
        }
        else
        {
            var slot = await DefaultSlotAsync("moment");
            widget.Left = slot.Left;
            widget.Top = slot.Top;
        }

        if (widget.Pin(_desktop, DesktopPinningMode.DesktopLevel).UsedFallback)
        {
            _needsRepin = true;
        }
    }

    /// <summary>Explorer khởi động lại: đóng widget cũ để lần đồng bộ sau dựng lại.</summary>
    private void ResetMomentWidget()
    {
        if (_momentWidget is null)
        {
            return;
        }

        try
        {
            _momentWidget.Close();
        }
        catch (Exception)
        {
            // Đã bị hủy cùng Explorer.
        }

        _momentWidget = null;
    }
}
