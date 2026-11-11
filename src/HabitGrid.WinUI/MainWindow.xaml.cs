using System.Runtime.InteropServices;
using HabitGrid.Core.Domain;
using HabitGrid.Presentation.Services;
using HabitGrid.Presentation.Theming;
using HabitGrid.Presentation.ViewModels;
using HabitGrid.WinUI.Pages;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using WinRT.Interop;

namespace HabitGrid.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly ShellViewModel _shell = App.Host.Get<ShellViewModel>();
    private readonly ThemeService _theme = App.Host.Get<ThemeService>();
    private GamificationCoordinator? _gamification;

    public MainWindow()
    {
        InitializeComponent();

        Title = Ui.T("app.name");
        TitleText.Text = Ui.T("app.name");

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 820));

        Rail.Build(_shell.MainItems, _shell.SettingsItem);
        Rail.Navigated += (_, key) => Navigate(key);
        Rail.AddRequested += async (_, _) => await AddHabitAsync();
        RegisterShortcuts();

        _theme.Changed += (_, palette) =>
        {
            ApplyPalette(palette);
            Backdrop.SetAnimated(_theme.AnimationsEnabled);
        };
        Activated += (_, e) => Backdrop.SetAnimated(_theme.AnimationsEnabled && e.WindowActivationState != WindowActivationState.Deactivated);
        Closed += (_, _) => Closing?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Closing;

    public string CurrentPageKey => _shell.SelectedKey;

    /// <summary>Đang ở luồng làm quen (ẩn dock điều hướng).</summary>
    public bool IsOnboarding { get; private set; }

    /// <summary>Trang đang hiển thị trong khung nội dung (smoke test).</summary>
    public Microsoft.UI.Xaml.Controls.Page? CurrentPage => ContentFrame.Content as Microsoft.UI.Xaml.Controls.Page;

    public OnboardingPage? CurrentOnboardingPage => ContentFrame.Content as OnboardingPage;

    /// <summary>Áp theme, rồi quyết định hiện onboarding hay vào thẳng trang Hôm nay.</summary>
    public async Task InitializeAsync()
    {
        await _theme.LoadAsync();
        _theme.Apply(Root);
        await InitializePlusAsync();

        var needed = await OnboardingViewModel.IsNeededAsync(
            App.Host.Get<ISettingsStore>(),
            App.Host.Get<IHabitRepository>());

        _gamification = new GamificationCoordinator(
            App.Host.Get<GamificationService>(),
            App.Host.Get<IDataChangeBus>(),
            DispatcherQueue,
            () => Root.XamlRoot);

        if (needed)
        {
            ShowOnboarding();
        }
        else
        {
            Navigate(PageKeys.Today);
            _ = _gamification.StartAsync();
            _ = RunDailyBackupAsync();
            _ = AskToChooseRunningIfNeededAsync();

            if (GamificationCoordinator.DialogsEnabled)
            {
                _ = CrashReporting.PromptIfNeededAsync(Root.XamlRoot);
            }
        }
    }

    /// <summary>Đổi theme lúc chạy (cài đặt, smoke test).</summary>
    public async Task SetThemeAsync(ThemeDefinition theme, ThemeMode mode)
    {
        await _theme.SetAsync(theme, mode, Root);
        await App.Host.Get<GamificationService>().SetFlagAsync("theme_changed");
    }

    /// <summary>Đổi ngôn ngữ lúc chạy: lưu, dịch lại dock và tiêu đề, dựng lại trang hiện tại.</summary>
    public async Task SetLanguageAsync(string language)
    {
        await App.Host.Get<ISettingsStore>().SetAsync(SettingKeys.Language, language);
        App.Host.Get<ILocalizer>().SetLanguage(language);
        Title = Ui.T("app.name");
        TitleText.Text = Ui.T("app.name");
        Rail.Build(_shell.MainItems, _shell.SettingsItem);
        Navigate(_shell.SelectedKey);
    }

    /// <summary>Chuyển tới trang theo khóa (dùng cho dock và smoke test).</summary>
    public void NavigateTo(string key) => Navigate(key);

    private void Navigate(string key)
    {
        if (IsOnboarding)
        {
            return;
        }

        _shell.SelectedKey = key;
        Rail.Select(key);

        var transition = new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromBottom };

        switch (key)
        {
            case PageKeys.Today:
                ContentFrame.Navigate(typeof(TodayPage), null, transition);
                break;
            case PageKeys.Habits:
                ContentFrame.Navigate(typeof(HabitsPage), null, transition);
                break;
            case PageKeys.Focus:
                ContentFrame.Navigate(typeof(FocusPage), null, transition);
                break;
            case PageKeys.Journal:
                ContentFrame.Navigate(typeof(JournalPage), null, transition);
                break;
            case PageKeys.Calendar:
                ContentFrame.Navigate(typeof(CalendarPage), null, transition);
                break;
            case PageKeys.Insights:
                ContentFrame.Navigate(typeof(InsightsPage), null, transition);
                break;
            case PageKeys.Buddy:
                ContentFrame.Navigate(typeof(BuddyPage), null, transition);
                break;
            case PageKeys.Settings:
                ContentFrame.Navigate(typeof(SettingsPage), null, transition);
                break;
            default:
                ContentFrame.Navigate(typeof(PlaceholderPage), key, transition);
                break;
        }
    }

    private async Task AddHabitAsync()
    {
        try
        {
            if (await HabitEditorDialog.ShowAsync(Root.XamlRoot) is not null)
            {
                await App.Host.Get<TodayViewModel>().LoadAsync();
                await App.Host.Get<HabitsViewModel>().LoadAsync();
            }
        }
        catch (Exception ex)
        {
            App.Host.Get<IAppLogger>().Error("Thêm thói quen từ dock lỗi", ex);
        }
    }

    /// <summary>
    /// Kính mờ Acrylic thật của Windows: cửa sổ nhìn xuyên và làm mờ hình nền phía sau. Cường độ 0 hoặc hệ thống không hỗ trợ thì dùng nền đặc.
    /// Ảnh chụp trong smoke test (vẽ XAML ngoài màn hình) không có lớp kính này.
    /// </summary>
    private void ApplyGlass(ThemePalette palette)
    {
        var supported = Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported();
        var enabled = supported && _theme.GlassStrength > 0;

        if (enabled && SystemBackdrop is not Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop)
        {
            SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
        }
        else if (!enabled && SystemBackdrop is not null)
        {
            SystemBackdrop = null;
        }

        Backdrop.SetGlassAlpha(enabled ? HabitGrid.Core.Domain.GlassStyle.CanvasAlpha(palette.IsDark, _theme.GlassStrength) : (byte)255);
    }

    private void ApplyPalette(ThemePalette palette)
    {
        ApplyGlass(palette);
        Backdrop.Apply(palette);

        // Nút hệ thống (thu nhỏ/phóng to/đóng) theo màu chữ của theme, nền trong suốt.
        var titleBar = AppWindow.TitleBar;
        var text = Ui.ParseColor(palette.TextPrimary);
        titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonForegroundColor = text;
        titleBar.ButtonHoverForegroundColor = text;
        titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(28, text.R, text.G, text.B);
        titleBar.ButtonInactiveForegroundColor = Ui.ParseColor(palette.TextMuted);
    }

    private void ShowOnboarding()
    {
        IsOnboarding = true;
        Rail.Visibility = Visibility.Collapsed;
        ContentFrame.Navigate(typeof(OnboardingPage), null, new SuppressNavigationTransitionInfo());

        if (ContentFrame.Content is OnboardingPage page)
        {
            page.Completed += OnOnboardingCompleted;
        }
    }

    private void OnOnboardingCompleted(object? sender, OnboardingResult? result)
    {
        IsOnboarding = false;
        Rail.Visibility = Visibility.Visible;
        Navigate(PageKeys.Today);
        _ = _gamification?.StartAsync();
    }

    /// <summary>Đưa cửa sổ lên trước (khi mở lần hai hoặc kích hoạt từ nơi khác).</summary>
    public void BringToFront()
    {
        var hwnd = WindowNative.GetWindowHandle(this);

        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
        SetForegroundWindow(hwnd);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
