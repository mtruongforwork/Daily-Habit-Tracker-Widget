using System.ComponentModel;
using HabitGrid.Presentation.Services;
using HabitGrid.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HabitGrid.WinUI.Pages;

public sealed partial class TodayPage : Page
{
    private bool _wasAllDone;

    public TodayPage()
    {
        ViewModel = App.Host.Get<TodayViewModel>();
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelChanged;
        ViewModel.Groups.CollectionChanged += (_, _) => UpdateEmpty();
        ViewModel.Week.CollectionChanged += (_, _) => Strip.Update(ViewModel.Week);
        Strip.DaySelected += async (_, day) => await Guard(async () =>
        {
            await ViewModel.SelectDayAsync(day);
            await LoadMoodAsync();
            await LoadRoutinesAsync();

            // Các khối của trang xuất hiện lần lượt (mờ dần + nổi lên).
            if (Scroll.Content is Microsoft.UI.Xaml.Controls.StackPanel content)
            {
                HabitGrid.WinUI.Controls.Motion.EnterChildren(content);
            }
        });
    }

    public TodayViewModel ViewModel { get; }

    /// <summary>Dải routine cho ngày đang xem; check một bước hoặc "xong cả routine" thì nạp lại thẻ thói quen.</summary>
    private async Task LoadRoutinesAsync()
    {
        var day = ViewModel.SelectedDay == default ? DateOnly.FromDateTime(DateTime.Today) : ViewModel.SelectedDay;

        if (!_routinesWired)
        {
            _routinesWired = true;
            Routines.Changed += async (_, _) => await Guard(async () =>
            {
                await ViewModel.LoadAsync();
                await LoadRoutinesAsync();
            });
        }

        await Routines.ReloadAsync(day);
        await Challenges.ReloadAsync();
        await Weekly.ReloadAsync();
    }

    private bool _routinesWired;

    /// <summary>Thanh cảm xúc cho ngày đang xem (ẩn khi xem ngày tương lai).</summary>
    private async Task LoadMoodAsync()
    {
        var day = ViewModel.SelectedDay == default ? DateOnly.FromDateTime(DateTime.Today) : ViewModel.SelectedDay;
        Mood.Visibility = day > DateOnly.FromDateTime(DateTime.Today) ? Visibility.Collapsed : Visibility.Visible;
        await Mood.LoadAsync(day);
    }

    /// <summary>Dải "đang tạm nghỉ đến ngày…" kèm nút kết thúc sớm.</summary>
    private async Task UpdateVacationBarAsync()
    {
        var info = await App.Host.Get<VacationService>().CurrentAsync();
        VacationBar.IsOpen = info is not null;

        if (info is null)
        {
            return;
        }

        VacationBar.Message = Ui.Loc.Format(info.IsAll ? "vacation.banner.all" : "vacation.banner.some", Ui.LongDate(info.To));
        VacationEndButton.Content = Ui.T("vacation.end");
    }

    private async void OnVacationEndClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await App.Host.Get<VacationService>().EndAsync();
            await ViewModel.LoadAsync();
            await UpdateVacationBarAsync();
            await LoadRoutinesAsync();
        }
        catch (Exception ex)
        {
            Log(ex, "Kết thúc tạm nghỉ lỗi");
        }
    }

    /// <summary>Phím 1–9: check dòng thứ <paramref name="index"/> (đếm từ 0) theo đúng thứ tự đang hiển thị.</summary>
    public async Task CheckRowAsync(int index)
    {
        var items = ViewModel.Groups.SelectMany(g => g.Items).ToList();

        if (index >= 0 && index < items.Count)
        {
            await ViewModel.CheckInAsync(items[index]);
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        try
        {
            ApplyTexts();
            await ViewModel.LoadAsync();
            _wasAllDone = ViewModel.AllDone;
            ApplyTexts();
            await UpdateVacationBarAsync();
            await LoadMoodAsync();
            await LoadRoutinesAsync();
        }
        catch (Exception ex)
        {
            Log(ex, "Mở trang Hôm nay lỗi");
        }
    }

    private void ApplyTexts()
    {
        var day = ViewModel.SelectedDay == default ? DateOnly.FromDateTime(DateTime.Today) : ViewModel.SelectedDay;

        GreetingText.Text = ViewModel.IsViewingPast ? Ui.Loc.Format("today.viewing", Ui.LongDate(day)) : Ui.Greeting(DateTime.Now);
        DateText.Text = ViewModel.IsViewingPast ? string.Empty : Ui.LongDate(day);
        DateText.Visibility = ViewModel.IsViewingPast ? Visibility.Collapsed : Visibility.Visible;
        AddButtonText.Text = Ui.T("habits.add");
        // Nút có icon + chữ trong StackPanel: trình đọc màn hình không tự lấy tên, phải đặt.
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(AddButton, AddButtonText.Text);
        BackToTodayButton.Content = Ui.T("today.backToToday");
        BackToTodayButton.Visibility = ViewModel.IsViewingPast ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = Ui.T("today.empty.title");
        EmptyBody.Text = Ui.T("today.empty.body");
        EmptyAddButton.Content = Ui.T("habits.add");
        UndoButton.Content = Ui.T("today.undo");

        Strip.Update(ViewModel.Week);
        Hero.Update(ViewModel);
        StreakBox.Update(ViewModel);
        UpdateEmpty();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TodayViewModel.DoneCount):
                _ = Guard(LoadRoutinesAsync);
                goto case nameof(TodayViewModel.TotalCount);
            case nameof(TodayViewModel.TotalCount):
            case nameof(TodayViewModel.BuddyMood):
            case nameof(TodayViewModel.BuddyName):
            case nameof(TodayViewModel.Xp):
            case nameof(TodayViewModel.Level):
                Hero.Update(ViewModel);
                break;
            case nameof(TodayViewModel.OverallStreak):
            case nameof(TodayViewModel.FreezeTokens):
                StreakBox.Update(ViewModel);
                break;
            case nameof(TodayViewModel.AllDone):
                OnAllDoneChanged();
                break;
            case nameof(TodayViewModel.SelectedDay):
            case nameof(TodayViewModel.IsViewingPast):
                ApplyTexts();
                break;
            case nameof(TodayViewModel.CanUndo):
            case nameof(TodayViewModel.LastAction):
                UpdateUndo();
                break;
            case nameof(TodayViewModel.ErrorMessage):
                ShowError();
                break;
            case nameof(TodayViewModel.IsEmpty):
                UpdateEmpty();
                break;
        }
    }

    /// <summary>Hoàn thành mọi thứ trong ngày → confetti từ giữa hero.</summary>
    private void OnAllDoneChanged()
    {
        if (ViewModel.AllDone && !_wasAllDone && !ViewModel.IsViewingPast)
        {
            Confetti.Burst(new Windows.Foundation.Point(ActualWidth * 0.35, 220));
        }

        _wasAllDone = ViewModel.AllDone;
    }

    private void UpdateEmpty()
    {
        EmptyPanel.Visibility = ViewModel.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        AddButton.Visibility = ViewModel.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        Hero.Visibility = ViewModel.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        StreakBox.Visibility = ViewModel.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        Strip.Visibility = ViewModel.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        NotTodayPanel.Visibility = ViewModel.NotToday.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotTodayToggle.Content = Ui.T("today.notToday") + $" ({ViewModel.NotToday.Count})";
    }

    private void UpdateUndo()
    {
        if (ViewModel.LastAction is { } action)
        {
            UndoBar.Message = Ui.Loc.Format("today.checked", action.HabitName);
            UndoBar.IsOpen = true;
        }
        else
        {
            UndoBar.IsOpen = false;
        }
    }

    private void ShowError()
    {
        var message = ViewModel.ErrorMessage;

        if (string.IsNullOrEmpty(message))
        {
            ErrorBar.IsOpen = false;
            return;
        }

        // Mã lỗi nghiệp vụ được dịch; lỗi hệ thống hiện thông điệp chung.
        ErrorBar.Message = message is "skip.limit" or "plan.readonly" ? Ui.T(message) : Ui.T("error.generic");
        ErrorBar.IsOpen = true;
    }

    private void OnNotTodayToggle(object sender, RoutedEventArgs e) =>
        NotTodayRepeater.Visibility = NotTodayToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private async void OnBackToToday(object sender, RoutedEventArgs e) =>
        await Guard(async () =>
        {
            await ViewModel.SelectDayAsync(ViewModel.Today);
            await LoadMoodAsync();
        });

    private async void OnUndoClick(object sender, RoutedEventArgs e) =>
        await Guard(ViewModel.UndoAsync);

    private async void OnAddClick(object sender, RoutedEventArgs e) =>
        await Guard(async () =>
        {
            if (await HabitEditorDialog.ShowAsync(XamlRoot) is not null)
            {
                await ViewModel.LoadAsync();
                ApplyTexts();
            }
        });

    /// <summary>Mọi handler async phải bắt lỗi: một lỗi ở đây không được làm app thoát.</summary>
    private static async Task Guard(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Log(ex, "Thao tác trên trang Hôm nay lỗi");
        }
    }

    private static void Log(Exception ex, string message) =>
        App.Host.Get<IAppLogger>().Error(message, ex);
}
