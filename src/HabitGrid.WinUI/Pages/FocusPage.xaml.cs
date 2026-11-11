using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Focus;
using HabitGrid.Presentation.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

namespace HabitGrid.WinUI.Pages;

/// <summary>
/// Trang Focus trong app: đồng hồ lớn (Pomodoro / đếm ngược / bấm giờ) gắn với một thói quen, điều khiển phiên đang chạy ở WidgetHost
/// (phiên tiếp tục kể cả khi đóng app) và thống kê tập trung. Phút tự cộng vào thói quen loại Thời lượng khi phiên kết thúc.
/// </summary>
public sealed partial class FocusPage : Page
{
    private readonly StackPanel _root = new() { Spacing = 16, Padding = new Thickness(36, 28, 36, 32), MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _tick;
    private FocusMode _mode = FocusMode.Pomodoro;
    private NumberBox? _work;
    private NumberBox? _break;
    private ComboBox? _habit;
    private Microsoft.UI.Xaml.Shapes.Path? _arc;
    private TextBlock? _time;
    private TextBlock? _phase;
    private Button? _primary;
    private Button? _stop;
    private StackPanel? _setup;
    private bool _wasActive;
    private bool _built;

    public FocusPage()
    {
        Model = App.Host.Get<FocusPageModel>();
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        Scroll.Content = _root;
        _tick = DispatcherQueue.CreateTimer();
        _tick.Interval = TimeSpan.FromMilliseconds(500);
        _tick.Tick += async (_, _) => await RefreshAsync();
    }

    public FocusPageModel Model { get; }

    /// <summary>Hoàn tất khi đã nạp và dựng xong (smoke test chờ).</summary>
    public Task Ready { get; private set; } = Task.CompletedTask;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Ready = LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _tick.Stop();
    }

    private async Task LoadAsync()
    {
        try
        {
            await Model.LoadAsync();
            _mode = Model.Choice.Mode;
            Build();
            await RefreshAsync();
            _tick.Start();
        }
        catch (Exception ex)
        {
            App.Host.Get<IAppLogger>().Error("Mở trang Focus lỗi", ex);
        }
    }

    private static Style Sty(string key) => (Style)Application.Current.Resources[key];

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    private Border Card() => new()
    {
        CornerRadius = (CornerRadius)Application.Current.Resources["RadiusLg"],
        Background = Res("CardIdleBrush"),
        BorderBrush = Res("StrokeSubtleBrush"),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(20),
        VerticalAlignment = VerticalAlignment.Top
    };

    private void Build()
    {
        _root.Children.Clear();
        _root.Children.Add(new TextBlock { Text = Ui.T("focus.title"), Style = Sty("TitleDisplayTextStyle") });

        var body = new Grid { ColumnSpacing = 16 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 280 });
        body.Children.Add(BuildTimerCard());
        var stats = BuildStatsCard();
        Grid.SetColumn(stats, 1);
        body.Children.Add(stats);
        _root.Children.Add(body);
        _built = true;
    }

    private FrameworkElement BuildTimerCard()
    {
        var card = Card();
        var stack = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center };

        // Vòng tiến độ tự vẽ (ProgressRing của WinUI không dựng ổn định ngoài màn hình và khó điều khiển góc): nền + cung tròn.
        _arc = new Microsoft.UI.Xaml.Shapes.Path { Stroke = Res("AccentBrandBrush"), StrokeThickness = 12, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        var track = new Ellipse { Width = 228, Height = 228, StrokeThickness = 12, Stroke = Res("HeatEmptyBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _time = new TextBlock { FontSize = 52, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _phase = new TextBlock { Style = Sty("BodySecondaryTextStyle"), HorizontalAlignment = HorizontalAlignment.Center };
        var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 2 };
        center.Children.Add(_time);
        center.Children.Add(_phase);
        var ringHost = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
        ringHost.Width = 240;
        ringHost.Height = 240;
        ringHost.Children.Add(track);
        ringHost.Children.Add(_arc);
        ringHost.Children.Add(center);
        AutomationProperties.SetName(ringHost, Ui.T("focus.title"));
        stack.Children.Add(ringHost);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        _primary = new Button { Style = Sty("GradientButtonStyle"), MinWidth = 130 };
        _primary.Click += async (_, _) => await OnPrimaryAsync();
        _stop = new Button { Style = Sty("GhostButtonStyle"), Content = Ui.T("focus.stop"), Visibility = Visibility.Collapsed };
        AutomationProperties.SetName(_stop, Ui.T("focus.stop"));
        _stop.Click += async (_, _) =>
        {
            await Model.StopAsync();
            await Task.Delay(1300);
            await ReloadStatsAsync();
        };
        buttons.Children.Add(_primary);
        buttons.Children.Add(_stop);
        stack.Children.Add(buttons);

        stack.Children.Add(BuildSetup());
        card.Child = stack;
        return card;
    }

    /// <summary>Chọn chế độ, thời lượng và thói quen (chỉ hiện khi chưa có phiên).</summary>
    private StackPanel BuildSetup()
    {
        _setup = new StackPanel { Spacing = 10, MinWidth = 320 };

        var modes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        var toggles = new List<ToggleButton>();

        foreach (var (mode, key) in new[] { (FocusMode.Pomodoro, "focus.mode.pomodoro"), (FocusMode.Countdown, "focus.mode.countdown"), (FocusMode.Stopwatch, "focus.mode.stopwatch") })
        {
            var chip = new ToggleButton { Style = Sty("ChipStyle"), Content = Ui.T(key), IsChecked = _mode == mode };
            AutomationProperties.SetName(chip, Ui.T(key));
            chip.Click += (_, _) =>
            {
                _mode = mode;
                toggles.ForEach(t => t.IsChecked = ReferenceEquals(t, chip));
                ApplyModeVisibility();
            };
            toggles.Add(chip);
            modes.Children.Add(chip);
        }

        _setup.Children.Add(modes);

        var numbers = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };
        _work = new NumberBox { Header = Ui.T("focus.minutes"), Minimum = 1, Maximum = 600, Value = Model.Choice.WorkMinutes, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, SmallChange = 5, Width = 150 };
        _break = new NumberBox { Header = Ui.T("focus.break"), Minimum = 0, Maximum = 120, Value = Model.Choice.BreakMinutes, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, SmallChange = 1, Width = 150 };
        numbers.Children.Add(_work);
        numbers.Children.Add(_break);
        _setup.Children.Add(numbers);

        _habit = new ComboBox { Header = Ui.T("focus.habit"), HorizontalAlignment = HorizontalAlignment.Stretch };
        _habit.Items.Add(new ComboBoxItem { Content = Ui.T("focus.habit.none"), Tag = null });

        foreach (var habit in Model.Candidates)
        {
            _habit.Items.Add(new ComboBoxItem { Content = $"{Ui.IconText(habit.Icon.Value)} {habit.Name}", Tag = habit.Id });
        }

        _habit.SelectedIndex = Math.Max(0, Model.Candidates.ToList().FindIndex(h => h.Id == Model.Choice.HabitId) + 1);
        AutomationProperties.SetName(_habit, Ui.T("focus.habit"));
        _setup.Children.Add(_habit);
        _setup.Children.Add(new TextBlock { Text = Ui.T("focus.hint"), Style = Sty("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });

        ApplyModeVisibility();
        return _setup;
    }

    private void ApplyModeVisibility()
    {
        if (_work is not null)
        {
            _work.Visibility = _mode == FocusMode.Stopwatch ? Visibility.Collapsed : Visibility.Visible;
        }

        if (_break is not null)
        {
            _break.Visibility = _mode == FocusMode.Pomodoro ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async Task OnPrimaryAsync()
    {
        var display = await Model.DisplayAsync(DateTime.UtcNow);

        if (display.IsActive)
        {
            await Model.ToggleAsync(DateTime.UtcNow);
            return;
        }

        var habit = (_habit?.SelectedItem as ComboBoxItem)?.Tag as Guid?;
        var work = double.IsNaN(_work?.Value ?? double.NaN) ? 25 : (int)_work!.Value;
        var rest = double.IsNaN(_break?.Value ?? double.NaN) ? 5 : (int)_break!.Value;
        await Model.StartAsync(new FocusChoice(_mode, work, rest, habit), DateTime.UtcNow);
    }

    private FrameworkElement BuildStatsCard()
    {
        var card = Card();
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(new TextBlock { Text = Ui.T("focus.stats"), Style = Sty("CardTitleTextStyle") });

        var s = Model.Stats;
        var tiles = new Grid { ColumnSpacing = 10 };

        for (var i = 0; i < 3; i++)
        {
            tiles.ColumnDefinitions.Add(new ColumnDefinition());
        }

        AddTile(tiles, 0, Ui.T("focus.today"), Duration(s.TodayMinutes));
        AddTile(tiles, 1, Ui.T("focus.week"), Duration(s.WeekMinutes));
        AddTile(tiles, 2, Ui.T("focus.total"), Duration(s.TotalMinutes));
        stack.Children.Add(tiles);

        stack.Children.Add(BuildBars(s));

        stack.Children.Add(new TextBlock { Text = Ui.T("focus.recent"), Style = Sty("CardTitleTextStyle"), Margin = new Thickness(0, 6, 0, 0) });

        if (Model.Recent.Count == 0)
        {
            stack.Children.Add(new TextBlock { Text = Ui.T("focus.none"), Style = Sty("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });
        }

        var names = Model.Candidates.ToDictionary(h => h.Id, h => h.Name);

        foreach (var record in Model.Recent)
        {
            var habit = record.HabitId is { } id && names.TryGetValue(id, out var n) ? n : Ui.T("focus.habit.none");
            var row = new TextBlock
            {
                Text = $"{record.EndedAtUtc.ToLocalTime():dd/MM HH:mm} · {Ui.Loc.Format("focus.min", record.Minutes)} · {habit}",
                Style = Sty("BodySecondaryTextStyle"),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            stack.Children.Add(row);
        }

        card.Child = stack;
        return card;
    }

    private static string Duration(int minutes)
    {
        var (h, m) = FocusStats.Split(minutes);
        return h > 0 ? Ui.Loc.Format("focus.hm", h, m) : Ui.Loc.Format("focus.min", m);
    }

    private static void AddTile(Grid grid, int column, string label, string value)
    {
        var tile = new StackPanel { Spacing = 2 };
        tile.Children.Add(new TextBlock { Text = value, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        tile.Children.Add(new TextBlock { Text = label, Style = Sty("BodySecondaryTextStyle") });
        AutomationProperties.SetName(tile, $"{label}: {value}");
        Grid.SetColumn(tile, column);
        grid.Children.Add(tile);
    }

    /// <summary>Biểu đồ cột 7 ngày gần nhất; cột cao nhất là mốc 100%.</summary>
    private FrameworkElement BuildBars(FocusStatsData s)
    {
        var max = Math.Max(1, s.Last7Days.Max(d => d.Minutes));
        var bars = new Grid { ColumnSpacing = 8, Height = 110 };
        var culture = Ui.CurrentCulture;

        for (var i = 0; i < s.Last7Days.Count; i++)
        {
            bars.ColumnDefinitions.Add(new ColumnDefinition());
            var (day, minutes) = s.Last7Days[i];
            var column = new Grid();
            column.RowDefinitions.Add(new RowDefinition());
            column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var bar = new Rectangle
            {
                Height = Math.Max(minutes == 0 ? 3 : 6, 80.0 * minutes / max),
                RadiusX = 5,
                RadiusY = 5,
                VerticalAlignment = VerticalAlignment.Bottom,
                Fill = minutes == 0 ? Res("HeatEmptyBrush") : Res("AccentBrandBrush")
            };
            column.Children.Add(bar);
            var label = new TextBlock { Text = culture.DateTimeFormat.GetAbbreviatedDayName(day.DayOfWeek), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Style = Sty("BodySecondaryTextStyle") };
            Grid.SetRow(label, 1);
            column.Children.Add(label);
            AutomationProperties.SetName(column, $"{Ui.LongDate(day)}: {Ui.Loc.Format("focus.min", minutes)}");
            Grid.SetColumn(column, i);
            bars.Children.Add(column);
        }

        return bars;
    }

    private async Task ReloadStatsAsync()
    {
        try
        {
            await Model.LoadAsync();
            Build();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            App.Host.Get<IAppLogger>().Error("Nạp lại thống kê Focus lỗi", ex);
        }
    }

    /// <summary>Làm mới đồng hồ (mỗi 0,5 giây). Phiên vừa kết thúc thì nạp lại thống kê.</summary>
    public async Task RefreshAsync()
    {
        if (!_built || _time is null || _arc is null || _phase is null || _primary is null || _stop is null || _setup is null)
        {
            return;
        }

        var display = await Model.DisplayAsync(DateTime.UtcNow);
        var span = display.Remaining ?? display.Elapsed;
        _time.Text = FormatSpan(display.IsActive ? span : DefaultSpan());
        _arc.Data = ArcGeometry(display.Fraction, 114);

        _phase.Text = display.Phase switch
        {
            FocusPhase.Work when display.IsPaused => Ui.T("focus.phase.paused"),
            FocusPhase.Work => Ui.T("focus.phase.work"),
            FocusPhase.Break => Ui.T("focus.phase.break"),
            _ => Ui.T("focus.phase.ready")
        };

        _primary.Content = display.IsActive ? (display.Phase == FocusPhase.Break ? Ui.T("focus.skipBreak") : display.IsPaused ? Ui.T("focus.resume") : Ui.T("focus.pause")) : Ui.T("focus.start");
        AutomationProperties.SetName(_primary, _primary.Content.ToString() ?? string.Empty);
        _stop.Visibility = display.IsActive ? Visibility.Visible : Visibility.Collapsed;
        _setup.Visibility = display.IsActive ? Visibility.Collapsed : Visibility.Visible;

        if (_wasActive && !display.IsActive)
        {
            _wasActive = false;
            await ReloadStatsAsync();
            return;
        }

        _wasActive = display.IsActive;
    }

    /// <summary>Cung từ 12 giờ theo chiều kim đồng hồ chiếm <paramref name="fraction"/> vòng; rỗng khi 0.</summary>
    private static Geometry? ArcGeometry(double fraction, double radius)
    {
        var f = Math.Clamp(fraction, 0, 0.9999);

        if (f <= 0.001)
        {
            return null;
        }

        var middle = 120.0;
        var angle = f * 2 * Math.PI;
        var figure = new PathFigure { StartPoint = new Windows.Foundation.Point(middle, middle - radius) };
        figure.Segments.Add(new ArcSegment
        {
            Point = new Windows.Foundation.Point(middle + (radius * Math.Sin(angle)), middle - (radius * Math.Cos(angle))),
            Size = new Windows.Foundation.Size(radius, radius),
            IsLargeArc = f > 0.5,
            SweepDirection = SweepDirection.Clockwise
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private TimeSpan DefaultSpan() => _mode == FocusMode.Stopwatch ? TimeSpan.Zero : TimeSpan.FromMinutes(double.IsNaN(_work?.Value ?? double.NaN) ? 25 : _work!.Value);

    private static string FormatSpan(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{span.Minutes:00}:{span.Seconds:00}";
}
