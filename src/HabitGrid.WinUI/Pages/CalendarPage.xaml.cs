using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Calendar;
using HabitGrid.Presentation.Services;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;

namespace HabitGrid.WinUI.Pages;

/// <summary>
/// Trang Lịch: lưới tháng tô đậm theo tỷ lệ việc đã xong mỗi ngày, chọn một ngày để xem lại và sửa (check, tăng/giảm cho loại đếm).
/// Không thể xem ngày tương lai; tháng sau tháng hiện tại không có.
/// </summary>
public sealed partial class CalendarPage : Page
{
    private readonly StackPanel _root = new() { Spacing = 16, Padding = new Thickness(36, 28, 36, 32), MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Stretch };

    public CalendarPage()
    {
        Model = App.Host.Get<CalendarModel>();
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        Scroll.Content = _root;
    }

    public CalendarModel Model { get; }

    /// <summary>Hoàn tất khi đã nạp và dựng xong (smoke test chờ).</summary>
    public Task Ready { get; private set; } = Task.CompletedTask;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Ready = ReloadAsync(() => Model.LoadAsync());
    }

    private async Task ReloadAsync(Func<Task> load)
    {
        try
        {
            await load();
            Build();
        }
        catch (Exception ex)
        {
            App.Host.Get<IAppLogger>().Error("Mở trang Lịch lỗi", ex);
        }
    }

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    private static Style Sty(string key) => (Style)Application.Current.Resources[key];

    private static Color ColorOf(string key) => ((SolidColorBrush)Application.Current.Resources[key]).Color;

    private void Build()
    {
        _root.Children.Clear();
        var grid = Model.Grid!;

        _root.Children.Add(BuildHeader(grid));

        var body = new Grid { ColumnSpacing = 16 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 300 });
        body.Children.Add(BuildMonth(grid));
        var panel = BuildDayPanel();
        Grid.SetColumn(panel, 1);
        body.Children.Add(panel);
        _root.Children.Add(body);
    }

    private FrameworkElement BuildHeader(MonthGrid grid)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titles = new StackPanel { Spacing = 2 };
        titles.Children.Add(new TextBlock { Text = Ui.T("calendar.title"), Style = Sty("TitleDisplayTextStyle") });
        var culture = Ui.CurrentCulture;
        var monthName = grid.Month.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", culture);
        titles.Children.Add(new TextBlock { Text = char.ToUpper(monthName[0], culture) + monthName[1..], Style = Sty("CardTitleTextStyle") });
        row.Children.Add(titles);

        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var prev = NavButton("", "calendar.prev", true, async () => await ReloadAsync(() => Model.ShiftMonthAsync(-1)));
        var today = new Button { Style = Sty("GhostButtonStyle"), Content = Ui.T("calendar.thisMonth") };
        today.Click += async (_, _) => await ReloadAsync(() => Model.LoadAsync(Model.Today, Model.Today));
        var next = NavButton("", "calendar.next", Model.CanGoNext, async () => await ReloadAsync(() => Model.ShiftMonthAsync(1)));
        nav.Children.Add(prev);
        nav.Children.Add(today);
        nav.Children.Add(next);
        Grid.SetColumn(nav, 1);
        row.Children.Add(nav);
        return row;
    }

    private static Button NavButton(string glyph, string nameKey, bool enabled, Func<Task> click)
    {
        var button = new Button
        {
            Style = Sty("GhostButtonStyle"),
            Content = new FontIcon { Glyph = glyph, FontSize = 14 },
            IsEnabled = enabled
        };
        AutomationProperties.SetName(button, Ui.T(nameKey));
        button.Click += async (_, _) => await click();
        return button;
    }

    private FrameworkElement BuildMonth(MonthGrid grid)
    {
        var culture = Ui.CurrentCulture;
        var card = new Border
        {
            CornerRadius = (CornerRadius)Application.Current.Resources["RadiusLg"],
            Background = Res("CardIdleBrush"),
            BorderBrush = Res("StrokeSubtleBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16)
        };

        var stack = new StackPanel { Spacing = 10 };

        // Tổng kết tháng.
        var summary = new TextBlock
        {
            Style = Sty("BodySecondaryTextStyle"),
            TextWrapping = TextWrapping.Wrap,
            Text = string.Join("  ·  ", Ui.Loc.Format("calendar.perfect", grid.PerfectDays), Ui.Loc.Format("calendar.active", grid.ActiveDays), Ui.Loc.Format("calendar.average", (int)Math.Round(grid.AverageFraction * 100)))
        };
        stack.Children.Add(summary);

        var cells = new Grid { RowSpacing = 6, ColumnSpacing = 6 };

        for (var c = 0; c < 7; c++)
        {
            cells.ColumnDefinitions.Add(new ColumnDefinition());
        }

        cells.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var c = 0; c < 7; c++)
        {
            var day = grid.Weeks[0][c].Day;
            var name = new TextBlock
            {
                Text = culture.DateTimeFormat.GetAbbreviatedDayName(day.DayOfWeek),
                Style = Sty("BodySecondaryTextStyle"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(name, c);
            cells.Children.Add(name);
        }

        for (var w = 0; w < grid.Weeks.Count; w++)
        {
            cells.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });

            for (var c = 0; c < 7; c++)
            {
                var cell = BuildCell(grid.Weeks[w][c]);
                Grid.SetRow(cell, w + 1);
                Grid.SetColumn(cell, c);
                cells.Children.Add(cell);
            }
        }

        stack.Children.Add(cells);
        card.Child = stack;
        return card;
    }

    private FrameworkElement BuildCell(CalendarCell cell)
    {
        var accent = ColorOf("AccentBrandBrush");
        var success = ColorOf("SuccessBrush");
        var selected = cell.Day == Model.SelectedDay;

        Brush fill;

        if (cell.Due == 0 || cell.IsFuture)
        {
            fill = Res("HeatEmptyBrush");
        }
        else if (cell.IsComplete)
        {
            fill = new SolidColorBrush(Color.FromArgb(0xCC, success.R, success.G, success.B));
        }
        else
        {
            var alpha = (byte)(0x24 + (cell.Fraction * 0x80));
            fill = new SolidColorBrush(Color.FromArgb(alpha, accent.R, accent.G, accent.B));
        }

        var border = new Border
        {
            CornerRadius = (CornerRadius)Application.Current.Resources["RadiusMd"],
            Background = fill,
            BorderThickness = new Thickness(selected ? 2 : cell.IsToday ? 1.5 : 0),
            BorderBrush = selected ? Res("TextPrimaryBrush") : Res("AccentBrandBrush"),
            Opacity = cell.InMonth ? (cell.IsFuture ? 0.45 : 1) : 0.28,
            IsTapEnabled = !cell.IsFuture
        };

        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 2 };
        content.Children.Add(new TextBlock
        {
            Text = cell.Day.Day.ToString(Ui.CurrentCulture),
            FontWeight = cell.IsToday || selected ? FontWeights.Bold : FontWeights.SemiBold,
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = cell.IsComplete ? Res("OnGradientBrush") : Res("TextPrimaryBrush")
        });

        if (cell.Due > 0 && !cell.IsFuture)
        {
            content.Children.Add(new TextBlock
            {
                Text = $"{cell.Done}/{cell.Due}",
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = cell.IsComplete ? Res("OnGradientBrush") : Res("TextSecondaryBrush")
            });
        }

        border.Child = content;
        AutomationProperties.SetName(border, $"{Ui.LongDate(cell.Day)}, {cell.Done}/{cell.Due}");

        if (!cell.IsFuture)
        {
            border.PointerEntered += (_, _) => border.Scale = new System.Numerics.Vector3(1.04f, 1.04f, 1);
            border.PointerExited += (_, _) => border.Scale = System.Numerics.Vector3.One;
            border.Tapped += async (_, _) => await ReloadAsync(() => Model.SelectDayAsync(cell.Day));
        }

        return border;
    }

    private FrameworkElement BuildDayPanel()
    {
        var card = new Border
        {
            CornerRadius = (CornerRadius)Application.Current.Resources["RadiusLg"],
            Background = Res("CardIdleBrush"),
            BorderBrush = Res("StrokeSubtleBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
            VerticalAlignment = VerticalAlignment.Top
        };

        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(new TextBlock { Text = Ui.LongDate(Model.SelectedDay), Style = Sty("CardTitleTextStyle"), TextWrapping = TextWrapping.Wrap });

        var items = Model.DayItems;
        var due = items.Count(i => !i.IsSkipped);
        var done = items.Count(i => i.IsDone);
        stack.Children.Add(new TextBlock { Text = due == 0 ? Ui.T("calendar.empty") : Ui.Loc.Format("calendar.dayProgress", done, due), Style = Sty("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });

        foreach (var item in items)
        {
            stack.Children.Add(BuildItemRow(item));
        }

        card.Child = stack;
        return card;
    }

    private FrameworkElement BuildItemRow(QuickItem item)
    {
        var row = new Grid { ColumnSpacing = 10, MinHeight = 40 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var check = new Button
        {
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(16),
            VerticalAlignment = VerticalAlignment.Center,
            Background = item.IsDone ? Res("SuccessBrush") : new SolidColorBrush(Colors.Transparent),
            BorderBrush = item.IsDone ? Res("SuccessBrush") : Res("TextMutedBrush"),
            BorderThickness = new Thickness(1.5),
            Content = item.IsDone ? new FontIcon { Glyph = "", FontSize = 14, Foreground = Res("OnGradientBrush") } : null,
            IsEnabled = !item.IsSkipped
        };
        AutomationProperties.SetName(check, item.Name);
        check.Click += async (_, _) => await ReloadAsync(() => Model.ToggleAsync(item));
        row.Children.Add(check);

        var name = new TextBlock
        {
            Text = item.Name,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = item.IsDone || item.IsSkipped ? Res("TextSecondaryBrush") : Res("TextPrimaryBrush")
        };

        if (item.IsDone)
        {
            name.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
        }

        Grid.SetColumn(name, 1);
        row.Children.Add(name);

        if (item.IsNumeric && !item.IsSkipped)
        {
            var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            controls.Children.Add(StepButton("", item, -1));
            controls.Children.Add(new TextBlock { Text = $"{item.Value}/{item.Target}", VerticalAlignment = VerticalAlignment.Center, Style = Sty("BodySecondaryTextStyle") });
            controls.Children.Add(StepButton("", item, 1));
            Grid.SetColumn(controls, 2);
            row.Children.Add(controls);
        }

        return row;
    }

    private Button StepButton(string glyph, QuickItem item, int delta)
    {
        var button = new Button { Style = Sty("GhostButtonStyle"), Padding = new Thickness(8, 6, 8, 6), Content = new FontIcon { Glyph = glyph, FontSize = 12 } };
        AutomationProperties.SetName(button, (delta > 0 ? "+" : "−") + " " + item.Name);
        button.Click += async (_, _) => await ReloadAsync(() => Model.AdjustAsync(item, delta));
        return button;
    }
}
