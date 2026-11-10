using System.Globalization;
using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Monetization;
using HabitGrid.Core.Domain.Widgets;
using HabitGrid.Data.V3;
using HabitGrid.Platform.Desktop;
using HabitGrid.Presentation.Services;
using HabitGrid.Core.Services;

namespace HabitGrid.WidgetHost;

/// <summary>
/// Widget Plus (M1): Buddy, Mục tiêu, Multi Grid. Hiện khi app chính báo có Plus (<c>plan.isPlus</c>) và người dùng bật trong Cài đặt
/// (mặc định tắt). Hết Plus thì ẩn nhưng cấu hình bật/tắt và vị trí vẫn giữ. Dữ liệu do <see cref="WidgetDataService"/> dựng.
/// </summary>
public sealed partial class WidgetController
{
    private readonly Dictionary<ExtraWidget, ExtraWidgetWindow> _extras = [];
    private readonly LiveEntitlements _entitlements = new();
    private WidgetDataService? _data;

    /// <summary>Các widget Plus đang hiển thị (kiểm tra tự động và ghim lại dùng).</summary>
    public IReadOnlyDictionary<ExtraWidget, ExtraWidgetWindow> Extras => _extras;

    private WidgetDataService Data()
    {
        if (_data is not null)
        {
            return _data;
        }

        var clock = new HlcClock(Environment.MachineName + ":widgets-extra");
        var days = new DayProvider(TimeSpan.Zero);
        var profile = new ProfileRepository(_database, clock, null);
        var wardrobe = new BuddyWardrobeService(_settings, profile, new AchievementRepository(_database, clock, null), _entitlements, days);
        var challenges = new ChallengeService(_settings, _habits, _logs, _entitlements, days);
        var weekly = new WeeklyRecapService(_habits, _logs, _settings, days);
        return _data = new WidgetDataService(_habits, _logs, profile, wardrobe, challenges, weekly, days);
    }

    /// <summary>App chính đã báo có Plus (kể cả đang dùng thử).</summary>
    private async Task<bool> IsPlusAsync()
    {
        var plus = await _settings.GetAsync(PlusService.PlusFlagKey) == "1";
        _entitlements.Set(plus);
        return plus;
    }

    /// <summary>Tạo/đóng từng widget theo quyền + cài đặt rồi cập nhật dữ liệu. Gọi khi khởi động, khi dữ liệu đổi và mỗi ~30 giây.</summary>
    public async Task SyncExtraWidgetsAsync()
    {
        var plus = await IsPlusAsync();

        foreach (var kind in Enum.GetValues<ExtraWidget>())
        {
            var show = WidgetRules.ShouldShow(kind, plus, await _settings.GetAsync(WidgetRules.EnabledKey(kind)));

            if (!show)
            {
                if (_extras.Remove(kind, out var closing))
                {
                    TryClose(closing);
                }

                continue;
            }

            if (!_extras.TryGetValue(kind, out var window))
            {
                window = await CreateExtraAsync(kind);
                _extras[kind] = window;
            }

            switch (window)
            {
                case BuddyWidgetWindow buddy:
                    buddy.Update(await Data().BuddyAsync());
                    break;
                case GoalsWidgetWindow goals:
                    goals.Update(await Data().GoalsAsync());
                    break;
                case MultiGridWidgetWindow grid:
                    grid.Update(await Data().MultiGridAsync());
                    break;
            }
        }
    }

    private async Task<ExtraWidgetWindow> CreateExtraAsync(ExtraWidget kind)
    {
        ExtraWidgetWindow window = kind switch
        {
            ExtraWidget.Buddy => new BuddyWidgetWindow(),
            ExtraWidget.Goals => new GoalsWidgetWindow(),
            _ => new MultiGridWidgetWindow()
        };
        window.EditMode = _editApplied;
        window.Show();

        var key = WidgetRules.PositionKey(kind);
        window.Moved += async (_, _) =>
        {
            SnapToGrid(window);
            await SavePositionAsync(key, string.Create(CultureInfo.InvariantCulture, $"{(int)window.Left},{(int)window.Top}"));
        };

        var saved = await LoadPositionAsync(key);

        if (saved?.Split(',') is [var left, var top] && double.TryParse(left, CultureInfo.InvariantCulture, out var l) && double.TryParse(top, CultureInfo.InvariantCulture, out var t))
        {
            window.Left = l;
            window.Top = t;
        }
        else
        {
            // Xếp lệch nhau ở góc phải để mới bật không chồng lên nhau.
            var index = (int)kind;
            window.Left = System.Windows.SystemParameters.WorkArea.Right - window.Width - 40;
            window.Top = 60 + index * 170;
        }

        if (window.Pin(_desktop, DesktopPinningMode.DesktopLevel).UsedFallback)
        {
            _needsRepin = true;
        }

        return window;
    }

    /// <summary>Chữ ký cấu hình màn hình hiện tại (vị trí widget được nhớ theo chữ ký này).</summary>
    private string ScreenSignature() =>
        ScreenLayout.Signature(_screen.GetScreens().Select(s => new ScreenLayout.Monitor(s.DevicePath, s.WorkArea.Left, s.WorkArea.Top, s.WorkArea.Width, s.WorkArea.Height)));

    /// <summary>Đọc vị trí theo cấu hình màn hình hiện tại; chưa có thì dùng vị trí chung gần nhất.</summary>
    private async Task<string?> LoadPositionAsync(string baseKey) =>
        await _settings.GetAsync(ScreenLayout.KeyFor(baseKey, ScreenSignature())) ?? await _settings.GetAsync(baseKey);

    /// <summary>Ghi vị trí cho cấu hình màn hình hiện tại và làm vị trí chung (dự phòng khi gặp cấu hình mới).</summary>
    private async Task SavePositionAsync(string baseKey, string value)
    {
        await _settings.SetAsync(ScreenLayout.KeyFor(baseKey, ScreenSignature()), value);
        await _settings.SetAsync(baseKey, value);
    }

    /// <summary>Căn widget Plus vào lưới 8 px của màn hình đang chứa nó (giống widget lưới); đổi giữa DIP của WPF và pixel vật lý.</summary>
    private void SnapToGrid(ExtraWidgetWindow window)
    {
        var scale = System.Windows.Media.VisualTreeHelper.GetDpi(window).DpiScaleX;
        var left = (int)Math.Round(window.Left * scale);
        var top = (int)Math.Round(window.Top * scale);

        if (_screen.GetScreenAt(left, top) is not { } screen)
        {
            return;
        }

        var (snappedLeft, snappedTop) = WidgetPlacement.Snap(left, top, screen, (int)Math.Round(window.Width * scale), (int)Math.Round(window.Height * scale));
        window.Left = snappedLeft / scale;
        window.Top = snappedTop / scale;
    }

    /// <summary>Đổi cấu hình màn hình (rút/cắm): đưa widget Plus về vị trí đã nhớ cho cấu hình đó, hoặc về màn hình chính nếu đang nằm ngoài.</summary>
    private async Task RepositionExtrasAsync()
    {
        foreach (var (kind, window) in _extras)
        {
            var saved = await LoadPositionAsync(WidgetRules.PositionKey(kind));

            if (saved?.Split(',') is [var left, var top] && double.TryParse(left, CultureInfo.InvariantCulture, out var l) && double.TryParse(top, CultureInfo.InvariantCulture, out var t) && IsPointVisible((int)l, (int)t))
            {
                window.Left = l;
                window.Top = t;
            }
            else if (!IsOnAnyScreen(new System.Windows.Interop.WindowInteropHelper(window).Handle))
            {
                window.Left = System.Windows.SystemParameters.WorkArea.Right - window.Width - 40;
                window.Top = 60 + (int)kind * 170;
            }
        }
    }

    private bool ReattachExtras()
    {
        var ok = true;

        foreach (var window in _extras.Values)
        {
            ok &= !window.Pin(_desktop, DesktopPinningMode.DesktopLevel).UsedFallback;
        }

        return ok;
    }

    /// <summary>Explorer khởi động lại: đóng widget cũ để lần đồng bộ sau dựng lại.</summary>
    private void ResetExtras()
    {
        foreach (var window in _extras.Values)
        {
            TryClose(window);
        }

        _extras.Clear();
    }

    /// <summary>Đặt cờ Plus (chỉ dùng cho tự kiểm tra trên CI).</summary>
    public Task Settings_SetPlusForTestAsync(bool plus) => _settings.SetAsync(PlusService.PlusFlagKey, plus ? "1" : "0");

    /// <summary>Bật cả ba widget Plus (chỉ dùng cho tự kiểm tra trên CI).</summary>
    public async Task EnableExtrasForTestAsync()
    {
        await _settings.SetAsync(PlusService.PlusFlagKey, "1");

        foreach (var kind in Enum.GetValues<ExtraWidget>())
        {
            await _settings.SetAsync(WidgetRules.EnabledKey(kind), "1");
        }

        await SyncExtraWidgetsAsync();
    }
}
