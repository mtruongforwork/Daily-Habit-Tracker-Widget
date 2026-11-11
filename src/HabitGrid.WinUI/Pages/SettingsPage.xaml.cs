using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Monetization;
using HabitGrid.Presentation.ViewModels;
using HabitGrid.WinUI.Plus;
using HabitGrid.Presentation.Services;
using HabitGrid.Presentation.Theming;
using HabitGrid.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace HabitGrid.WinUI.Pages;

/// <summary>Cài đặt: chủ đề màu, chế độ sáng/tối, giảm chuyển động, ngôn ngữ. Áp dụng ngay, không cần khởi động lại.</summary>
public sealed partial class SettingsPage : Page
{
    private readonly ThemeService _theme = App.Host.Get<ThemeService>();
    private readonly StackPanel _root = new() { Spacing = 16, Padding = new Thickness(36, 28, 36, 32), MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Dictionary<string, Border> _tiles = [];
    private ChipGroup? _modeChips;
    private static readonly ThemeMode[] ModeOrder = [ThemeMode.System, ThemeMode.Light, ThemeMode.Dark];
    private TextBlock? _lightOnlyNote;
    private bool _building;
    private readonly Dictionary<string, TextBlock> _lockLabels = [];
    private int _level = 1;

    public SettingsPage()
    {
        InitializeComponent();
        Scroll.Content = _root;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Build();
    }

    private void Build()
    {
        _building = true;
        _root.Children.Clear();
        _tiles.Clear();

        _root.Children.Add(new TextBlock { Text = Ui.T("settings.title"), Style = Res("TitleDisplayTextStyle") });
        BuildPlusCard();
        BuildBackupCard();
        BuildVacationCard();

        // Chủ đề màu
        var themeCard = Card(Ui.T("settings.theme"));
        var tiles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };

        foreach (var theme in Themes.All)
        {
            tiles.Children.Add(CreateTile(theme));
        }

        themeCard.Children.Add(tiles);
        _lightOnlyNote = new TextBlock { Text = Ui.T("settings.pastel.note"), Style = Res("BodySecondaryTextStyle") };
        themeCard.Children.Add(_lightOnlyNote);
        Wrap(themeCard);

        BuildAccentCard();

        // Chế độ
        var modeCard = Card(Ui.T("settings.mode"));
        _modeChips = new ChipGroup { MinChipWidth = 130, MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left };

        foreach (var key in new[] { "settings.mode.system", "settings.mode.light", "settings.mode.dark" })
        {
            _modeChips.Add(Ui.T(key));
        }

        _modeChips.SelectedIndex = Array.IndexOf(ModeOrder, _theme.Mode);
        _modeChips.SelectionChanged += async (_, _) =>
        {
            if (!_building)
            {
                await _theme.SetAsync(_theme.Theme, ModeOrder[Math.Max(0, _modeChips.SelectedIndex)], XamlRoot.Content as FrameworkElement);
            }
        };
        modeCard.Children.Add(_modeChips);
        Wrap(modeCard);

        // Kính mờ Acrylic: kéo để đổi độ trong suốt của cửa sổ (0 = nền đặc).
        var glassCard = Card(Ui.T("settings.glass"));
        glassCard.Children.Add(new TextBlock { Text = Ui.T("settings.glass.hint"), Style = Res("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });
        var glass = new Slider { Minimum = 0, Maximum = 100, StepFrequency = 5, Value = _theme.GlassStrength, MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(glass, Ui.T("settings.glass"));
        glass.ValueChanged += async (_, e) =>
        {
            if (!_building)
            {
                await _theme.SetGlassAsync((int)e.NewValue, XamlRoot.Content as FrameworkElement);
            }
        };
        glassCard.Children.Add(glass);
        Wrap(glassCard);

        // Giảm chuyển động
        var motionCard = Card(Ui.T("settings.motion"));
        var motion = new ToggleSwitch { IsOn = _theme.ReduceMotionSetting, OnContent = string.Empty, OffContent = string.Empty };
        AutomationProperties.SetName(motion, Ui.T("settings.motion"));
        motion.Toggled += async (_, _) =>
        {
            if (!_building)
            {
                await _theme.SetReduceMotionAsync(motion.IsOn);
            }
        };
        motionCard.Children.Add(new TextBlock { Text = Ui.T("settings.motion.hint"), Style = Res("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });
        motionCard.Children.Add(motion);
        Wrap(motionCard);

        BuildRemindersCard();
        BuildHotkeysCard();

        // Streak freeze
        var freezeCard = Card(Ui.T("buddy.title"));
        var autoFreeze = new ToggleSwitch { OnContent = Ui.T("settings.freeze.auto"), OffContent = Ui.T("settings.freeze.auto") };
        AutomationProperties.SetName(autoFreeze, Ui.T("settings.freeze.auto"));
        var gamification = App.Host.Get<GamificationService>();
        _ = LoadAutoFreezeAsync();
        async Task LoadAutoFreezeAsync()
        {
            autoFreeze.IsOn = await gamification.GetAutoFreezeAsync();
        }

        autoFreeze.Toggled += async (_, _) =>
        {
            if (!_building)
            {
                await gamification.SetAutoFreezeAsync(autoFreeze.IsOn);
            }
        };
        freezeCard.Children.Add(new TextBlock { Text = Ui.T("settings.freeze.hint"), Style = Res("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });
        freezeCard.Children.Add(autoFreeze);
        Wrap(freezeCard);

        // Widget chạy cùng Windows (WidgetHost đọc khóa này và tự đăng ký/gỡ)
        var widgetsCard = Card(Ui.T("settings.widgets"));
        var autostart = new ToggleSwitch { OnContent = Ui.T("settings.autostart"), OffContent = Ui.T("settings.autostart") };
        AutomationProperties.SetName(autostart, Ui.T("settings.autostart"));
        var settingsStore = App.Host.Get<ISettingsStore>();
        _ = LoadAutostartAsync();
        async Task LoadAutostartAsync()
        {
            autostart.IsOn = await settingsStore.GetAsync(SettingKeys.WidgetsAutoStart) != "0";
        }

        autostart.Toggled += async (_, _) =>
        {
            if (!_building)
            {
                await settingsStore.SetAsync(SettingKeys.WidgetsAutoStart, autostart.IsOn ? "1" : "0");
            }
        };
        widgetsCard.Children.Add(new TextBlock { Text = Ui.T("settings.autostart.hint"), Style = Res("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });
        widgetsCard.Children.Add(autostart);

        // Widget khoảnh khắc (Plus): WidgetHost bật khi có Plus và công tắc này đang bật.
        var momentToggle = new ToggleSwitch { OnContent = Ui.T("settings.momentWidget"), OffContent = Ui.T("settings.momentWidget") };
        AutomationProperties.SetName(momentToggle, Ui.T("settings.momentWidget"));
        _ = LoadMomentAsync();
        async Task LoadMomentAsync()
        {
            momentToggle.IsOn = await settingsStore.GetAsync("widgets.moment.enabled") != "0";
        }

        momentToggle.Toggled += async (_, _) =>
        {
            if (_building)
            {
                return;
            }

            if (momentToggle.IsOn && !App.Host.Get<PlusService>().Status.IsPlus && !await PaywallDialog.ShowAsync(XamlRoot, PaywallSource.Settings))
            {
                momentToggle.IsOn = false;
                return;
            }

            await settingsStore.SetAsync("widgets.moment.enabled", momentToggle.IsOn ? "1" : "0");
            HostLink.EnsureRunning();
        };
        widgetsCard.Children.Add(new TextBlock { Text = Ui.T("settings.momentWidget.hint"), Style = Res("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });
        widgetsCard.Children.Add(momentToggle);

        // Widget Plus (M1): Buddy, Mục tiêu, Multi Grid. Mặc định tắt; bật khi chưa có Plus thì mở paywall (người dùng vừa bấm tính năng khóa).
        widgetsCard.Children.Add(new TextBlock { Text = Ui.T("settings.widgets.limits"), Style = Res("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });

        foreach (var kind in Enum.GetValues<HabitGrid.Core.Domain.Widgets.ExtraWidget>())
        {
            var key = HabitGrid.Core.Domain.Widgets.WidgetRules.EnabledKey(kind);
            var label = Ui.T("settings.widget." + kind);
            var toggle = new ToggleSwitch { OnContent = label, OffContent = label };
            AutomationProperties.SetName(toggle, label);
            _ = LoadExtraAsync();
            async Task LoadExtraAsync() => toggle.IsOn = HabitGrid.Core.Domain.Widgets.WidgetRules.IsEnabled(await settingsStore.GetAsync(key));

            toggle.Toggled += async (_, _) =>
            {
                if (_building)
                {
                    return;
                }

                if (toggle.IsOn && !App.Host.Get<PlusService>().Status.IsPlus && !await PaywallDialog.ShowAsync(XamlRoot, PaywallSource.Settings))
                {
                    toggle.IsOn = false;
                    return;
                }

                await settingsStore.SetAsync(key, toggle.IsOn ? "1" : "0");
                HostLink.EnsureRunning();
            };
            widgetsCard.Children.Add(toggle);
        }


        // Cách hiển thị: một widget gộp (mặc định) hay nhiều widget riêng.
        widgetsCard.Children.Add(new TextBlock { Text = Ui.T("settings.layout"), Style = Res("SectionHeader"), Margin = new Thickness(0, 10, 0, 0) });
        widgetsCard.Children.Add(new TextBlock { Text = Ui.T("settings.layout.hint"), Style = Res("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });
        var layoutChips = new ChipGroup { MinChipWidth = 230, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        var layoutModes = Enum.GetValues<HabitGrid.Core.Domain.Widgets.WidgetLayoutMode>();

        foreach (var mode in layoutModes)
        {
            layoutChips.Add(Ui.T("settings.layout." + mode));
        }

        var currentLayout = HabitGrid.Core.Domain.Widgets.WidgetLayoutModes.Parse(settingsStore.GetAsync(HabitGrid.Core.Domain.Widgets.WidgetLayoutModes.SettingKey).GetAwaiter().GetResult());
        layoutChips.SelectedIndex = Array.IndexOf(layoutModes, currentLayout);
        layoutChips.SelectionChanged += async (_, _) =>
        {
            if (_building || layoutChips.SelectedIndex < 0)
            {
                return;
            }

            await settingsStore.SetAsync(HabitGrid.Core.Domain.Widgets.WidgetLayoutModes.SettingKey, HabitGrid.Core.Domain.Widgets.WidgetLayoutModes.ToStored(layoutModes[layoutChips.SelectedIndex]));
            HostLink.EnsureRunning();
        };
        widgetsCard.Children.Add(layoutChips);

        // Kiểu ghim widget (đổi có hiệu lực sau vài giây, WidgetHost tự dựng lại).
        widgetsCard.Children.Add(new TextBlock { Text = Ui.T("settings.pin"), Style = Res("SectionHeader"), Margin = new Thickness(0, 10, 0, 0) });
        widgetsCard.Children.Add(new TextBlock { Text = Ui.T("settings.pin.hint"), Style = Res("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });
        var pinChips = new ChipGroup { MinChipWidth = 190, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        var pinChoices = Enum.GetValues<HabitGrid.Core.Domain.Widgets.PinChoice>();

        foreach (var choice in pinChoices)
        {
            pinChips.Add(Ui.T("settings.pin." + choice));
        }

        var currentPin = HabitGrid.Core.Domain.Widgets.PinChoices.Parse(settingsStore.GetAsync(HabitGrid.Core.Domain.Widgets.PinChoices.SettingKey).GetAwaiter().GetResult());
        pinChips.SelectedIndex = Array.IndexOf(pinChoices, currentPin);
        pinChips.SelectionChanged += async (_, _) =>
        {
            if (_building || pinChips.SelectedIndex < 0)
            {
                return;
            }

            await settingsStore.SetAsync(HabitGrid.Core.Domain.Widgets.PinChoices.SettingKey, HabitGrid.Core.Domain.Widgets.PinChoices.ToStored(pinChoices[pinChips.SelectedIndex]));
            HostLink.EnsureRunning();
        };
        widgetsCard.Children.Add(pinChips);
        Wrap(widgetsCard);

        // Hỗ trợ: phiên bản, thư mục log và báo lỗi (người dùng tự gửi, app không tải gì lên).
        var supportCard = Card(Ui.T("settings.support"));
        supportCard.Children.Add(new TextBlock { Text = Ui.Loc.Format("settings.support.version", CrashReporting.AppVersion), Style = Res("BodySecondaryTextStyle") });
        supportCard.Children.Add(new TextBlock { Text = Ui.T("settings.support.hint"), Style = Res("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });
        var logs = new Button { Content = Ui.T("settings.support.logs"), Style = Res("GhostButtonStyle") };
        logs.Click += (_, _) => CrashReporting.OpenFolder();
        supportCard.Children.Add(logs);
        Wrap(supportCard);

        // Ngôn ngữ
        var languageCard = Card(Ui.T("settings.language"));
        var language = new ChipGroup { MinChipWidth = 130, MaxWidth = 320, HorizontalAlignment = HorizontalAlignment.Left };
        language.Add(Ui.T("settings.lang.vi"));
        language.Add(Ui.T("settings.lang.en"));
        language.SelectedIndex = Ui.Loc.Language == "en" ? 1 : 0;
        language.SelectionChanged += async (_, _) =>
        {
            if (!_building && App.MainWindowInstance is { } window)
            {
                await window.SetLanguageAsync(language.SelectedIndex == 1 ? "en" : "vi");
            }
        };
        languageCard.Children.Add(language);
        Wrap(languageCard);

        UpdateTiles();
        _building = false;
    }

    private static Style Res(string key) => (Style)Application.Current.Resources[key];

    /// <summary>Thẻ Plus: trạng thái gói, nâng cấp (mở paywall), khôi phục mua hàng, chọn thói quen đang chạy khi vượt giới hạn.</summary>
    private void BuildPlusCard()
    {
        var plus = App.Host.Get<PlusService>();
        var card = Card(Ui.T("settings.plus"));
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var recheck = new TextBlock { Text = Ui.T("settings.plus.recheck"), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["WarningBrush"] };
        var result = new TextBlock { TextWrapping = TextWrapping.Wrap, Style = Res("BodySecondaryTextStyle"), Visibility = Visibility.Collapsed };
        var upgrade = new Button { Content = Ui.T("settings.plus.upgrade"), Style = Res("GradientButtonStyle") };
        var restore = new Button { Content = Ui.T("plus.restore"), Style = Res("GhostButtonStyle") };
        var choose = new Button { Content = Ui.T("settings.plus.choose"), Style = Res("GhostButtonStyle"), Visibility = Visibility.Collapsed };

        void Render()
        {
            var s = plus.Status;
            status.Text = s.Tier == PlusTier.Promo
                ? Ui.Loc.Format("settings.plus.promo", plus.PromoDaysLeft)
                : s.IsPlus ? Ui.Loc.Format("settings.plus.active", Ui.T("settings.plus.tier." + s.Tier)) : Ui.T("settings.plus.free");
            recheck.Visibility = s.NeedsRecheck ? Visibility.Visible : Visibility.Collapsed;
            upgrade.Visibility = s.IsPlus && s.Tier != PlusTier.Promo ? Visibility.Collapsed : Visibility.Visible;
        }

        upgrade.Click += async (_, _) =>
        {
            await PaywallDialog.ShowAsync(XamlRoot, PaywallSource.Settings);
            Render();
            UpdateTiles();
        };
        restore.Click += async (_, _) =>
        {
            var vm = new PaywallViewModel(plus, PaywallSource.Settings);
            await vm.RestoreAsync();
            result.Text = vm.MessageKey is { } key ? Ui.T(key) : string.Empty;
            result.Visibility = Visibility.Visible;
            Render();
            UpdateTiles();
        };
        choose.Click += async (_, _) => await ChooseRunningDialog.ShowAsync(XamlRoot);

        _ = LoadAsync();

        async Task LoadAsync()
        {
            _level = (await App.Host.Get<IProfileRepository>().GetAsync()).Level;
            var allowance = await plus.GetAllowanceAsync();
            choose.Visibility = allowance.ReadOnly.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateTiles();
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(upgrade);
        buttons.Children.Add(restore);
        buttons.Children.Add(choose);

        card.Children.Add(status);
        card.Children.Add(recheck);
        card.Children.Add(buttons);
        card.Children.Add(result);
        Render();
        Wrap(card);
    }

    /// <summary>Nhắc nhở: bật/tắt, giờ yên lặng, cứu streak, tổng kết sáng. WidgetHost đọc lại và lập lịch toast trong vài giây.</summary>
    private void BuildRemindersCard()
    {
        var store = App.Host.Get<ISettingsStore>();
        var card = Card(Ui.T("settings.reminders"));
        card.Children.Add(new TextBlock { Text = Ui.T("settings.reminders.hint"), Style = Res("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });

        var enabled = new ToggleSwitch { OnContent = Ui.T("settings.reminders.enabled"), OffContent = Ui.T("settings.reminders.enabled") };
        var quiet = new ToggleSwitch { OnContent = Ui.T("settings.quiet"), OffContent = Ui.T("settings.quiet") };
        var from = new TimePicker { Header = Ui.T("settings.quiet.from"), ClockIdentifier = "24HourClock", MinuteIncrement = 15 };
        var to = new TimePicker { Header = Ui.T("settings.quiet.to"), ClockIdentifier = "24HourClock", MinuteIncrement = 15 };
        var rescue = new ToggleSwitch { OnContent = Ui.T("settings.rescue"), OffContent = Ui.T("settings.rescue") };
        var morning = new ToggleSwitch { OnContent = Ui.T("settings.morning"), OffContent = Ui.T("settings.morning") };
        var morningAt = new TimePicker { Header = Ui.T("settings.morning.at"), ClockIdentifier = "24HourClock", MinuteIncrement = 5 };

        foreach (var (control, key) in new (FrameworkElement, string)[] { (enabled, "settings.reminders.enabled"), (quiet, "settings.quiet"), (rescue, "settings.rescue"), (morning, "settings.morning") })
        {
            AutomationProperties.SetName(control, Ui.T(key));
        }

        var quietRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        quietRow.Children.Add(from);
        quietRow.Children.Add(to);

        card.Children.Add(enabled);
        card.Children.Add(quiet);
        card.Children.Add(quietRow);
        card.Children.Add(rescue);
        card.Children.Add(morning);
        card.Children.Add(morningAt);
        Wrap(card);

        var loading = true;
        _ = LoadAsync();

        async Task LoadAsync()
        {
            var settings = await ReminderSettingsStore.LoadAsync(store);
            enabled.IsOn = await ReminderSettingsStore.RemindersEnabledAsync(store);
            quiet.IsOn = settings.QuietHoursEnabled;
            from.Time = settings.QuietStart.ToTimeSpan();
            to.Time = settings.QuietEnd.ToTimeSpan();
            rescue.IsOn = settings.StreakRescue;
            morning.IsOn = settings.MorningSummary;
            morningAt.Time = settings.MorningSummaryAt.ToTimeSpan();
            loading = false;
        }

        async Task SaveAsync()
        {
            if (loading || _building)
            {
                return;
            }

            await store.SetAsync(ReminderSettingsStore.RemindersEnabledKey, enabled.IsOn ? "1" : "0");
            await ReminderSettingsStore.SaveAsync(store, new HabitGrid.Core.Domain.Reminders.ReminderSettings
            {
                QuietHoursEnabled = quiet.IsOn,
                QuietStart = TimeOnly.FromTimeSpan(from.Time),
                QuietEnd = TimeOnly.FromTimeSpan(to.Time),
                StreakRescue = rescue.IsOn,
                MorningSummary = morning.IsOn,
                MorningSummaryAt = TimeOnly.FromTimeSpan(morningAt.Time)
            });

            // Nhắc WidgetHost lập lại lịch ngay (ghi settings làm đổi data_version).
            HostLink.EnsureRunning();
        }

        enabled.Toggled += async (_, _) => await SaveAsync();
        quiet.Toggled += async (_, _) => await SaveAsync();
        rescue.Toggled += async (_, _) => await SaveAsync();
        morning.Toggled += async (_, _) => await SaveAsync();
        from.TimeChanged += async (_, _) => await SaveAsync();
        to.TimeChanged += async (_, _) => await SaveAsync();
        morningAt.TimeChanged += async (_, _) => await SaveAsync();
    }

    /// <summary>Phím tắt: bật/tắt hai phím toàn cục (WidgetHost áp dụng trong ~30 giây) và hiện phím thực tế đang đăng ký.</summary>
    private void BuildHotkeysCard()
    {
        var store = App.Host.Get<ISettingsStore>();
        var card = Card(Ui.T("settings.hotkeys"));
        card.Children.Add(new TextBlock { Text = Ui.T("settings.hotkeys.hint"), Style = Res("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });

        foreach (var (enabledKey, registeredKey, label) in new[]
        {
            (ReminderSettingsStore.QuickHotKeyEnabledKey, "hotkey.quick.registered", "settings.hotkey.quick"),
            (ReminderSettingsStore.FocusHotKeyEnabledKey, "hotkey.focus.registered", "settings.hotkey.focus")
        })
        {
            var toggle = new ToggleSwitch { OnContent = Ui.T(label), OffContent = Ui.T(label) };
            AutomationProperties.SetName(toggle, Ui.T(label));
            var status = new TextBlock { Style = Res("BodySecondaryTextStyle"), Margin = new Thickness(0, -6, 0, 4) };
            var loading = true;
            _ = LoadAsync();

            async Task LoadAsync()
            {
                toggle.IsOn = await store.GetAsync(enabledKey) != "0";
                var registered = await store.GetAsync(registeredKey);
                status.Text = string.IsNullOrEmpty(registered) ? Ui.T("settings.hotkey.none") : Ui.Loc.Format("settings.hotkey.registered", registered);
                loading = false;
            }

            toggle.Toggled += async (_, _) =>
            {
                if (!loading && !_building)
                {
                    await store.SetAsync(enabledKey, toggle.IsOn ? "1" : "0");
                }
            };

            card.Children.Add(toggle);
            card.Children.Add(status);
        }

        card.Children.Add(new TextBlock { Text = Ui.T("settings.shortcuts.inapp"), Style = Res("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });
        Wrap(card);
    }

    private static StackPanel Card(string title)
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), Style = Res("SectionHeader"), Margin = new Thickness(0) });
        return panel;
    }

    private void Wrap(StackPanel content) =>
        _root.Children.Add(new Border { Style = Res("CardBorder"), Child = content });

    private UIElement CreateTile(ThemeDefinition theme)
    {
        var palette = theme.Dark ?? theme.Light!;
        var name = Ui.T(theme.NameKey);

        var swatch = new Border
        {
            Height = 64,
            CornerRadius = new CornerRadius(16),
            Background = Ui.GradientBrush(new HabitGrid.Core.Domain.GradientStops(palette.AccentStart, palette.AccentEnd, palette.AccentEnd, 0))
        };

        var canvas = new Border
        {
            Height = 22,
            CornerRadius = new CornerRadius(8),
            Background = Ui.Brush(theme.Light is { } light && theme.Dark is null ? light.Canvas : palette.Canvas),
            BorderBrush = (Brush)Application.Current.Resources["StrokeSubtleBrush"],
            BorderThickness = new Thickness(1)
        };

        var label = new TextBlock { Text = name, Style = Res("CardTitleTextStyle"), HorizontalAlignment = HorizontalAlignment.Center };
        var lockLabel = new TextBlock { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Foreground = (Brush)Application.Current.Resources["TextMutedBrush"] };
        _lockLabels[theme.Id] = lockLabel;
        var content = new StackPanel { Spacing = 8, Width = 150 };
        content.Children.Add(swatch);
        content.Children.Add(canvas);
        content.Children.Add(label);
        content.Children.Add(lockLabel);

        var tile = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(22),
            BorderThickness = new Thickness(2),
            Background = (Brush)Application.Current.Resources["CardIdleBrush"],
            Child = content,
            IsTabStop = true,
            UseSystemFocusVisuals = true
        };
        AutomationProperties.SetName(tile, name);
        AutomationProperties.SetAccessibilityView(tile, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Control);

        async void Apply()
        {
            if (_building)
            {
                return;
            }

            // Theme khóa (Plus / chưa đủ level): mở paywall vì người dùng vừa chọn nó; chỉ đổi theme nếu đã có quyền.
            if (!ThemeAccess.IsUnlocked(theme.Id, App.Host.Get<PlusService>().Status.IsPlus, _level)
                && !await PaywallDialog.ShowAsync(XamlRoot, PaywallSource.Theme, ThemeAccess.UnlockLevel(theme.Id)))
            {
                return;
            }

            _theme.Override = null;
            await _theme.SetAsync(theme, _theme.Mode, XamlRoot.Content as FrameworkElement);
            UpdateTiles();
        }

        tile.Tapped += (_, _) => Apply();
        tile.KeyDown += (_, e) =>
        {
            if (e.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space)
            {
                Apply();
                e.Handled = true;
            }
        };

        _tiles[theme.Id] = tile;
        return tile;
    }

    private void UpdateTiles()
    {
        var isPlus = App.Host.Get<PlusService>().Status.IsPlus;

        foreach (var (id, lockLabel) in _lockLabels)
        {
            var unlocked = ThemeAccess.IsUnlocked(id, isPlus, _level);
            lockLabel.Text = unlocked ? string.Empty
                : ThemeAccess.UnlockLevel(id) is { } level ? Ui.Loc.Format("theme.locked.level", level) : Ui.T("theme.locked");
            lockLabel.Visibility = unlocked ? Visibility.Collapsed : Visibility.Visible;
        }

        if (_lightOnlyNote is not null)
        {
            _lightOnlyNote.Visibility = _theme.Theme.Dark is null ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (var (id, tile) in _tiles)
        {
            var selected = id == _theme.Theme.Id;
            tile.BorderBrush = selected
                ? (Brush)Application.Current.Resources["AccentGradientBrush"]
                : (Brush)Application.Current.Resources["StrokeSubtleBrush"];
        }
    }
}
