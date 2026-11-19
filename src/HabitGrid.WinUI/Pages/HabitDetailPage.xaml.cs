using HabitGrid.Core.Domain;
using HabitGrid.Presentation.Services;
using HabitGrid.Presentation.ViewModels;
using HabitGrid.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

namespace HabitGrid.WinUI.Pages;

/// <summary>Chi tiết một thói quen: hero chuỗi, thống kê, lịch 52 tuần sửa được từng ngày, biểu đồ, nhật ký.</summary>
public sealed partial class HabitDetailPage : Page
{
    private const double MinCell = 9;
    private const double MaxCell = 18;

    public HabitDetailPage()
    {
        ViewModel = App.Host.Get<HabitDetailViewModel>();
        InitializeComponent();
    }

    public HabitDetailViewModel ViewModel { get; }

    /// <summary>Id của thói quen đang xem (để smoke test và điều hướng).</summary>
    public Guid HabitId { get; private set; }

    /// <summary>Gọi sau khi dữ liệu nạp xong (smoke test chờ tín hiệu này).</summary>
    public Task Loaded2 { get; private set; } = Task.CompletedTask;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        BackText.Text = Ui.T("detail.back");
        EditText.Text = Ui.T("habits.edit");
        FocusText.Text = Ui.T("detail.focus");
        PauseText.Text = Ui.T("vacation.pause.habit");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PauseButton, PauseText.Text);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(BackButton, BackText.Text);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(EditButton, EditText.Text);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(FocusButton, FocusText.Text);
        BestLabel.Text = Ui.T("detail.streak.best");
        RateLabel.Text = Ui.T("detail.rate30");
        TotalLabel.Text = Ui.T("detail.total");
        HeatmapTitle.Text = Ui.T("detail.heatmap").ToUpperInvariant();
        MonthsTitle.Text = Ui.T("detail.months").ToUpperInvariant();
        WeekdaysTitle.Text = Ui.T("detail.weekdays").ToUpperInvariant();
        JournalTitle.Text = Ui.T("detail.journal").ToUpperInvariant();
        JournalEmpty.Text = Ui.T("detail.journal.empty");
        NotFoundText.Text = Ui.T("detail.notfound");

        if (e.Parameter is Guid id)
        {
            HabitId = id;
            Loaded2 = LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            await ViewModel.LoadAsync(HabitId);
            Render();
        }
        catch (Exception ex)
        {
            App.Host.Get<IAppLogger>().Error("Mở chi tiết thói quen lỗi", ex);
        }
    }

    public void Render()
    {
        var found = ViewModel.Habit is not null && !ViewModel.NotFound;
        Body.Visibility = found ? Visibility.Visible : Visibility.Collapsed;
        NotFoundText.Visibility = found ? Visibility.Collapsed : Visibility.Visible;
        EditButton.Visibility = found ? Visibility.Visible : Visibility.Collapsed;

        if (ViewModel.Habit is not { } habit || !found)
        {
            return;
        }

        var stops = HabitGradient.From(habit.ColorHue);
        HeroFill.Background = Ui.GradientBrush(stops);
        HeroScrim.Opacity = stops.ScrimOpacity;
        HeroEmoji.Text = Ui.IconText(habit.Icon.Value);
        HeroName.Text = habit.Name;
        HeroCaption.Text = Ui.T("detail.streak.current");
        HeroStreak.Text = ViewModel.CurrentStreak.ToString(Ui.CurrentCulture);
        HeroUnit.Text = Ui.StreakText(ViewModel.CurrentStreak, ViewModel.StreakUnit).Replace(ViewModel.CurrentStreak.ToString(Ui.CurrentCulture), string.Empty).Trim();

        BestValue.Text = Ui.StreakText(ViewModel.BestStreak, ViewModel.StreakUnit);
        RateValue.Text = ViewModel.Rate30 is { } rate ? $"{rate}%" : "—";
        RateDelta.Text = ViewModel.RateDelta switch
        {
            > 0 and var d => Ui.Loc.Format("detail.delta.up", d),
            < 0 and var d => Ui.Loc.Format("detail.delta.down", -d),
            0 => Ui.T("detail.delta.same"),
            _ => string.Empty
        };
        RateDelta.Foreground = ViewModel.RateDelta switch
        {
            > 0 => (Brush)Application.Current.Resources["SuccessBrush"],
            < 0 => (Brush)Application.Current.Resources["WarningBrush"],
            _ => (Brush)Application.Current.Resources["TextSecondaryBrush"]
        };
        TotalValue.Text = ViewModel.TotalDone.ToString(Ui.CurrentCulture);

        Heatmap.ColorHue = habit.ColorHue;
        Heatmap.Data = ViewModel.Heatmap;
        FitHeatmap(HeatmapHost.ActualWidth);

        BuildBars(MonthsChart, ViewModel.Months.Select(m =>
            (Ui.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(m.Month), m.Percent)).ToList(), habit.ColorHue);
        BuildBars(WeekdaysChart, ViewModel.Weekdays.Select(w =>
            (Ui.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(w.Day), w.Percent)).ToList(), habit.ColorHue);
        BuildJournal();
        BuildMoments();
        BuildTimeline(habit.ColorHue);
    }

    private void BuildTimeline(string colorHue)
    {
        TimelineTitle.Text = Ui.T("detail.timeline");
        TimelineList.Children.Clear();
        var runs = ViewModel.StreakTimeline;
        TimelineEmpty.Text = Ui.T("detail.timeline.empty");
        TimelineEmpty.Visibility = runs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (runs.Count == 0)
        {
            return;
        }

        var maxLen = Math.Max(1, runs.Max(r => r.Length));
        var fillBrush = Ui.GradientBrush(HabitGradient.From(colorHue));

        foreach (var run in runs)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(165) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(new TextBlock
            {
                Text = $"{run.StartDay:dd/MM} – {run.EndDay:dd/MM}",
                Style = (Style)Application.Current.Resources["BodySecondaryTextStyle"],
                VerticalAlignment = VerticalAlignment.Center
            });

            var track = new Grid { Height = 12, CornerRadius = new CornerRadius(6), Background = (Brush)Application.Current.Resources["HeatEmptyBrush"], VerticalAlignment = VerticalAlignment.Center };
            var ratio = Math.Max(0.08, (double)run.Length / maxLen);
            var bar = new Border { CornerRadius = new CornerRadius(6), Background = fillBrush, HorizontalAlignment = HorizontalAlignment.Left };
            track.Children.Add(bar);
            track.SizeChanged += (_, e) => bar.Width = e.NewSize.Width * ratio;
            Grid.SetColumn(track, 1);
            row.Children.Add(track);

            var badge = new TextBlock
            {
                Text = Ui.Loc.Format("streak.days", run.Length),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(badge, 2);
            row.Children.Add(badge);

            TimelineList.Children.Add(row);
        }
    }

    /// <summary>Chọn cỡ ô sao cho 52 tuần vừa khung; quá hẹp thì để cuộn ngang.</summary>
    private void FitHeatmap(double available)
    {
        if (ViewModel.Heatmap is not { } data || available <= 0)
        {
            return;
        }

        var weeks = data.Weeks.Count;
        var cell = Math.Clamp(Math.Floor((available - 30 + 3) / weeks - 3), MinCell, MaxCell);
        Heatmap.CellSize = cell;
    }

    private void OnHeatmapHostSizeChanged(object sender, SizeChangedEventArgs e) => FitHeatmap(e.NewSize.Width);

    private static void BuildBars(Grid host, IReadOnlyList<(string Label, int? Percent)> values, string colorHue)
    {
        host.Children.Clear();
        host.ColumnDefinitions.Clear();
        host.RowDefinitions.Clear();
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var stops = HabitGradient.From(colorHue);
        const double maxBar = 96;

        for (var i = 0; i < values.Count; i++)
        {
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var (label, percent) = values[i];
            var fraction = Math.Clamp((percent ?? 0) / 100.0, 0, 1);

            var column = new Grid { VerticalAlignment = VerticalAlignment.Bottom, RowSpacing = 4 };
            column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var value = new TextBlock
            {
                Text = percent is { } p ? $"{p}" : "–",
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"]
            };

            var bar = new Border
            {
                Height = Math.Max(4, fraction * maxBar),
                CornerRadius = new CornerRadius(8),
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = percent is null
                    ? (Brush)Application.Current.Resources["HeatEmptyBrush"]
                    : Ui.GradientBrush(stops)
            };

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Spacing = 4 };
            stack.Children.Add(value);
            stack.Children.Add(bar);
            Grid.SetRow(stack, 0);
            column.Children.Add(stack);
            ToolTipService.SetToolTip(column, percent is { } v ? $"{label}: {v}%" : label);

            Grid.SetColumn(column, i);
            host.Children.Add(column);

            var caption = new TextBlock
            {
                Text = label,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["TextMutedBrush"]
            };
            Grid.SetRow(caption, 1);
            Grid.SetColumn(caption, i);
            host.Children.Add(caption);
        }
    }

    private void BuildJournal()
    {
        JournalList.Children.Clear();
        JournalEmpty.Visibility = ViewModel.Journal.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        string[] moods = ["😞", "🙁", "😐", "🙂", "😄"];

        foreach (var entry in ViewModel.Journal)
        {
            var row = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } } };
            var mood = new TextBlock { Text = entry.Mood is >= 1 and <= 5 ? moods[entry.Mood.Value - 1] : "📝", FontSize = 22, VerticalAlignment = VerticalAlignment.Top };
            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(new TextBlock { Text = Ui.LongDate(entry.Day), Style = (Style)Application.Current.Resources["BodySecondaryTextStyle"] });
            text.Children.Add(new TextBlock { Text = entry.Note, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(mood);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            JournalList.Children.Add(row);
        }
    }

    private async void OnCellTapped(object? sender, HeatCellTappedEventArgs e)
    {
        try
        {
            await DayEditorFlyout.ShowAsync(e.Anchor, ViewModel, e.Cell.Day, Render);
        }
        catch (Exception ex)
        {
            App.Host.Get<IAppLogger>().Error("Mở trình sửa ngày lỗi", ex);
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
        else
        {
            Frame.Navigate(typeof(HabitsPage));
        }
    }

    /// <summary>Tạm nghỉ riêng thói quen này (F3).</summary>
    private async void OnPauseClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ViewModel.Habit is { } habit && await VacationDialog.ShowAsync(XamlRoot, habit.Id, habit.Name))
            {
                await LoadAsync();
            }
        }
        catch (Exception ex)
        {
            App.Host.Get<IAppLogger>().Error("Tạm nghỉ từ trang chi tiết lỗi", ex);
        }
    }

    /// <summary>Bắt đầu Focus gắn với thói quen này (WidgetHost chạy đồng hồ và cộng phút khi xong).</summary>
    private async void OnFocusClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ViewModel.Habit is { } habit && !await HostLink.StartFocusAsync(habit.Id))
            {
                await new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Content = Ui.T("focus.host.missing"),
                    CloseButtonText = Ui.T("celebrate.close")
                }.ShowAsync();
            }
        }
        catch (Exception ex)
        {
            App.Host.Get<IAppLogger>().Error("Bắt đầu Focus từ trang chi tiết lỗi", ex);
        }
    }

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ViewModel.Habit is { } habit && await HabitEditorDialog.ShowAsync(XamlRoot, habit) is not null)
            {
                await LoadAsync();
            }
        }
        catch (Exception ex)
        {
            App.Host.Get<IAppLogger>().Error("Sửa thói quen từ trang chi tiết lỗi", ex);
        }
    }
}
