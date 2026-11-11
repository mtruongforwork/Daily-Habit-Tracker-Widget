using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using HabitGrid.Core.Domain.Widgets;
using HabitGrid.Presentation.Services;

namespace HabitGrid.WidgetHost;

/// <summary>
/// Widget gộp duy nhất, theo bản thiết kế "Buddy Widget". Nhỏ gọn: viên thuốc 288×64 gồm Buddy trong vòng tiến độ, tiêu đề (việc kế tiếp hoặc đồng hồ Focus),
/// dòng phụ luân phiên và một nút tròn làm ngay việc kế tiếp (check, hoặc +1 với thói quen đếm). Mở rộng: Buddy lớn, cấp độ, thanh XP, danh sách Hôm nay
/// (check, và −/+ cho thói quen đếm), Mục tiêu và lưới chuỗi (Plus).
/// </summary>
public sealed class HubWidgetWindow : ExtraWidgetWindow
{
    private const double ExpandedWidth = 384;

    private readonly DispatcherTimer _rotate = new() { Interval = TimeSpan.FromSeconds(4) };
    private HubData? _data;
    private int _index;
    private double _shownXp;
    private bool _hopNext;

    public HubWidgetWindow() : base("HabitGrid")
    {
        _rotate.Tick += (_, _) =>
        {
            _index++;

            if (!IsExpanded && _data is not null && CompactLines(_data, Now()).Count > 1)
            {
                Render();
            }
        };
        _rotate.Start();
        Closed += (_, _) => _rotate.Stop();
    }

    /// <summary>Gọi khi người dùng bấm một việc (bật/tắt, hoặc +1 với thói quen đếm).</summary>
    public Func<QuickItem, Task>? OnToggle { get; set; }

    /// <summary>Gọi khi người dùng bấm −/+ trên thói quen đếm.</summary>
    public Func<QuickItem, int, Task>? OnAdjust { get; set; }

    public Func<DateTime> Now { get; set; } = () => DateTime.Now;

    /// <summary>Các mục đang hiển thị (để kiểm tra phân gói).</summary>
    public HubSection Sections => (_data is null ? HubSection.None : HubSection.Buddy | HubSection.Today)
                                  | (_data?.Goals is not null ? HubSection.Goals : HubSection.None)
                                  | (_data?.Grid is not null ? HubSection.Grid : HubSection.None);

    private WidgetPlacementMode _placement;

    /// <summary>Vị trí/kiểu hiển thị. Giữa mép trên = "Dynamic Island": trạng thái nghỉ rất nhỏ, nở quanh tâm, không kéo được.</summary>
    public WidgetPlacementMode Placement
    {
        get => _placement;
        set
        {
            if (_placement == value)
            {
                return;
            }

            _placement = value;

            if (_data is not null)
            {
                Render();
                RefreshLayout();
            }
        }
    }

    public bool IsIsland => _placement == WidgetPlacementMode.TopCenter;

    protected override bool CenterAnchored => IsIsland;

    // Island: rê chuột vào là mở nhanh (hơi trễ để lướt qua mép trên không bật nhầm), rời đi thì thu lại sau nửa giây.
    protected override TimeSpan HoverDelay => IsIsland ? TimeSpan.FromMilliseconds(140) : base.HoverDelay;

    protected override TimeSpan CollapseDelay => IsIsland ? TimeSpan.FromMilliseconds(500) : base.CollapseDelay;

    protected override bool HasHoverLevel => IsIsland;

    /// <summary>Mức xem nhanh của Island: danh sách việc hôm nay, cao theo số việc (tối đa <see cref="MaxHoverRows"/> dòng).</summary>
    protected override Size HoverSize => new(WidgetLayout.IslandListWidth, EstimateHoverHeight(_data));

    public const int MaxHoverRows = 7;

    public static double EstimateHoverHeight(HubData? data)
    {
        var rows = data is null ? 1 : Math.Max(1, Math.Min(data.Today.Count, MaxHoverRows));
        var extra = data is not null && data.Today.Count > MaxHoverRows ? 22 : 0;
        return 26 + 30 + (rows * 33) + extra + 8;
    }

    protected override bool CanDrag => !IsIsland;

    protected override Size CompactSize =>
        IsIsland
            ? new Size(FocusProvider?.Invoke() is not null ? WidgetLayout.IslandFocusWidth : WidgetLayout.IslandRestWidth, WidgetLayout.IslandRestHeight)
            : new Size(WidgetLayout.HubWidth, WidgetLayout.HubHeight);

    protected override Size ExpandedSize => new(IsIsland ? WidgetLayout.IslandPanelWidth : ExpandedWidth, EstimateHeight(_data));

    /// <summary>Chiều cao dạng mở rộng theo nội dung (ước lượng để hoạt ảnh biết đích đến).</summary>
    public static double EstimateHeight(HubData? data)
    {
        if (data is null)
        {
            return 240;
        }

        var height = 30 + 96;
        height += 30 + Math.Max(1, Math.Min(data.Today.Count, 6)) * 34;

        if (data.Goals is { IsEmpty: false } goals)
        {
            height += 28 + ((goals.Intent is null ? 0 : 1) + goals.Challenges.Count) * 30;
        }

        if (data.Grid is { Rows.Count: > 0 } grid)
        {
            height += 28 + grid.Rows.Count * 24;
        }

        return height;
    }

    public void Update(HubData data)
    {
        var previous = _data;
        _data = data;
        _hopNext = previous is not null && data.Buddy.TodayDone > previous.Buddy.TodayDone;
        Render();
        RefreshLayout();
        _shownXp = data.Buddy.XpFraction;

        if (previous is not null)
        {
            AnnounceChanges(previous, data);
        }
    }

    /// <summary>Thông tin phiên Focus đang chạy (đồng hồ chạy ngay trên viên thuốc); null nếu không có phiên.</summary>
    public Func<FocusSnapshot?>? FocusProvider { get; set; }

    private FocusSnapshot? _lastFocus;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>
    /// Các sự kiện làm thẻ "nhô lên": check-in xong một việc, xong hết hôm nay, bắt đầu nguy cơ mất chuỗi (lần đầu), Focus bắt đầu/kết thúc.
    /// </summary>
    private void AnnounceChanges(HubData before, HubData after)
    {
        var b = before.Buddy;
        var a = after.Buddy;
        var now = Now();

        if (a.Level > b.Level)
        {
            PeekMessage("⭐", HostText.HubLevelUp(a.Level), 260);
            return;
        }

        if (a.TodayDue > 0 && a.TodayDone >= a.TodayDue && b.TodayDone < b.TodayDue)
        {
            PeekMessage("🎉", HostText.HubAllDonePeek, 240);
            return;
        }

        if (a.TodayDone > b.TodayDone)
        {
            var finished = after.Today.FirstOrDefault(i => i.IsDone && before.Today.Any(o => o.Id == i.Id && !o.IsDone));
            PeekMessage("✔", $"{finished?.Name ?? HostText.Done}  {HostText.Progress(a.TodayDone, a.TodayDue)}", 250);
            return;
        }

        if (BuddyWidgetWindow.MoodFor(a, now) == BuddyWidgetWindow.Mood.AtRisk && BuddyWidgetWindow.MoodFor(b, now) != BuddyWidgetWindow.Mood.AtRisk)
        {
            PeekMessage("⚠", HostText.HubAtRisk(after.Today.Count(i => !i.IsDone && !i.IsSkipped)), 290);
        }
    }

    private void PeekMessage(string icon, string message, double width)
    {
        var row = new Grid { VerticalAlignment = VerticalAlignment.Center };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var glyph = Emoji(icon, 20);
        glyph.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(glyph);
        var text = Text(message, 13, bold: true);
        text.Margin = new Thickness(10, 0, 0, 0);
        text.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        Peek(row, new Size(Math.Max(width, WidgetLayout.HubWidth), 52));
    }

    /// <summary>Mỗi giây: cập nhật đồng hồ Focus trên viên thuốc và nhô lên khi phiên bắt đầu/kết thúc.</summary>
    public void StartLiveActivities()
    {
        _tick.Tick += (_, _) =>
        {
            var focus = FocusProvider?.Invoke();
            var started = focus is not null && _lastFocus is null;
            var ended = focus is null && _lastFocus is not null;
            _lastFocus = focus;

            if (started)
            {
                PeekMessage("⏱", HostText.HubFocusStarted(focus!.Habit), 280);
            }
            else if (ended)
            {
                PeekMessage("🎉", HostText.HubFocusEnded, 260);
            }
            else if (focus is not null && !IsExpanded && !_peeking)
            {
                Render();
            }
        };
        _tick.Start();
        Closed += (_, _) => _tick.Stop();
    }

    /// <summary>Các dòng luân phiên ở dạng nhỏ gọn. Tối còn việc: dòng đầu là cảnh báo giữ chuỗi.</summary>
    public static IReadOnlyList<string> CompactLines(HubData data, DateTime now)
    {
        var lines = new List<string>();
        var buddy = data.Buddy;
        var mood = BuddyWidgetWindow.MoodFor(buddy, now);
        var pending = data.Today.Where(i => !i.IsDone && !i.IsSkipped).ToList();

        if (mood == BuddyWidgetWindow.Mood.AtRisk)
        {
            lines.Add(HostText.HubAtRisk(pending.Count));
        }

        lines.Add(buddy.TodayDue == 0 ? HostText.NothingDue : mood == BuddyWidgetWindow.Mood.AllDone ? HostText.AllDone : $"{HostText.Today}  {HostText.Progress(buddy.TodayDone, buddy.TodayDue)}");

        if (buddy.XpNeed > 0)
        {
            lines.Add($"{HostText.BuddyLevel(buddy.Level)} · {buddy.XpInto}/{buddy.XpNeed} XP");
        }

        if (data.Goals is { } goals)
        {
            lines.AddRange(GoalsWidgetWindow.Rotation(goals).Select(r => $"{r.Label} · {r.Detail}"));
        }

        return lines;
    }

    private static Color MoodColor(BuddyWidgetWindow.Mood mood) => mood switch
    {
        BuddyWidgetWindow.Mood.AllDone => Color.FromRgb(52, 211, 153),
        BuddyWidgetWindow.Mood.AtRisk => Color.FromRgb(251, 191, 36),
        _ => Color.FromRgb(167, 139, 250)
    };

    /// <summary>Nút tròn (viền mờ, nền nhẹ) dùng cho "làm ngay", −, +, thu gọn. Bấm không làm đổi trạng thái giữ-mở của cả widget.</summary>
    private static Border RoundButton(string glyph, double size, Func<Task> click, Color? fill = null, double fontSize = 14)
    {
        var button = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Cursor = Cursors.Hand,
            Background = new SolidColorBrush(fill ?? Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1)
        };
        var label = Text(glyph, fontSize, bold: true);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.TextAlignment = TextAlignment.Center;
        label.TextTrimming = TextTrimming.None;
        button.Child = label;

        button.MouseEnter += (_, _) => ScaleTo(button, 1.1);
        button.MouseLeave += (_, _) => ScaleTo(button, 1);
        button.MouseLeftButtonDown += (_, e) => e.Handled = true;
        button.MouseLeftButtonUp += async (_, e) =>
        {
            e.Handled = true;
            await click();
        };
        return button;
    }

    private static void ScaleTo(UIElement element, double scale)
    {
        if (element.RenderTransform is not ScaleTransform transform)
        {
            return;
        }

        if (!Animations)
        {
            transform.ScaleX = transform.ScaleY = scale;
            return;
        }

        var animation = new DoubleAnimation(scale, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    protected override UIElement BuildCompact()
    {
        if (IsIsland)
        {
            return BuildIslandRest();
        }

        var data = _data!;
        var now = Now();
        var mood = BuddyWidgetWindow.MoodFor(data.Buddy, now);
        var pending = data.Today.Where(i => !i.IsDone && !i.IsSkipped).ToList();
        var next = pending.FirstOrDefault();
        var focus = FocusProvider?.Invoke();

        var ring = Ring(mood == BuddyWidgetWindow.Mood.AllDone ? 1 : data.Buddy.TodayDue == 0 ? 0 : data.Buddy.TodayFraction, 48, 4, MoodColor(mood), BuddyWidgetWindow.BuildAvatar(data.Buddy, 0.4));

        if (mood == BuddyWidgetWindow.Mood.AtRisk)
        {
            ring.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.55, new Duration(TimeSpan.FromMilliseconds(900))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        }

        if (focus is not null)
        {
            // Live Activity: đồng hồ Focus chạy ngay trên viên thuốc, vòng tiến độ màu xanh ngọc.
            ring = Ring(focus.Fraction, 48, 4, focus.IsBreak ? Color.FromRgb(96, 165, 250) : Color.FromRgb(45, 212, 191), Emoji(focus.IsBreak ? "☕" : "⏱", 18));
        }

        var lines = CompactLines(data, now);
        var title = focus is not null
            ? focus.Display
            : next is not null ? next.Name : data.Buddy.TodayDue == 0 ? HostText.NothingDue : HostText.AllDone;
        var others = lines.Where(l => l != title).ToList();
        var subtitle = focus is not null ? focus.Habit ?? string.Empty : others.Count == 0 ? string.Empty : others[_index % others.Count];

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
        texts.Children.Add(Text(title, 14, bold: true));
        texts.Children.Add(Text(subtitle, 11, opacity: 0.7));

        var row = new Grid { VerticalAlignment = VerticalAlignment.Center };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(ring);
        Grid.SetColumn(texts, 1);
        row.Children.Add(texts);

        if (focus is null && next is not null && OnToggle is { } act)
        {
            // Nút làm ngay việc kế tiếp: check, hoặc +1 với thói quen đếm.
            var button = RoundButton(next.IsNumeric ? "+" : "✓", 36, () => act(next), Color.FromRgb(124, 92, 255), 16);
            Grid.SetColumn(button, 2);
            row.Children.Add(button);
        }

        return row;
    }

    /// <summary>
    /// Trạng thái nghỉ của "Dynamic Island": một dòng rất ngắn. Đang Focus thì chỉ hiện đồng hồ; không thì hiện chuỗi ngày và số việc còn lại hôm nay
    /// (vàng khi tối mà còn việc, xanh khi xong hết).
    /// </summary>
    private UIElement BuildIslandRest()
    {
        var data = _data!;
        var focus = FocusProvider?.Invoke();
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        if (focus is not null)
        {
            var clock = Text(focus.Clock + (focus.Paused ? " ⏸" : string.Empty), 13, bold: true);
            clock.Foreground = new SolidColorBrush(focus.IsBreak ? Color.FromRgb(96, 165, 250) : Color.FromRgb(45, 212, 191));
            clock.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(Emoji(focus.IsBreak ? "☕" : "⏱", 13));
            clock.Margin = new Thickness(6, 0, 0, 0);
            row.Children.Add(clock);
            return row;
        }

        var buddy = data.Buddy;
        var mood = BuddyWidgetWindow.MoodFor(buddy, Now());
        var remaining = Math.Max(0, buddy.TodayDue - buddy.TodayDone);

        if (buddy.Streak > 0)
        {
            var dot = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Color.FromRgb(251, 146, 60)), VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(dot);
            var streak = Text(buddy.Streak.ToString(CultureInfo.InvariantCulture), 13, bold: true);
            streak.Margin = new Thickness(5, 0, 8, 0);
            streak.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(streak);
        }

        var left = Text(buddy.TodayDue == 0 ? HostText.IslandNothing : remaining == 0 ? HostText.IslandAllDone : HostText.IslandLeft(remaining), 12, bold: true);
        left.VerticalAlignment = VerticalAlignment.Center;
        left.Foreground = new SolidColorBrush(mood switch
        {
            BuddyWidgetWindow.Mood.AllDone => Color.FromRgb(52, 211, 153),
            BuddyWidgetWindow.Mood.AtRisk => Color.FromRgb(251, 191, 36),
            _ => Colors.White
        });
        row.Children.Add(left);
        return row;
    }

    /// <summary>
    /// Mức xem nhanh của Island (rê chuột): tiêu đề "Hôm nay n/m" kèm chuỗi ngày, rồi danh sách việc hôm nay để check tại chỗ
    /// (thói quen đếm có −/+). Bấm vào vùng trống của thẻ để mở bảng đầy đủ bên dưới.
    /// </summary>
    protected override UIElement BuildHover()
    {
        var data = _data!;
        var buddy = data.Buddy;
        var stack = new StackPanel();

        var header = new Grid { Height = 24 };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Text($"{HostText.Today.ToUpperInvariant()}  {buddy.TodayDone}/{buddy.TodayDue}", 11, bold: true, opacity: 0.7);
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);

        if (buddy.Streak > 0)
        {
            var streak = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            streak.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Color.FromRgb(251, 146, 60)), VerticalAlignment = VerticalAlignment.Center });
            var days = Text(HostText.IslandStreak(buddy.Streak), 11, bold: true);
            days.Margin = new Thickness(5, 0, 0, 0);
            streak.Children.Add(days);
            Grid.SetColumn(streak, 1);
            header.Children.Add(streak);
        }

        stack.Children.Add(header);

        if (data.Today.Count == 0)
        {
            stack.Put(Text(HostText.NothingDue, 12, opacity: 0.8), 8);
            return stack;
        }

        foreach (var item in data.Today.Take(MaxHoverRows))
        {
            stack.Put(TodayRow(item), 3);
        }

        if (data.Today.Count > MaxHoverRows)
        {
            stack.Put(Text(HostText.IslandMore(data.Today.Count - MaxHoverRows), 11, opacity: 0.6), 2);
        }

        return stack;
    }

    /// <summary>Thanh XP: phần lấp đầy chạy từ giá trị đã hiện lần trước tới giá trị mới (không chạy khi tắt hoạt ảnh).</summary>
    private Border XpBar(double fraction, double width)
    {
        var track = new Border { Height = 8, Width = width, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Left };
        var fill = new Border
        {
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = fraction * width,
            Background = new LinearGradientBrush(Color.FromRgb(139, 92, 246), Color.FromRgb(236, 72, 153), 0)
        };
        track.Child = fill;

        if (Animations && Math.Abs(fraction - _shownXp) > 0.001)
        {
            fill.BeginAnimation(WidthProperty, new DoubleAnimation(_shownXp * width, fraction * width, TimeSpan.FromMilliseconds(650)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        return track;
    }

    protected override UIElement BuildExpanded()
    {
        var data = _data!;
        var now = Now();
        var buddy = data.Buddy;
        var mood = BuddyWidgetWindow.MoodFor(buddy, now);
        var stack = new StackPanel();

        // Đầu thẻ: Buddy trong vòng tiến độ, tên + cấp, thanh XP, nút thu gọn.
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        var hop = new TranslateTransform();
        var avatar = Ring(mood == BuddyWidgetWindow.Mood.AllDone ? 1 : buddy.TodayDue == 0 ? 0 : buddy.TodayFraction, 76, 5, MoodColor(mood), BuddyWidgetWindow.BuildAvatar(buddy, 0.62));
        avatar.RenderTransform = hop;
        avatar.Cursor = Cursors.Hand;
        avatar.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Hop(hop);
        };
        header.Children.Add(avatar);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        var nameRow = new Grid();
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        nameRow.ColumnDefinitions.Add(new ColumnDefinition());
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        nameRow.Children.Add(Text(buddy.Name, 18, bold: true));
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
        var collapse = RoundButton("–", 24, () =>
        {
            Collapse();
            return Task.CompletedTask;
        }, fontSize: 13);
        Grid.SetColumn(collapse, 2);
        nameRow.Children.Add(collapse);
        info.Children.Add(nameRow);

        var say = mood == BuddyWidgetWindow.Mood.AtRisk
            ? HostText.HubAtRisk(data.Today.Count(i => !i.IsDone && !i.IsSkipped))
            : buddy.TodayDue == 0 ? HostText.NothingDue : mood == BuddyWidgetWindow.Mood.AllDone ? HostText.AllDone : $"{HostText.Today}  {HostText.Progress(buddy.TodayDone, buddy.TodayDue)}";
        info.Children.Add(Text(say, 11, opacity: 0.8));
        info.Children.Add(new Border { Height = 6 });
        info.Children.Add(XpBar(buddy.XpFraction, 236));
        info.Children.Add(Text($"{buddy.XpInto}/{buddy.XpNeed} XP", 10, opacity: 0.65));
        Grid.SetColumn(info, 1);
        header.Children.Add(info);
        stack.Put(header);

        if (_hopNext && Animations)
        {
            _hopNext = false;
            Dispatcher.BeginInvoke(() => Hop(hop), DispatcherPriority.Loaded);
        }

        // Hôm nay: bấm một dòng để check / bỏ check; thói quen đếm có −/+.
        stack.Put(Text($"{HostText.Today.ToUpperInvariant()}  {buddy.TodayDone}/{buddy.TodayDue}", 10, bold: true, opacity: 0.6), 12);

        if (data.Today.Count == 0)
        {
            stack.Put(Text(HostText.NothingDue, 12, opacity: 0.8), 4);
        }

        foreach (var item in data.Today.Take(6))
        {
            stack.Put(TodayRow(item), 3);
        }

        if (data.Today.Count > 6)
        {
            stack.Put(Text("+" + (data.Today.Count - 6), 11, opacity: 0.6), 2);
        }

        // Mục tiêu (Plus).
        if (data.Goals is { IsEmpty: false } goals)
        {
            stack.Put(Text(HostText.GoalsTitle.ToUpperInvariant(), 10, bold: true, opacity: 0.6), 12);

            foreach (var (label, fraction, detail) in GoalsWidgetWindow.Rotation(goals))
            {
                var line = new Grid();
                line.ColumnDefinitions.Add(new ColumnDefinition());
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                line.Children.Add(Text(label, 11));
                var d = Text(detail, 10, opacity: 0.8);
                Grid.SetColumn(d, 1);
                line.Children.Add(d);
                stack.Put(line, 4);
                stack.Put(Bar(fraction, ExpandedWidth - 32, Color.FromRgb(139, 92, 246), Color.FromRgb(236, 72, 153), 4), 2);
            }
        }

        // Chuỗi (Plus).
        if (data.Grid is { Rows.Count: > 0 } grid)
        {
            stack.Put(Text("🔥", 10, bold: true, opacity: 0.6), 12);

            foreach (var row in grid.Rows)
            {
                var line = new Grid();
                line.ColumnDefinitions.Add(new ColumnDefinition());
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                line.Children.Add(Text($"{IconText(row.Icon)} {row.Name}  🔥{row.Streak}", 11));
                var strip = MultiGridWidgetWindow.Strip(row);
                Grid.SetColumn(strip, 1);
                line.Children.Add(strip);
                stack.Put(line, 4);
            }
        }

        return stack;
    }

    /// <summary>Buddy nảy lên rồi rơi xuống (bấm vào, hoặc sau khi check xong một việc).</summary>
    private static void Hop(TranslateTransform transform)
    {
        if (!Animations)
        {
            return;
        }

        var animation = new DoubleAnimation(0, -12, TimeSpan.FromMilliseconds(170))
        {
            AutoReverse = true,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        transform.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    private UIElement TodayRow(QuickItem item)
    {
        var done = item.IsDone;
        var green = Color.FromRgb(52, 211, 153);
        var box = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(done ? green : Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
            Background = done ? new SolidColorBrush(green) : Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (done)
        {
            var tick = Text("✓", 12, bold: true);
            tick.HorizontalAlignment = HorizontalAlignment.Center;
            tick.VerticalAlignment = VerticalAlignment.Center;
            tick.TextTrimming = TextTrimming.None;
            box.Child = tick;
        }

        var label = Text(item.Name, 13, opacity: done || item.IsSkipped ? 0.5 : 1);

        if (done)
        {
            label.TextDecorations = TextDecorations.Strikethrough;
        }

        var row = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, Height = 30 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(box);
        label.Margin = new Thickness(10, 0, 0, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(label, 1);
        row.Children.Add(label);

        if (item.IsNumeric && !item.IsSkipped && OnAdjust is { } adjust)
        {
            // Thói quen đếm: −/+ và giá trị hiện tại trên mục tiêu.
            var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            controls.Children.Add(RoundButton("−", 22, () => adjust(item, -1), fontSize: 13));
            var value = Text($"{item.Value}/{item.Target}", 11, bold: true, opacity: 0.85);
            value.Margin = new Thickness(8, 0, 8, 0);
            value.VerticalAlignment = VerticalAlignment.Center;
            controls.Children.Add(value);
            controls.Children.Add(RoundButton("+", 22, () => adjust(item, 1), fontSize: 13));
            Grid.SetColumn(controls, 2);
            row.Children.Add(controls);
        }
        else
        {
            // Bấm dòng = check/bỏ check; chặn sự kiện để không đổi trạng thái giữ-mở của cả widget.
            row.MouseLeftButtonUp += async (_, e) =>
            {
                e.Handled = true;

                if (OnToggle is { } toggle && !item.IsSkipped)
                {
                    await toggle(item);
                }
            };
        }

        return row;
    }
}
