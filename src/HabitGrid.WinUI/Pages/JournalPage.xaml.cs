using HabitGrid.Core.Domain.Journal;
using HabitGrid.Presentation.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace HabitGrid.WinUI.Pages;

/// <summary>
/// Trang Nhật ký (v3, lưới Bento cao hết trang): cột trái là danh sách + tìm kiếm; giữa là vùng viết kính mờ có thanh chèn nhanh và chọn
/// tâm trạng; cột phải là biểu đồ tâm trạng 30 ngày và ảnh khoảnh khắc của ngày đang xem.
/// Tự lưu sau khi ngừng gõ ~0,8 giây và trước khi đổi ngày/rời trang. Dữ liệu chỉ nằm trên máy.
/// </summary>
public sealed partial class JournalPage : Page
{
    private static readonly string[] MoodEmoji = ["😞", "🙁", "😐", "🙂", "😄"];

    private readonly StackPanel _root = new() { Spacing = 18, Padding = new Thickness(32, 24, 32, 32) };
    private readonly DispatcherTimerLite _saveTimer;
    private TextBox? _editor;
    private TextBlock? _status;
    private int? _mood;
    private bool _loading;
    private bool _dirty;

    public JournalPage()
    {
        Model = App.Host.Get<JournalModel>();
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        Scroll.Content = _root;
        _saveTimer = new DispatcherTimerLite(DispatcherQueue, TimeSpan.FromMilliseconds(800), async () => await FlushAsync());
    }

    public JournalModel Model { get; }

    /// <summary>Hoàn tất khi đã nạp và dựng xong (smoke test chờ).</summary>
    public Task Ready { get; private set; } = Task.CompletedTask;

    /// <summary>Ô soạn thảo hiện tại (cho smoke test).</summary>
    public TextBox? Editor => _editor;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Ready = ReloadAsync(() => Model.LoadAsync());
    }

    protected override async void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        base.OnNavigatingFrom(e);
        await FlushAsync();
    }

    private async Task ReloadAsync(Func<Task> load)
    {
        try
        {
            await FlushAsync();
            await load();
            Build();
        }
        catch (Exception ex)
        {
            App.Host.Get<IAppLogger>().Error("Mở trang Nhật ký lỗi", ex);
        }
    }

    /// <summary>Lưu ngay nếu có thay đổi chưa lưu.</summary>
    public async Task FlushAsync()
    {
        _saveTimer.Stop();

        if (!_dirty || _editor is null)
        {
            return;
        }

        _dirty = false;

        try
        {
            await Model.SaveAsync(_editor.Text, _mood);

            if (_status is not null)
            {
                _status.Text = Ui.T("journal.saved");
            }
        }
        catch (Exception ex)
        {
            App.Host.Get<IAppLogger>().Error("Lưu nhật ký lỗi", ex);

            if (_status is not null)
            {
                _status.Text = Ui.T("journal.failed");
            }
        }
    }

    private static Style Sty(string key) => (Style)Application.Current.Resources[key];

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    private void Build()
    {
        _root.Children.Clear();
        _root.Children.Add(BuildHeader());

        // Hai hàng tối thiểu 300: lưới cao gần hết khung nhìn, không còn nửa dưới trống.
        var body = new HabitGrid.WinUI.Controls.BentoGrid { MinRowHeight = 300 };
        body.Add(BuildList(), columns: 1, rows: 2);
        body.Add(BuildEditor(), columns: 2, rows: 2);
        body.Add(BuildMoodChart(), columns: 1);
        body.Add(BuildMoments(), columns: 1);
        _root.Children.Add(body);
    }

    private FrameworkElement BuildHeader()
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = Ui.T("journal.title"), Style = Sty("TitleDisplayTextStyle") });

        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        nav.Children.Add(NavButton("", "journal.prev", true, () => Model.ShiftDayAsync(-1)));
        var today = new Button { Style = Sty("GhostButtonStyle"), Content = Ui.T("journal.today") };
        today.Click += async (_, _) => await ReloadAsync(() => Model.LoadAsync(Model.Today));
        nav.Children.Add(today);
        nav.Children.Add(NavButton("", "journal.next", Model.CanGoNext, () => Model.ShiftDayAsync(1)));
        Grid.SetColumn(nav, 1);
        row.Children.Add(nav);
        return row;
    }

    private Button NavButton(string glyph, string nameKey, bool enabled, Func<Task> action)
    {
        var button = new Button { Style = Sty("GhostButtonStyle"), Content = new FontIcon { Glyph = glyph, FontSize = 14 }, IsEnabled = enabled };
        AutomationProperties.SetName(button, Ui.T(nameKey));
        button.Click += async (_, _) => await ReloadAsync(action);
        return button;
    }

    private static Border Card() => new() { Style = Sty("GlassCard") };

    private FrameworkElement BuildList()
    {
        var card = Card();
        var stack = new StackPanel { Spacing = 10 };

        var search = new TextBox { PlaceholderText = Ui.T("journal.search"), Text = Model.Query, MaxLength = 60 };
        AutomationProperties.SetName(search, Ui.T("journal.search"));
        search.KeyDown += async (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                await ReloadAsync(() => Model.SearchAsync(search.Text));
            }
        };
        search.TextChanged += async (_, _) =>
        {
            // Xóa ô tìm thì trả về danh sách gần đây ngay, không cần Enter.
            if (search.Text.Length == 0 && Model.Query.Length > 0)
            {
                await ReloadAsync(() => Model.SearchAsync(string.Empty));
            }
        };
        stack.Children.Add(search);

        if (Model.Entries.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = Model.Query.Length > 0 ? Ui.T("journal.noResults") : Ui.T("journal.empty"),
                Style = Sty("BodySecondaryTextStyle"),
                TextWrapping = TextWrapping.Wrap
            });
        }

        foreach (var entry in Model.Entries.Take(60))
        {
            stack.Children.Add(BuildListRow(entry));
        }

        card.Child = stack;
        return card;
    }

    private FrameworkElement BuildListRow(JournalEntry entry)
    {
        var selected = entry.Day == Model.SelectedDay;
        var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(10, 8, 10, 8), CornerRadius = (CornerRadius)Application.Current.Resources["RadiusMd"] };
        row.Background = selected ? (Brush)new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0x8B, 0x5C, 0xF6)) : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());

        row.Children.Add(new TextBlock { Text = entry.Mood is { } m ? MoodEmoji[m - 1] : "📝", FontSize = 20, VerticalAlignment = VerticalAlignment.Center });

        var texts = new StackPanel { Spacing = 2 };
        texts.Children.Add(new TextBlock { Text = Ui.LongDate(entry.Day), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        texts.Children.Add(new TextBlock { Text = entry.Preview(), Style = Sty("BodySecondaryTextStyle"), TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(texts, 1);
        row.Children.Add(texts);

        AutomationProperties.SetName(row, $"{Ui.LongDate(entry.Day)}: {entry.Preview(40)}");
        row.Tapped += async (_, _) => await ReloadAsync(() => Model.SelectDayAsync(entry.Day));
        return row;
    }

    private FrameworkElement BuildEditor()
    {
        var card = Card();
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(new TextBlock { Text = Ui.LongDate(Model.SelectedDay), Style = Sty("CardTitleTextStyle") });

        // Tâm trạng: bấm lại để bỏ chọn.
        _mood = Model.Current?.Mood;
        var moods = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var buttons = new List<ToggleButton>();

        for (var i = 0; i < MoodEmoji.Length; i++)
        {
            var value = i + 1;
            var button = new ToggleButton { Content = MoodEmoji[i], FontSize = 20, Padding = new Thickness(10, 6, 10, 6), IsChecked = _mood == value };
            AutomationProperties.SetName(button, $"{Ui.T("detail.day.mood")} {value}/5");
            button.Click += (_, _) =>
            {
                _mood = _mood == value ? null : value;

                for (var j = 0; j < buttons.Count; j++)
                {
                    buttons[j].IsChecked = _mood == j + 1;
                }

                MarkDirty();
            };
            buttons.Add(button);
            moods.Children.Add(button);
        }

        stack.Children.Add(moods);

        // Thanh chèn nhanh: gạch đầu dòng, việc cần làm, giờ hiện tại, câu gợi ý biết ơn (nhật ký là văn bản thường, không có định dạng ẩn).
        var tools = new HabitGrid.WinUI.Controls.WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };

        foreach (var (label, key, insert) in new (string, string, Func<string>)[]
        {
            ("•", "journal.tool.bullet", () => "• "),
            ("☐", "journal.tool.todo", () => "☐ "),
            ("🕒", "journal.tool.time", () => DateTime.Now.ToString("HH:mm", Ui.CurrentCulture) + " "),
            ("🙏", "journal.tool.grateful", () => Ui.T("journal.tool.grateful.text") + " ")
        })
        {
            var button = new Button { Style = Sty("GhostButtonStyle"), Content = $"{label}  {Ui.T(key)}", Padding = new Thickness(12, 6, 12, 6) };
            AutomationProperties.SetName(button, Ui.T(key));
            button.Click += (_, _) => InsertAtCaret(insert());
            tools.Children.Add(button);
        }

        stack.Children.Add(tools);

        // Guided Reflection Prompts theo ngày
        var promptLabel = new TextBlock { Text = "✨ " + Ui.T("journal.prompts.title"), Style = Sty("BodySecondaryTextStyle"), FontSize = 12 };
        stack.Children.Add(promptLabel);
        var prompts = new HabitGrid.WinUI.Controls.WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };
        foreach (var prompt in HabitGrid.Core.Domain.JournalPrompts.GetForDay(Model.SelectedDay))
        {
            var cat = Ui.T(prompt.CategoryKey);
            var question = Ui.T(prompt.TextKey);
            var chip = new Button { Style = Sty("GhostButtonStyle"), Content = cat, Padding = new Thickness(10, 4, 10, 4), FontSize = 12 };
            ToolTipService.SetToolTip(chip, question);
            AutomationProperties.SetName(chip, $"{cat}: {question}");
            chip.Click += (_, _) => InsertAtCaret($"✨ {question}\n");
            prompts.Children.Add(chip);
        }
        stack.Children.Add(prompts);

        _loading = true;
        _editor = new TextBox
        {
            Text = Model.Current?.Text ?? string.Empty,
            PlaceholderText = Ui.T("journal.placeholder"),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 420,
            MaxLength = JournalRules.MaxLength
        };
        _loading = false;
        AutomationProperties.SetName(_editor, Ui.T("journal.title"));
        _editor.TextChanged += (_, _) => MarkDirty();
        stack.Children.Add(_editor);

        _status = new TextBlock { Style = Sty("BodySecondaryTextStyle"), Text = Model.Current is null ? string.Empty : Ui.T("journal.saved") };
        stack.Children.Add(_status);
        card.Child = stack;
        return card;
    }

    /// <summary>Chèn chữ vào vị trí con trỏ; đang ở giữa dòng mà chèn gạch đầu dòng/việc cần làm thì xuống dòng trước.</summary>
    private void InsertAtCaret(string text)
    {
        if (_editor is null)
        {
            return;
        }

        var caret = _editor.SelectionStart;
        var before = _editor.Text[..caret];
        var lineStart = before.Length == 0 || before.EndsWith('\r') || before.EndsWith('\n');
        var insert = (!lineStart && text is "• " or "☐ " ? "\r" : string.Empty) + text;
        _editor.Text = before + insert + _editor.Text[caret..];
        _editor.SelectionStart = caret + insert.Length;
        _editor.Focus(FocusState.Programmatic);
    }

    /// <summary>Biểu đồ tâm trạng 30 ngày: đường nối các ngày có ghi tâm trạng (1–5), chấm tô theo mức.</summary>
    private FrameworkElement BuildMoodChart()
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(new TextBlock { Text = Ui.T("journal.mood.title"), Style = Sty("CardTitleTextStyle"), TextWrapping = TextWrapping.Wrap });

        var points = Model.MoodTrend;

        if (points.Count == 0)
        {
            stack.Children.Add(new TextBlock { Text = Ui.T("journal.mood.empty"), Style = Sty("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });
            return new Border { Style = Sty("GlassCard"), Child = stack };
        }

        const double width = 240, height = 120;
        var canvas = new Canvas { Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Center };
        var first = Model.Today.AddDays(-(HabitGrid.Presentation.Services.JournalModel.MoodDays - 1));
        double X(DateOnly day) => (day.DayNumber - first.DayNumber) / (double)(HabitGrid.Presentation.Services.JournalModel.MoodDays - 1) * (width - 12) + 6;
        double Y(int mood) => height - 8 - ((mood - 1) / 4.0 * (height - 16));

        // Lưới mờ 5 mức.
        for (var m = 1; m <= 5; m++)
        {
            var line = new Microsoft.UI.Xaml.Shapes.Line { X1 = 0, X2 = width, Y1 = Y(m), Y2 = Y(m), Stroke = Res("StrokeSubtleBrush"), StrokeThickness = 1 };
            canvas.Children.Add(line);
        }

        var poly = new Microsoft.UI.Xaml.Shapes.Polyline { Stroke = Res("AccentGradientBrush"), StrokeThickness = 3, StrokeLineJoin = PenLineJoin.Round };

        foreach (var (day, mood) in points)
        {
            poly.Points.Add(new Windows.Foundation.Point(X(day), Y(mood)));
        }

        canvas.Children.Add(poly);

        foreach (var (day, mood) in points)
        {
            var dot = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 10, Height = 10, Fill = Res("AccentBrandBrush"), Stroke = Res("TextPrimaryBrush"), StrokeThickness = 1.5 };
            Canvas.SetLeft(dot, X(day) - 5);
            Canvas.SetTop(dot, Y(mood) - 5);
            ToolTipService.SetToolTip(dot, $"{Ui.LongDate(day)} · {MoodEmoji[mood - 1]}");
            canvas.Children.Add(dot);
        }

        AutomationProperties.SetName(canvas, Ui.Loc.Format("journal.mood.summary", points.Count, Math.Round(points.Average(p => p.Mood), 1)));
        stack.Children.Add(canvas);

        var legend = new Grid();
        legend.ColumnDefinitions.Add(new ColumnDefinition());
        legend.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        legend.Children.Add(new TextBlock { Text = Ui.T("journal.mood.from"), Style = Sty("BodySecondaryTextStyle"), FontSize = 11 });
        var today = new TextBlock { Text = Ui.T("journal.today"), Style = Sty("BodySecondaryTextStyle"), FontSize = 11 };
        Grid.SetColumn(today, 1);
        legend.Children.Add(today);
        stack.Children.Add(legend);
        return new Border { Style = Sty("GlassCard"), Child = stack };
    }

    /// <summary>Ảnh khoảnh khắc (mọi thói quen) của ngày đang xem.</summary>
    private FrameworkElement BuildMoments()
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(new TextBlock { Text = Ui.T("journal.moments.title"), Style = Sty("CardTitleTextStyle"), TextWrapping = TextWrapping.Wrap });
        var moments = App.Host.Get<MomentService>().Store.All().Where(m => m.Day == Model.SelectedDay).Take(4).ToList();

        if (moments.Count == 0)
        {
            stack.Children.Add(new TextBlock { Text = Ui.T("journal.moments.empty"), Style = Sty("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });
            return new Border { Style = Sty("GlassCard"), Child = stack };
        }

        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());

        for (var i = 0; i < moments.Count; i++)
        {
            var image = new Image { Stretch = Stretch.UniformToFill, Height = 96 };
            var frame = new Border { CornerRadius = new CornerRadius(12), Child = image, Background = Res("SurfaceBrush") };
            Grid.SetColumn(frame, i % 2);
            Grid.SetRow(frame, i / 2);
            grid.Children.Add(frame);
            var path = moments[i].Path;
            _ = LoadThumbAsync(image, path);
        }

        stack.Children.Add(grid);
        return new Border { Style = Sty("GlassCard"), Child = stack };
    }

    private static async Task LoadThumbAsync(Image image, string path)
    {
        try
        {
            image.Source = await HabitGrid.WinUI.Moments.MomentCapture.LoadNowAsync(path, 240);
        }
        catch (Exception ex)
        {
            App.Host.Get<IAppLogger>().Error("Nạp ảnh khoảnh khắc cho nhật ký lỗi", ex);
        }
    }

    private void MarkDirty()
    {
        if (_loading)
        {
            return;
        }

        _dirty = true;

        if (_status is not null)
        {
            _status.Text = Ui.T("journal.saving");
        }

        _saveTimer.Restart();
    }
}

/// <summary>Bộ hẹn giờ một lần dùng lại (khởi động lại mỗi lần gõ) trên luồng giao diện.</summary>
internal sealed class DispatcherTimerLite
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;

    public DispatcherTimerLite(Microsoft.UI.Dispatching.DispatcherQueue queue, TimeSpan interval, Func<Task> action)
    {
        _timer = queue.CreateTimer();
        _timer.Interval = interval;
        _timer.IsRepeating = false;
        _timer.Tick += async (_, _) => await action();
    }

    public void Restart()
    {
        _timer.Stop();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();
}
