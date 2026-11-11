using HabitGrid.Core.Domain;
using HabitGrid.Presentation.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace HabitGrid.WinUI;

/// <summary>
/// Áp dụng theme lúc chạy (03-UI-REBUILD-GLOW2 §8): đổi màu của các brush dùng chung trong <c>Tokens.xaml</c>
/// thay vì thay brush, nên mọi chỗ đang dùng được cập nhật ngay.
/// </summary>
public sealed class ThemeService
{
    private readonly ISettingsStore _settings;
    private readonly UISettings _uiSettings = new();

    public ThemeService(ISettingsStore settings)
    {
        _settings = settings;
    }

    public ThemeDefinition Theme { get; private set; } = Themes.CandyBento;

    /// <summary>
    /// Theme hiển thị thay thế khi theme đã chọn không còn quyền dùng (Plus hết hạn): hiện Candy Bento nhưng vẫn giữ lựa chọn
    /// đã lưu để nâng cấp lại là dùng tiếp (SPEC-V2-10 §5).
    /// </summary>
    public ThemeDefinition? Override { get; set; }

    public ThemeDefinition Effective => Override ?? Theme;

    public ThemeMode Mode { get; private set; } = ThemeMode.System;

    public ThemePalette Palette { get; private set; } = Themes.CandyBento.Dark!;

    /// <summary>Phát sau khi bảng màu đổi (nền động, title bar cập nhật theo).</summary>
    public event EventHandler<ThemePalette>? Changed;

    public bool AnimationsEnabled { get; private set; } = true;

    /// <summary>Cường độ kính mờ 0–100 (0 = nền đặc). Lưu ở <c>appearance.glass</c>.</summary>
    public int GlassStrength { get; private set; } = GlassStyle.Default;

    /// <summary>Đặt cường độ kính mờ, lưu và áp ngay (phát <see cref="Changed"/> để cửa sổ đổi nền).</summary>
    public async Task SetGlassAsync(int strength, FrameworkElement? root)
    {
        GlassStrength = GlassStyle.Clamp(strength);
        await _settings.SetAsync(GlassStyle.SettingKey, GlassStrength.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Apply(root);
    }

    /// <summary>Màu nhấn người dùng chọn (#RRGGBB), lưu ngay cả khi hết Plus để nâng cấp lại là dùng tiếp.</summary>
    public string? AccentColor { get; private set; }

    /// <summary>Plus mới được áp màu nhấn tùy chỉnh; Free vẫn thấy màu của theme.</summary>
    public bool AccentAllowed { get; set; }

    public async Task LoadAsync()
    {
        Theme = Themes.Find(await _settings.GetAsync(SettingKeys.Theme));
        Mode = Enum.TryParse<ThemeMode>(await _settings.GetAsync(SettingKeys.ThemeMode), out var mode) ? mode : ThemeMode.System;
        ReduceMotionSetting = await _settings.GetAsync(SettingKeys.ReduceMotion) == "1";
        AnimationsEnabled = _uiSettings.AnimationsEnabled && !ReduceMotionSetting;
        AccentColor = AccentColors.Normalize(await _settings.GetAsync(AccentColors.SettingKey));
        GlassStrength = GlassStyle.Parse(await _settings.GetAsync(GlassStyle.SettingKey));
    }

    /// <summary>Đặt (hoặc xóa với null) màu nhấn tùy chỉnh và áp ngay. Trả false nếu mã màu không hợp lệ.</summary>
    public async Task<bool> SetAccentAsync(string? hex, FrameworkElement? root)
    {
        var normalized = AccentColors.Normalize(hex);

        if (hex is not null && normalized is null)
        {
            return false;
        }

        AccentColor = normalized;

        if (normalized is null)
        {
            await _settings.RemoveAsync(AccentColors.SettingKey);
        }
        else
        {
            await _settings.SetAsync(AccentColors.SettingKey, normalized);
        }

        Apply(root);
        return true;
    }

    /// <summary>Bật/tắt giảm chuyển động ngay lập tức và nhớ lựa chọn.</summary>
    public async Task SetReduceMotionAsync(bool reduce)
    {
        await _settings.SetAsync(SettingKeys.ReduceMotion, reduce ? "1" : "0");
        ReduceMotionSetting = reduce;
        AnimationsEnabled = _uiSettings.AnimationsEnabled && !reduce;
        Changed?.Invoke(this, Palette);
    }

    /// <summary>Lựa chọn của người dùng (độc lập với cài đặt hệ thống).</summary>
    public bool ReduceMotionSetting { get; private set; }

    public async Task SetAsync(ThemeDefinition theme, ThemeMode mode, FrameworkElement? root)
    {
        Theme = theme;
        Mode = mode;
        await _settings.SetAsync(SettingKeys.Theme, theme.Id);
        await _settings.SetAsync(SettingKeys.ThemeMode, mode.ToString());
        Apply(root);
    }

    public void Apply(FrameworkElement? root)
    {
        Palette = Effective.Resolve(Mode, SystemIsDark());
        var resources = Application.Current.Resources;

        void Set(string key, string hex)
        {
            if (resources.TryGetValue(key, out var value) && value is SolidColorBrush brush)
            {
                brush.Color = Ui.ParseColor(hex, brush.Color);
            }
        }

        var p = Palette;
        Set("CanvasBrush", p.Canvas);
        Set("SurfaceBrush", p.Surface);
        Set("Surface2Brush", p.SurfaceStrong);
        Set("StrokeSubtleBrush", p.Stroke);
        Set("StrokeHighlightBrush", p.StrokeHighlight);
        Set("TextPrimaryBrush", p.TextPrimary);
        Set("TextSecondaryBrush", p.TextSecondary);
        Set("TextMutedBrush", p.TextMuted);
        Set("AccentBrandBrush", p.AccentStart);
        Set("CardIdleBrush", p.CardIdle);
        Set("HeatEmptyBrush", p.HeatEmpty);
        Set("SuccessBrush", p.Success);
        Set("WarningBrush", p.Warning);
        Set("FreezeBrush", p.Freeze);
        Set("RailBrush", p.Rail);
        Set("RailSelectedTextBrush", p.RailSelectedText);

        if (resources.TryGetValue("AccentGradientBrush", out var gradient) && gradient is LinearGradientBrush accent && accent.GradientStops.Count >= 2)
        {
            accent.GradientStops[0].Color = Ui.ParseColor(p.AccentStart);
            accent.GradientStops[1].Color = Ui.ParseColor(p.AccentEnd);
        }

        if (AccentAllowed && AccentColor is { } custom)
        {
            var end = AccentColors.DeriveEnd(custom);
            Set("AccentBrandBrush", custom);

            if (resources.TryGetValue("AccentGradientBrush", out var g2) && g2 is LinearGradientBrush custom2 && custom2.GradientStops.Count >= 2)
            {
                custom2.GradientStops[0].Color = Ui.ParseColor(custom);
                custom2.GradientStops[1].Color = Ui.ParseColor(end);
            }
        }

        // Control hệ thống (TextBox, NumberBox…) theo sáng/tối của bảng màu.
        if (root is not null)
        {
            root.RequestedTheme = p.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        }

        Changed?.Invoke(this, p);
    }

    private bool SystemIsDark()
    {
        // Nền hệ thống tối ⇔ màu Background có độ sáng thấp.
        var bg = _uiSettings.GetColorValue(UIColorType.Background);
        return bg.R + bg.G + bg.B < 3 * 128;
    }
}
