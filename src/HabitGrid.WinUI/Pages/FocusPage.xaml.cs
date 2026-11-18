using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Focus;
using HabitGrid.Presentation.Services;
using HabitGrid.WinUI.Controls;
using HabitGrid.WinUI.Focus;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

namespace HabitGrid.WinUI.Pages;

/// <summary>
/// Trang Focus (v3, lưới Bento): đồng hồ lớn vòng sáng (Pomodoro / đếm ngược / bấm giờ) với chip chọn nhanh số phút, thống kê tập trung
/// và bàn trộn âm thanh nền tự tạo. Phiên chạy ở WidgetHost (tiếp tục kể cả khi đóng app); phút tự cộng vào thói quen loại Thời lượng.
/// </summary>
public sealed partial class FocusPage : Page
{
    private static readonly int[] WorkChoices = [15, 25, 45, 60];
    private static readonly int[] BreakChoices = [5, 10, 15];
    private const int WaveBars = 36;
    private const double RingSize = 280;
    private const double RingRadius = 133;

    private readonly StackPanel _root = new() { Spacing = 18, Padding = new Thickness(32, 24, 32, 32) };
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _tick;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _wave;
    private readonly List<Rectangle> _bars = [];
    private FocusMode _mode = FocusMode.Pomodoro;
    private int _workMinutes = 25;
    private int _breakMinutes = 5;
    private ComboBox? _habit;
    private Microsoft.UI.Xaml.Shapes.Path? _arc;
    private Microsoft.UI.Xaml.Shapes.Path? _glow;
    private TextBlock? _time;
    private TextBlock? _phase;
    private Button? _primary;
    private Button? _stop;
    private StackPanel? _setup;
    private StackPanel? _workRow;
    private StackPanel? _breakRow;
    private Button? _play;
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
        _wave = DispatcherQueue.CreateTimer();
        _wave.Interval = TimeSpan.FromMilliseconds(90);
        _wave.Tick += (_, _) => DrawWave();
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
        _wave.Stop();
        AmbientMixer.Instance.Changed -= OnMixerChanged;
    }

    private async Task LoadAsync()
    {
        try
        {
            await Model.LoadAsync();
            await AmbientMixer.Instance.LoadAsync(App.Host.Get<ISettingsStore>());
            _mode = Model.Choice.Mode;
            _workMinutes = Math.Max(1, Model.Choice.WorkMinutes);
            _breakMinutes = Math.Max(0, Model.Choice.BreakMinutes);
            Build();
            await RefreshAsync();
            _tick.Start();
            AmbientMixer.Instance.Changed += OnMixerChanged;
            OnMixerChanged(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            App.Host.Get<IAppLogger>().Error("Mở trang Focus lỗi", ex);
        }
    }

    private static Style Sty(string key) => (Style)Application.Current.Resources[key];

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    private static Border Glass(UIElement child) => new() { Style = Sty("GlassCard"), Child = child };

    private void Build()
    {
        _root.Children.Clear();
        _bars.Clear();
        _root.Children.Add(new TextBlock { Text = Ui.T("focus.title"), Style = Sty("TitleDisplayTextStyle") });

        var grid = new BentoGrid();
        grid.Add(BuildTimerCard(), columns: 2, rows: 2);
        grid.Add(BuildStatsCard(), columns: 2);
        grid.Add(BuildMixerCard(), columns: 2);
        _root.Children.Add(grid);
        _built = true;
    }

    // ---------- Đồng hồ ----------

    private FrameworkElement BuildTimerCard()
    {
        var stack = new StackPanel { Spacing = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        // Vòng "Liquid Neon": cung gradient + một cung dày, mờ phía sau làm quầng sáng; nền là vòng tròn nhạt.
        var track = new Ellipse { Width = RingSize - 14, Height = RingSize - 14, StrokeThickness = 14, Stroke = Res("HeatEmptyBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _glow = new Microsoft.UI.Xaml.Shapes.Path { Stroke = Res("AccentGradientBrush"), StrokeThickness = 30, Opacity = 0.22, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        _arc = new Microsoft.UI.Xaml.Shapes.Path { Stroke = Res("AccentGradientBrush"), StrokeThickness = 14, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        _time = new TextBlock { FontSize = 60, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, FontFamily = (FontFamily)Application.Current.Resources["DisplayFont"] };
        _phase = new TextBlock { Style = Sty("BodySecondaryTextStyle"), HorizontalAlignment = HorizontalAlignment.Center };
        var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 2 };
        center.Children.Add(_time);
        center.Children.Add(_phase);
        var ringHost = new Grid { Width = RingSize, Height = RingSize, HorizontalAlignment = HorizontalAlignment.Center };
        ringHost.Children.Add(track);
        ringHost.Children.Add(_glow);
        ringHost.Children.Add(_arc);
        ringHost.Children.Add(center);
        AutomationProperties.SetName(ringHost, Ui.T("focus.title"));
        stack.Children.Add(ringHost);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        _primary = new Button { Style = Sty("GradientButtonStyle"), MinWidth = 140 };
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
        return Glass(stack);
    }

    /// <summary>Chọn chế độ, số phút (chip chọn nhanh) và thói quen; chỉ hiện khi chưa có phiên.</summary>
    private StackPanel BuildSetup()
    {
        _setup = new StackPanel { Spacing = 12, MaxWidth = 460 };

        var modes = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        var toggles = new List<ToggleButton>();

        foreach (var (mode, key) in new[] { (FocusMode.Pomodoro, "focus.mode.pomodoro"), (FocusMode.Countdown, "focus.mode.countdown"), (FocusMode.Stopwatch, "focus.mode.stopwatch") })
        {
            var chip = new ToggleButton { Style = Sty("ChipStyle"), Content = Ui.T(key), IsChecked = _mode == mode };
            AutomationProperties.SetName(chip, Ui.T(key));
            chip.Click += async (_, _) =>
            {
                _mode = mode;
                toggles.ForEach(t => t.IsChecked = ReferenceEquals(t, chip));
                ApplyModeVisibility();
                await RefreshAsync();
            };
            toggles.Add(chip);
            modes.Children.Add(chip);
        }

        _setup.Children.Add(modes);

        _workRow = MinuteRow(Ui.T("focus.minutes"), WorkChoices, () => _workMinutes, v => _workMinutes = v);
        _breakRow = MinuteRow(Ui.T("focus.break"), BreakChoices, () => _breakMinutes, v => _breakMinutes = v);
        _setup.Children.Add(_workRow);
        _setup.Children.Add(_breakRow);

        _habit = new ComboBox { Header = Ui.T("focus.habit"), HorizontalAlignment = HorizontalAlignment.Stretch };
        _habit.Items.Add(new ComboBoxItem { Content = Ui.T("focus.habit.none"), Tag = null });

        foreach (var habit in Model.Candidates)
        {
            _habit.Items.Add(new ComboBoxItem { Content = $"{Ui.IconText(habit.Icon.Value)} {habit.Name}", Tag = habit.Id });
        }

        _habit.SelectedIndex = Math.Max(0, Model.Candidates.ToList().FindIndex(h => h.Id == Model.Choice.HabitId) + 1);
        AutomationProperties.SetName(_habit, Ui.T("focus.habit"));
        _setup.Children.Add(_habit);
        _setup.Children.Add(new TextBlock { Text = Ui.T("focus.hint"), Style = Sty("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });

        ApplyModeVisibility();
        return _setup;
    }

    /// <summary>Hàng chip số phút (thay ô nhập số có mũi tên nhỏ). Số phút đang lưu mà không có trong danh sách thì thêm làm một chip.</summary>
    private StackPanel MinuteRow(string label, int[] choices, Func<int> get, Action<int> set)
    {
        var row = new StackPanel { Spacing = 6 };
        row.Children.Add(new TextBlock { Text = label, Style = Sty("BodySecondaryTextStyle"), HorizontalAlignment = HorizontalAlignment.Center });
        var chips = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        var values = choices.Contains(get()) ? choices : [.. choices, get()];
        var toggles = new List<ToggleButton>();

        foreach (var value in values.Order())
        {
            var text = Ui.Loc.Format("focus.min", value);
            var chip = new ToggleButton { Style = Sty("ChipStyle"), Content = text, IsChecked = value == get(), MinWidth = 72 };
            AutomationProperties.SetName(chip, $"{label}: {text}");
            chip.Click += async (_, _) =>
            {
                set(value);
                toggles.ForEach(t => t.IsChecked = ReferenceEquals(t, chip));
                await RefreshAsync();
            };
            toggles.Add(chip);
            chips.Children.Add(chip);
        }

        row.Children.Add(chips);
        return row;
    }

    private void ApplyModeVisibility()
    {
        if (_workRow is not null)
        {
            _workRow.Visibility = _mode == FocusMode.Stopwatch ? Visibility.Collapsed : Visibility.Visible;
        }

        if (_breakRow is not null)
        {
            _breakRow.Visibility = _mode == FocusMode.Pomodoro ? Visibility.Visible : Visibility.Collapsed;
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
        await Model.StartAsync(new FocusChoice(_mode, _workMinutes, _breakMinutes, habit), DateTime.UtcNow);
    }

    // ---------- Thống kê ----------

    private FrameworkElement BuildStatsCard()
    {
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

        stack.Children.Add(new TextBlock { Text = Ui.T("focus.recent"), Style = Sty("CardTitleTextStyle"), Margin = new Thickness(0, 4, 0, 0) });

        if (Model.Recent.Count == 0)
        {
            stack.Children.Add(new TextBlock { Text = Ui.T("focus.none"), Style = Sty("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });
        }

        var names = Model.Candidates.ToDictionary(h => h.Id, h => h.Name);

        foreach (var record in Model.Recent.Take(4))
        {
            var habit = record.HabitId is { } id && names.TryGetValue(id, out var n) ? n : Ui.T("focus.habit.none");
            stack.Children.Add(new TextBlock
            {
                Text = $"{record.EndedAtUtc.ToLocalTime():dd/MM HH:mm} · {Ui.Loc.Format("focus.min", record.Minutes)} · {habit}",
                Style = Sty("BodySecondaryTextStyle"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        return Glass(stack);
    }

    private static string Duration(int minutes)
    {
        var (h, m) = FocusStats.Split(minutes);
        return h > 0 ? Ui.Loc.Format("focus.hm", h, m) : Ui.Loc.Format("focus.min", m);
    }

    private static void AddTile(Grid grid, int column, string label, string value)
    {
        var tile = new Border { CornerRadius = new CornerRadius(14), Background = Res("SurfaceBrush"), Padding = new Thickness(14, 10, 14, 10) };
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new TextBlock { Text = value, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = label, Style = Sty("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });
        tile.Child = stack;
        AutomationProperties.SetName(tile, $"{label}: {value}");
        Grid.SetColumn(tile, column);
        grid.Children.Add(tile);
    }

    /// <summary>Biểu đồ cột 7 ngày gần nhất (cột mảnh bo tròn); cột cao nhất là mốc 100%.</summary>
    private static FrameworkElement BuildBars(FocusStatsData s)
    {
        var max = Math.Max(1, s.Last7Days.Max(d => d.Minutes));
        var bars = new Grid { ColumnSpacing = 10, Height = 120 };
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
                Width = 18,
                Height = Math.Max(minutes == 0 ? 4 : 8, 90.0 * minutes / max),
                RadiusX = 9,
                RadiusY = 9,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
                Fill = minutes == 0 ? Res("HeatEmptyBrush") : Res("AccentGradientBrush")
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

    // ---------- Bàn trộn âm thanh ----------

    private FrameworkElement BuildMixerCard()
    {
        var mixer = AmbientMixer.Instance;
        var settings = App.Host.Get<ISettingsStore>();
        var stack = new StackPanel { Spacing = 12 };

        var head = new Grid { ColumnSpacing = 12 };
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel { Spacing = 2 };
        titles.Children.Add(new TextBlock { Text = Ui.T("focus.ambient.title"), Style = Sty("CardTitleTextStyle") });
        titles.Children.Add(new TextBlock { Text = Ui.T("focus.ambient.hint"), Style = Sty("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });
        head.Children.Add(titles);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
        _play = new Button { Style = Sty("GradientButtonStyle"), MinWidth = 96 };
        _play.Click += async (_, _) =>
        {
            if (mixer.IsPlaying)
            {
                mixer.Pause();
            }
            else
            {
                await mixer.PlayAsync();
            }
        };
        var minimize = new Button { Style = Sty("GhostButtonStyle"), Content = Ui.T("focus.minimize") };
        AutomationProperties.SetName(minimize, Ui.T("focus.minimize"));
        ToolTipService.SetToolTip(minimize, Ui.T("focus.minimize.hint"));
        minimize.Click += (_, _) =>
        {
            // Thu nhỏ app: phiên Focus tiếp tục ở widget (viên thuốc Dynamic Island hiện đồng hồ) và âm thanh nền vẫn phát.
            if (App.MainWindowInstance?.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.Minimize();
            }
        };
        actions.Children.Add(_play);
        actions.Children.Add(minimize);
        Grid.SetColumn(actions, 1);
        head.Children.Add(actions);
        stack.Children.Add(head);

        // Sóng âm chạy theo mức âm thật của các kênh đang bật.
        var wave = new Grid { Height = 54, ColumnSpacing = 3 };

        for (var i = 0; i < WaveBars; i++)
        {
            wave.ColumnDefinitions.Add(new ColumnDefinition());
            var bar = new Rectangle { RadiusX = 2, RadiusY = 2, Height = 4, VerticalAlignment = VerticalAlignment.Center, Fill = Res("AccentGradientBrush"), Opacity = 0.85 };
            Grid.SetColumn(bar, i);
            wave.Children.Add(bar);
            _bars.Add(bar);
        }

        stack.Children.Add(wave);

        foreach (var kind in Enum.GetValues<AmbientKind>())
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = $"{Emoji(kind)}  {Ui.T("focus.ambient." + kind)}", VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            var slider = new Slider { Minimum = 0, Maximum = 100, Value = mixer.VolumeOf(kind) * 100, StepFrequency = 5, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(slider, Ui.T("focus.ambient." + kind));
            slider.ValueChanged += async (_, e) => await mixer.SetVolumeAsync(kind, e.NewValue / 100, settings);
            Grid.SetColumn(slider, 1);
            row.Children.Add(slider);
            stack.Children.Add(row);
        }

        return Glass(stack);
    }

    private static string Emoji(AmbientKind kind) => kind switch
    {
        AmbientKind.White => "🌫️",
        AmbientKind.Pink => "🌸",
        AmbientKind.Brown => "🍂",
        AmbientKind.Rain => "🌧️",
        _ => "🌊"
    };

    private void OnMixerChanged(object? sender, EventArgs e)
    {
        var playing = AmbientMixer.Instance.IsPlaying;

        if (_play is not null)
        {
            _play.Content = Ui.T(playing ? "focus.ambient.pause" : "focus.ambient.play");
            AutomationProperties.SetName(_play, _play.Content.ToString() ?? string.Empty);
        }

        if (playing)
        {
            _wave.Start();
        }
        else
        {
            _wave.Stop();
            DrawWave();
        }
    }

    private void DrawWave()
    {
        var mixer = AmbientMixer.Instance;

        for (var i = 0; i < _bars.Count; i++)
        {
            // Mỗi cột là mức âm ở một thời điểm lệch nhau 60 ms: sóng trôi từ phải sang trái.
            var level = mixer.LevelAt(-(_bars.Count - i) * 0.06);
            _bars[i].Height = Math.Max(4, level * 50);
        }
    }

    private async Task ReloadStatsAsync()
    {
        try
        {
            await Model.LoadAsync();
            Build();
            await RefreshAsync();
            OnMixerChanged(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            App.Host.Get<IAppLogger>().Error("Nạp lại thống kê Focus lỗi", ex);
        }
    }

    /// <summary>Làm mới đồng hồ (mỗi 0,5 giây). Phiên vừa kết thúc thì nạp lại thống kê.</summary>
    public async Task RefreshAsync()
    {
        if (!_built || _time is null || _arc is null || _glow is null || _phase is null || _primary is null || _stop is null || _setup is null)
        {
            return;
        }

        var display = await Model.DisplayAsync(DateTime.UtcNow);
        var span = display.Remaining ?? display.Elapsed;
        _time.Text = FormatSpan(display.IsActive ? span : DefaultSpan());
        _arc.Data = ArcGeometry(display.Fraction, RingRadius);
        _glow.Data = ArcGeometry(display.Fraction, RingRadius);

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

        var middle = RingSize / 2;
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

    private TimeSpan DefaultSpan() => _mode == FocusMode.Stopwatch ? TimeSpan.Zero : TimeSpan.FromMinutes(_workMinutes);

    private static string FormatSpan(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{span.Minutes:00}:{span.Seconds:00}";
}
