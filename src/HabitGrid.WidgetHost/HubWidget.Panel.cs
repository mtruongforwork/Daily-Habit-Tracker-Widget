using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HabitGrid.Presentation.Services;

namespace HabitGrid.WidgetHost;

/// <summary>
/// Bảng đầy đủ của widget gộp (mở rộng): đầu thẻ Buddy + XP, ba ô số liệu, ba tab (Hôm nay / Tuần này / Nhiệm vụ),
/// danh sách việc kèm thanh tiến độ từng đoạn, ô thêm thói quen, phiên Focus và chân thẻ.
/// Mọi khối có chiều cao cố định để <see cref="EstimatePanelHeight"/> khớp với thực tế (nội dung bị cắt theo thẻ).
/// </summary>
public sealed partial class HubWidgetWindow
{
    public enum PanelTab
    {
        Today,
        Week,
        Goals
    }

    private const double HeaderHeight = 84;
    private const double TilesHeight = 58;
    private const double TabsHeight = 34;
    private const double FooterHeight = 22;
    private const double RowHeight = 44;
    private const double RowGap = 4;
    private const double AddHeight = 36;
    private const double FocusHeight = 48;
    private const double LabelHeight = 20;
    private const double SectionGap = 12;
    private const int MaxPanelRows = 6;

    private static readonly int[] AddTargets = [1, 2, 3, 5, 8];
    private static readonly int[] FocusChoices = [15, 25, 50];

    private PanelTab _tab;
    private int _addTarget = 1;
    private int _focusMinutes = 25;
    private string? _addError;
    private string _addText = string.Empty;
    private TextBox? _addBox;

    /// <summary>Thêm thói quen (tên, số lần mỗi ngày). Trả về lời báo lỗi hoặc null nếu thành công.</summary>
    public Func<string, int, Task<string?>>? OnAddHabit { get; set; }

    public Func<int, Task>? OnStartFocus { get; set; }

    public Func<Task>? OnStopFocus { get; set; }

    public Action? OnOpenApp { get; set; }

    /// <summary>Phím tắt ẩn/hiện widget để ghi ở chân thẻ.</summary>
    public Func<string?>? HotKeyText { get; set; }

    private List<PanelTab> AvailableTabs()
    {
        var tabs = new List<PanelTab> { PanelTab.Today };

        if (_data?.Grid is { Rows.Count: > 0 })
        {
            tabs.Add(PanelTab.Week);
        }

        if (_data?.Goals is { IsEmpty: false })
        {
            tabs.Add(PanelTab.Goals);
        }

        return tabs;
    }

    private PanelTab ActiveTab() => AvailableTabs().Contains(_tab) ? _tab : PanelTab.Today;

    private static int VisibleRows(HubData data) => Math.Min(data.Today.Count, MaxPanelRows);

    private double ContentHeight(PanelTab tab)
    {
        var data = _data!;

        switch (tab)
        {
            case PanelTab.Week:
                return (data.Grid?.Rows.Count ?? 0) * 28;
            case PanelTab.Goals:
                return ((data.Goals?.Intent is null ? 0 : 1) + (data.Goals?.Challenges.Count ?? 0)) * 34;
            default:
                var rows = VisibleRows(data);
                var list = rows == 0 ? 28 : rows * (RowHeight + RowGap);
                var more = data.Today.Count > MaxPanelRows ? 20 : 0;
                return LabelHeight + 6 + list + more + 8 + AddHeight + 8 + FocusHeight;
        }
    }

    /// <summary>Chiều cao bảng đầy đủ theo nội dung (đích của hoạt ảnh mở rộng).</summary>
    private double EstimatePanelHeight()
    {
        if (_data is null)
        {
            return 240;
        }

        var tabs = AvailableTabs();
        return 30 + HeaderHeight + SectionGap + TilesHeight + (tabs.Count > 1 ? SectionGap + TabsHeight : 0) + SectionGap + ContentHeight(ActiveTab()) + SectionGap + FooterHeight;
    }

    protected override UIElement BuildExpanded()
    {
        var data = _data!;
        var now = Now();
        var refocus = _addBox is { IsKeyboardFocused: true };
        var stack = new StackPanel();
        stack.Children.Add(PanelHeader(data, now));
        stack.Put(PanelTiles(data), SectionGap);

        var tabs = AvailableTabs();
        var active = ActiveTab();

        if (tabs.Count > 1)
        {
            stack.Put(PanelTabBar(tabs, active), SectionGap);
        }

        var content = active switch
        {
            PanelTab.Week => WeekTab(data),
            PanelTab.Goals => GoalsTab(data),
            _ => TodayTab(data, now, refocus)
        };
        stack.Put(content, SectionGap);
        stack.Put(PanelFooter(), SectionGap);
        return stack;
    }

    // ---------- Đầu thẻ ----------

    private UIElement PanelHeader(HubData data, DateTime now)
    {
        var buddy = data.Buddy;
        var mood = BuddyWidgetWindow.MoodFor(buddy, now);
        var header = new Grid { Height = HeaderHeight };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());

        var hop = new TranslateTransform();
        var avatar = Ring(mood == BuddyWidgetWindow.Mood.AllDone ? 1 : buddy.TodayDue == 0 ? 0 : buddy.TodayFraction, 76, 5, MoodColor(mood), BuddyWidgetWindow.BuildAvatar(buddy, 0.62));
        avatar.RenderTransform = hop;
        avatar.Cursor = Cursors.Hand;
        avatar.VerticalAlignment = VerticalAlignment.Center;
        avatar.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Hop(hop);
        };
        header.Children.Add(avatar);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        var nameRow = new Grid { Height = 26 };
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        nameRow.ColumnDefinitions.Add(new ColumnDefinition());
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = Text(buddy.Name, 18, bold: true);
        name.VerticalAlignment = VerticalAlignment.Center;
        nameRow.Children.Add(name);
        var pill = new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0xA7, 0x8B, 0xFA)),
            Padding = new Thickness(8, 1, 8, 1),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = Text(HostText.BuddyLevel(buddy.Level) + (buddy.FreezeTokens > 0 ? $"  ❄ {buddy.FreezeTokens}" : string.Empty), 11, bold: true)
        };
        Grid.SetColumn(pill, 1);
        nameRow.Children.Add(pill);

        var collapse = RoundButton("", 26, () =>
        {
            Collapse();
            return Task.CompletedTask;
        }, fontSize: 11);

        if (collapse.Child is TextBlock chevron)
        {
            chevron.FontFamily = new FontFamily("Segoe MDL2 Assets");
        }

        collapse.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(collapse, 2);
        nameRow.Children.Add(collapse);
        info.Children.Add(nameRow);

        var pending = data.Today.Count(i => !i.IsDone && !i.IsSkipped);
        var say = mood == BuddyWidgetWindow.Mood.AtRisk
            ? HostText.PanelRisk(pending)
            : buddy.TodayDue == 0 ? HostText.NothingDue : mood == BuddyWidgetWindow.Mood.AllDone ? HostText.AllDone : $"{HostText.Today}  {HostText.Progress(buddy.TodayDone, buddy.TodayDue)}";
        var sayText = Text(say, 11.5, opacity: 0.9);

        if (mood == BuddyWidgetWindow.Mood.AtRisk)
        {
            sayText.Foreground = new SolidColorBrush(Amber);
        }

        info.Children.Add(sayText);

        var bar = XpBar(buddy.XpFraction, 296);
        bar.Margin = new Thickness(0, 5, 0, 3);
        info.Children.Add(bar);

        var labels = new Grid();
        labels.ColumnDefinitions.Add(new ColumnDefinition());
        labels.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        labels.Children.Add(Text($"{buddy.XpInto} / {buddy.XpNeed} XP", 10, opacity: 0.65));
        var toNext = Text(HostText.PanelXpToNext(Math.Max(0, buddy.XpNeed - buddy.XpInto), buddy.Level + 1), 10, opacity: 0.65);
        Grid.SetColumn(toNext, 1);
        labels.Children.Add(toNext);
        info.Children.Add(labels);
        Grid.SetColumn(info, 1);
        header.Children.Add(info);

        if (_hopNext && Animations)
        {
            _hopNext = false;
            Dispatcher.BeginInvoke(() => Hop(hop), DispatcherPriority.Loaded);
        }

        return header;
    }

    // ---------- Ô số liệu ----------

    private UIElement PanelTiles(HubData data)
    {
        var buddy = data.Buddy;
        var grid = new Grid { Height = TilesHeight };

        for (var i = 0; i < 3; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        var tiles = new[]
        {
            (HostText.PanelStatToday, HostText.Progress(buddy.TodayDone, buddy.TodayDue)),
            (HostText.PanelStatBest, HostText.PanelDays(Math.Max(data.Stats?.BestStreak ?? 0, buddy.Streak))),
            (HostText.PanelStatLast7, (data.Stats?.Last7Percent ?? 0).ToString(CultureInfo.InvariantCulture) + "%")
        };

        for (var i = 0; i < tiles.Length; i++)
        {
            var tile = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, i < 2 ? 8 : 0, 0)
            };
            var inner = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            inner.Children.Add(Text(tiles[i].Item1, 10, opacity: 0.65));
            var value = Text(tiles[i].Item2, 18, bold: true);
            value.Margin = new Thickness(0, 2, 0, 0);
            inner.Children.Add(value);
            tile.Child = inner;
            Grid.SetColumn(tile, i);
            grid.Children.Add(tile);
        }

        return grid;
    }

    // ---------- Tab ----------

    private UIElement PanelTabBar(List<PanelTab> tabs, PanelTab active)
    {
        var bar = new Border
        {
            Height = TabsHeight,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            Padding = new Thickness(3)
        };
        var grid = new UniformGrid { Rows = 1 };

        foreach (var tab in tabs)
        {
            var label = Text(tab switch { PanelTab.Week => HostText.PanelTabWeek, PanelTab.Goals => HostText.PanelTabGoals, _ => HostText.Today }, 12, bold: true, opacity: tab == active ? 1 : 0.7);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            var item = new Border
            {
                CornerRadius = new CornerRadius(8),
                Cursor = Cursors.Hand,
                Background = tab == active ? new SolidColorBrush(Color.FromArgb(0x59, 0x7C, 0x5C, 0xFF)) : Brushes.Transparent,
                Child = label
            };
            var picked = tab;
            item.MouseLeftButtonDown += (_, e) => e.Handled = true;
            item.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;

                if (_tab == picked)
                {
                    return;
                }

                _tab = picked;
                _addError = null;
                Render();
                RefreshLayout();
            };
            grid.Children.Add(item);
        }

        bar.Child = grid;
        return bar;
    }

    private UIElement WeekTab(HubData data)
    {
        var stack = new StackPanel();

        foreach (var row in data.Grid!.Rows)
        {
            var line = new Grid { Height = 28 };
            line.ColumnDefinitions.Add(new ColumnDefinition());
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = Text($"{IconText(row.Icon)} {row.Name}  🔥{row.Streak}", 12);
            name.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(name);
            var strip = MultiGridWidgetWindow.Strip(row);
            strip.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(strip, 1);
            line.Children.Add(strip);
            stack.Children.Add(line);
        }

        return stack;
    }

    private UIElement GoalsTab(HubData data)
    {
        var stack = new StackPanel();

        foreach (var (label, fraction, detail) in GoalsWidgetWindow.Rotation(data.Goals!))
        {
            var block = new StackPanel { Height = 34 };
            var line = new Grid { Height = 18 };
            line.ColumnDefinitions.Add(new ColumnDefinition());
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.Children.Add(Text(label, 12));
            var d = Text(detail, 10.5, opacity: 0.8);
            Grid.SetColumn(d, 1);
            line.Children.Add(d);
            block.Children.Add(line);
            var bar = Bar(fraction, ExpandedWidth - 32 - 2, Color.FromRgb(139, 92, 246), Color.FromRgb(236, 72, 153), 4);
            bar.Margin = new Thickness(0, 4, 0, 0);
            block.Children.Add(bar);
            stack.Children.Add(block);
        }

        return stack;
    }

    // ---------- Tab Hôm nay ----------

    private UIElement TodayTab(HubData data, DateTime now, bool refocus)
    {
        var stack = new StackPanel();
        var label = Text(_addError ?? HostText.PanelTimeLeft(now.Date.AddDays(1) - now), 10.5, opacity: _addError is null ? 0.7 : 1);
        label.Height = LabelHeight;

        if (_addError is not null)
        {
            label.Foreground = new SolidColorBrush(Amber);
        }

        stack.Children.Add(label);

        var list = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };

        if (data.Today.Count == 0)
        {
            var none = Text(HostText.NothingDue, 12, opacity: 0.8);
            none.Height = 28;
            list.Children.Add(none);
        }

        foreach (var item in data.Today.Take(MaxPanelRows))
        {
            list.Children.Add(PanelRow(item, data.Stats));
        }

        stack.Children.Add(list);

        if (data.Today.Count > MaxPanelRows)
        {
            var more = Text(HostText.IslandMore(data.Today.Count - MaxPanelRows), 11, opacity: 0.6);
            more.Height = 20;
            stack.Children.Add(more);
        }

        var add = AddRow(refocus);
        add.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(add);

        var focus = FocusRow();
        focus.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(focus);
        return stack;
    }

    private UIElement PanelRow(QuickItem item, WidgetStatsData? stats)
    {
        var done = item.IsDone;
        var card = new Border
        {
            Height = RowHeight,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 0, 0, RowGap),
            Padding = new Thickness(10, 0, 10, 0)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var circle = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(done ? Green : Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
            Background = done ? new SolidColorBrush(Green) : Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand
        };

        if (done)
        {
            var tick = Text("✓", 13, bold: true);
            tick.HorizontalAlignment = HorizontalAlignment.Center;
            tick.VerticalAlignment = VerticalAlignment.Center;
            tick.TextTrimming = TextTrimming.None;
            circle.Child = tick;
        }

        grid.Children.Add(circle);

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
        var name = Text(item.Name, 13, bold: true, opacity: done || item.IsSkipped ? 0.5 : 1);

        if (done)
        {
            name.TextDecorations = TextDecorations.Strikethrough;
        }

        texts.Children.Add(name);

        if (item.IsNumeric)
        {
            var segments = SegmentBar(item.Value, item.Target, done);
            segments.Margin = new Thickness(0, 5, 0, 0);
            texts.Children.Add(segments);
        }

        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        if (stats is not null)
        {
            var streak = stats.HabitStreaks.TryGetValue(item.Id, out var days) ? days : 0;
            var flame = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Opacity = streak == 0 ? 0.5 : 1, Margin = new Thickness(0, 0, 8, 0) };
            flame.Children.Add(Emoji("🔥", 11));
            var number = Text(streak.ToString(CultureInfo.InvariantCulture), 11, bold: true);
            number.Foreground = new SolidColorBrush(streak == 0 ? Colors.White : Amber);
            number.Margin = new Thickness(3, 0, 0, 0);
            flame.Children.Add(number);
            Grid.SetColumn(flame, 2);
            grid.Children.Add(flame);
        }

        if (item.IsNumeric && !item.IsSkipped && OnAdjust is { } adjust)
        {
            var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            controls.Children.Add(RoundButton("−", 24, () => adjust(item, -1), fontSize: 13));
            var value = Text($"{item.Value}/{item.Target}", 12, bold: true, opacity: 0.9);
            value.Margin = new Thickness(8, 0, 8, 0);
            value.VerticalAlignment = VerticalAlignment.Center;
            controls.Children.Add(value);
            controls.Children.Add(RoundButton("+", 24, () => adjust(item, 1), Violet, 13));
            Grid.SetColumn(controls, 3);
            grid.Children.Add(controls);
        }

        // Vòng tròn = check (thói quen đếm: +1); thói quen thường bấm cả dòng.
        async void Toggle(object? sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            if (OnToggle is { } toggle && !item.IsSkipped)
            {
                await toggle(item);
            }
        }

        circle.MouseLeftButtonDown += (_, e) => e.Handled = true;
        circle.MouseLeftButtonUp += Toggle;

        if (!item.IsNumeric)
        {
            card.Cursor = Cursors.Hand;
            card.MouseLeftButtonDown += (_, e) => e.Handled = true;
            card.MouseLeftButtonUp += Toggle;
        }

        card.Child = grid;
        return card;
    }

    /// <summary>Thanh tiến độ chia đoạn: mỗi đoạn một lần (tối đa 10 đoạn; mục tiêu lớn hơn thì chia theo tỷ lệ).</summary>
    private static FrameworkElement SegmentBar(int value, int target, bool done)
    {
        target = Math.Max(1, target);
        var count = Math.Min(target, 10);
        var filled = target <= 10 ? Math.Min(value, target) : (int)Math.Ceiling(Math.Clamp(value, 0, target) / (double)target * count);
        var grid = new Grid { Height = 4 };

        for (var i = 0; i < count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var segment = new Border
            {
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 0, i < count - 1 ? 3 : 0, 0),
                Background = new SolidColorBrush(i < filled ? (done ? Green : Violet) : Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF))
            };
            Grid.SetColumn(segment, i);
            grid.Children.Add(segment);
        }

        return grid;
    }

    private FrameworkElement AddRow(bool refocus)
    {
        var row = new Grid { Height = AddHeight };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var box = new TextBox
        {
            Text = _addText,
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            CaretBrush = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 12,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(10, 0, 8, 0),
            MaxLength = 60
        };
        var hint = Text(HostText.PanelAddPlaceholder, 12, opacity: 0.5);
        hint.Margin = new Thickness(12, 0, 0, 0);
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.IsHitTestVisible = false;
        hint.Visibility = _addText.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        var field = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Child = new Grid { Children = { box, hint } }
        };
        row.Children.Add(field);

        _addBox = box;
        box.TextChanged += (_, _) =>
        {
            _addText = box.Text;
            hint.Visibility = _addText.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        };

        // Widget không kích hoạt để khỏi cướp focus; chỉ khi bấm vào ô nhập mới nhận bàn phím, rời ô thì trả lại.
        box.PreviewMouseLeftButtonDown += (_, _) => AllowTyping(true);
        box.GotKeyboardFocus += (_, _) => AllowTyping(true);
        box.LostKeyboardFocus += (_, _) =>
        {
            if (ReferenceEquals(_addBox, box))
            {
                AllowTyping(false);
            }
        };

        async Task Submit()
        {
            var name = _addText.Trim();

            if (name.Length == 0 || OnAddHabit is not { } add)
            {
                return;
            }

            var error = await add(name, _addTarget);
            _addError = error;

            if (error is null)
            {
                _addText = string.Empty;
            }

            Render();
            RefreshLayout();
        }

        box.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await Submit();
            }
        };

        var perDay = Chip(ChipText(HostText.PanelPerDay(_addTarget)), Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF), () =>
        {
            var next = Array.IndexOf(AddTargets, _addTarget) + 1;
            _addTarget = AddTargets[next % AddTargets.Length];
            Render();
            return Task.CompletedTask;
        });
        perDay.Height = AddHeight - 4;
        perDay.CornerRadius = new CornerRadius(10);
        Grid.SetColumn(perDay, 1);
        row.Children.Add(perDay);

        var button = Chip(ChipText(HostText.PanelAdd), Violet, Submit);
        button.Height = AddHeight - 4;
        button.CornerRadius = new CornerRadius(10);
        Grid.SetColumn(button, 2);
        row.Children.Add(button);

        if (refocus)
        {
            Dispatcher.BeginInvoke(() =>
            {
                box.Focus();
                box.CaretIndex = box.Text.Length;
            }, DispatcherPriority.Loaded);
        }

        return row;
    }

    private FrameworkElement FocusRow()
    {
        var card = new Border
        {
            Height = FocusHeight,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            Padding = new Thickness(12, 0, 10, 0)
        };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var focus = FocusProvider?.Invoke();
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(Emoji(focus is { IsBreak: true } ? "☕" : "⏱", 15));
        var text = Text(focus is null ? HostText.PanelFocusSession : $"{HostText.PanelFocusing} · {focus.Clock}", 13, bold: true);
        text.Margin = new Thickness(8, 0, 0, 0);
        text.VerticalAlignment = VerticalAlignment.Center;

        if (focus is not null)
        {
            text.Foreground = new SolidColorBrush(focus.IsBreak ? Blue : Teal);
        }

        title.Children.Add(text);
        row.Children.Add(title);

        if (focus is not null)
        {
            var stop = Chip(ChipText(HostText.PanelFocusStop), Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF), () => OnStopFocus?.Invoke() ?? Task.CompletedTask);
            Grid.SetColumn(stop, 2);
            row.Children.Add(stop);
        }
        else
        {
            var choices = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            foreach (var minutes in FocusChoices)
            {
                var picked = minutes;
                var selected = minutes == _focusMinutes;
                var choice = Chip(ChipText(minutes.ToString(CultureInfo.InvariantCulture) + "'"), selected ? Color.FromArgb(0x66, 0x7C, 0x5C, 0xFF) : Colors.Transparent, () =>
                {
                    _focusMinutes = picked;
                    Render();
                    return Task.CompletedTask;
                });
                choice.MinWidth = 30;
                choice.Margin = new Thickness(2, 0, 0, 0);
                choices.Children.Add(choice);
            }

            Grid.SetColumn(choices, 1);
            row.Children.Add(choices);

            var start = Chip(ChipText(HostText.PanelFocusStart), Violet, () => OnStartFocus?.Invoke(_focusMinutes) ?? Task.CompletedTask);
            start.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(start, 2);
            row.Children.Add(start);
        }

        card.Child = row;
        return card;
    }

    // ---------- Chân thẻ ----------

    private UIElement PanelFooter()
    {
        var footer = new Grid { Height = FooterHeight };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (HotKeyText?.Invoke() is { Length: > 0 } keys)
        {
            var hint = Text(HostText.PanelHotkey(keys.Replace("+", " + ")), 10, opacity: 0.55);
            hint.VerticalAlignment = VerticalAlignment.Center;
            footer.Children.Add(hint);
        }

        var open = Text(HostText.PanelOpenApp, 11, bold: true);
        open.Foreground = new SolidColorBrush(Color.FromRgb(167, 139, 250));
        open.VerticalAlignment = VerticalAlignment.Center;
        open.Cursor = Cursors.Hand;
        open.MouseLeftButtonDown += (_, e) => e.Handled = true;
        open.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            OnOpenApp?.Invoke();
        };
        Grid.SetColumn(open, 1);
        footer.Children.Add(open);
        return footer;
    }
}
