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
/// Trang Nhật ký: viết một đoạn cho mỗi ngày kèm tâm trạng, xem lại các ngày gần đây và tìm trong nhật ký.
/// Tự lưu sau khi ngừng gõ ~0,8 giây và trước khi đổi ngày/rời trang. Dữ liệu chỉ nằm trên máy.
/// </summary>
public sealed partial class JournalPage : Page
{
    private static readonly string[] MoodEmoji = ["😞", "🙁", "😐", "🙂", "😄"];

    private readonly StackPanel _root = new() { Spacing = 16, Padding = new Thickness(36, 28, 36, 32), MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Stretch };
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

        var body = new Grid { ColumnSpacing = 16 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 260 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        body.Children.Add(BuildList());
        var editor = BuildEditor();
        Grid.SetColumn(editor, 1);
        body.Children.Add(editor);
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

    private Border Card() => new()
    {
        CornerRadius = (CornerRadius)Application.Current.Resources["RadiusLg"],
        Background = Res("CardIdleBrush"),
        BorderBrush = Res("StrokeSubtleBrush"),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(16),
        VerticalAlignment = VerticalAlignment.Top
    };

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

        _loading = true;
        _editor = new TextBox
        {
            Text = Model.Current?.Text ?? string.Empty,
            PlaceholderText = Ui.T("journal.placeholder"),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 260,
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
