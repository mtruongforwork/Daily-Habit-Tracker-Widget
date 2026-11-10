using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Challenges;
using HabitGrid.Core.Domain.Gamification;
using HabitGrid.Platform.Desktop;
using HabitGrid.Presentation.Services;
using HabitGrid.Presentation.ViewModels;

namespace HabitGrid.WidgetHost;

internal static class StackExtensions
{
    /// <summary>WPF StackPanel không có Spacing: thêm phần tử kèm khoảng cách phía trên (trừ phần tử đầu).</summary>
    public static void Put(this StackPanel panel, UIElement element, double gap = 8)
    {
        if (panel.Children.Count > 0 && element is FrameworkElement fe)
        {
            fe.Margin = new Thickness(fe.Margin.Left, gap, fe.Margin.Right, fe.Margin.Bottom);
        }

        panel.Children.Add(element);
    }
}

/// <summary>
/// Nền chung của các widget Plus: cửa sổ không viền, nền tối bo góc (chữ trắng đọc được trên mọi hình nền), kéo được ở chế độ sắp xếp
/// (hoặc giữ Alt), nhớ vị trí qua <see cref="Moved"/>, ghim theo kiểu người dùng chọn.
/// <para>
/// Mỗi widget có hai dạng: <b>nhỏ gọn</b> (chiếm một góc nhỏ, chỉ số liệu quan trọng, tự đổi theo ngữ cảnh) và <b>mở rộng</b> (đầy đủ).
/// Rê chuột vào một lúc thì mở rộng, rời đi thì thu lại; bấm vào để giữ mở (bấm lần nữa để thu).
/// </para>
/// </summary>
public abstract class ExtraWidgetWindow : Window
{
    private static readonly Brush CardBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xE6, 0x1B, 0x15, 0x30)));
    private static readonly Brush RimBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)));
    private readonly DispatcherTimer _expandTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly DispatcherTimer _collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private bool _pinnedOpen;
    private bool _dragged;

    protected ExtraWidgetWindow(string title)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Title = title;

        MouseLeftButtonDown += (_, _) =>
        {
            _dragged = false;

            if (!EditMode && !Keyboard.IsKeyDown(Key.LeftAlt) && !Keyboard.IsKeyDown(Key.RightAlt))
            {
                return;
            }

            try
            {
                DragMove();
                _dragged = true;
                Moved?.Invoke(this, EventArgs.Empty);
            }
            catch (InvalidOperationException)
            {
                // Nhả chuột trước khi kéo.
            }
        };

        // Bấm = giữ mở / thu lại (không tính khi vừa kéo xong).
        MouseLeftButtonUp += (_, _) =>
        {
            if (_dragged || EditMode)
            {
                return;
            }

            _pinnedOpen = !_pinnedOpen;
            SetExpanded(_pinnedOpen);
        };

        MouseEnter += (_, _) =>
        {
            _collapseTimer.Stop();

            if (!IsExpanded)
            {
                _expandTimer.Start();
            }
        };
        MouseLeave += (_, _) =>
        {
            _expandTimer.Stop();

            if (IsExpanded && !_pinnedOpen)
            {
                _collapseTimer.Start();
            }
        };
        _expandTimer.Tick += (_, _) =>
        {
            _expandTimer.Stop();
            SetExpanded(true);
        };
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();

            if (!_pinnedOpen)
            {
                SetExpanded(false);
            }
        };
    }

    public bool EditMode { get; set; }

    public bool IsExpanded { get; private set; }

    public event EventHandler? Moved;

    protected abstract Size CompactSize { get; }

    protected abstract Size ExpandedSize { get; }

    protected abstract UIElement BuildCompact();

    protected abstract UIElement BuildExpanded();

    /// <summary>Kích thước dạng nhỏ gọn (dùng để xếp vị trí mặc định).</summary>
    public Size NominalSize => CompactSize;

    /// <summary>Gọi sau khi dữ liệu đổi: dựng lại nội dung theo dạng hiện tại.</summary>
    protected void Render()
    {
        if (double.IsNaN(Width) || Width == 0)
        {
            Width = CompactSize.Width;
            Height = CompactSize.Height;
        }

        Content = new Border
        {
            CornerRadius = new CornerRadius(IsExpanded ? 22 : 18),
            Background = CardBrush,
            BorderBrush = RimBrush,
            BorderThickness = new Thickness(1),
            Padding = IsExpanded ? new Thickness(16, 14, 16, 14) : new Thickness(8),
            Child = IsExpanded ? BuildExpanded() : BuildCompact()
        };
    }

    /// <summary>Mở rộng/thu gọn kèm hoạt ảnh; giữ nguyên mép gần cạnh màn hình hơn để widget không nhảy khỏi chỗ.</summary>
    public void SetExpanded(bool expanded, bool animate = true)
    {
        if (IsExpanded == expanded)
        {
            return;
        }

        IsExpanded = expanded;
        var target = expanded ? ExpandedSize : CompactSize;
        var work = SystemParameters.WorkArea;
        var anchorRight = Left + (Width / 2) > work.Left + (work.Width / 2);
        var newLeft = anchorRight ? Left + Width - target.Width : Left;
        var newTop = Math.Min(Top, Math.Max(work.Top, work.Bottom - target.Height - 8));
        Render();

        if (!animate)
        {
            Width = target.Width;
            Height = target.Height;
            Left = newLeft;
            Top = newTop;
            return;
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(180));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(WidthProperty, new DoubleAnimation(target.Width, duration) { EasingFunction = ease });
        BeginAnimation(HeightProperty, new DoubleAnimation(target.Height, duration) { EasingFunction = ease });
        BeginAnimation(LeftProperty, new DoubleAnimation(newLeft, duration) { EasingFunction = ease });
        BeginAnimation(TopProperty, new DoubleAnimation(newTop, duration) { EasingFunction = ease });
    }

    public DesktopHostResult Pin(DesktopHostService host, DesktopPinningMode mode) =>
        host.Attach(new WindowInteropHelper(this).EnsureHandle(), mode);

    protected static TextBlock Text(string text, double size, bool bold = false, double opacity = 1, bool wrap = false) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = Brushes.White,
        Opacity = opacity,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis
    };

    /// <summary>Biểu tượng thói quen để hiện trong widget: emoji thì bỏ tiền tố <c>emoji:</c>; biểu tượng Fluent (không có chữ) thì dùng dấu chấm.</summary>
    protected static string IconText(string icon) =>
        icon.StartsWith(HabitIcon.EmojiPrefix, StringComparison.Ordinal) ? icon[HabitIcon.EmojiPrefix.Length..] : "•";

    /// <summary>WPF chỉ vẽ emoji đơn sắc: ép màu trắng để thấy rõ trên nền tối thay vì mặc định đen.</summary>
    protected static TextBlock Emoji(string emoji, double size) => new() { Text = emoji, FontSize = size, Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI Emoji") };

    protected static Border Bar(double fraction, double width, Color from, Color to, double height = 7)
    {
        var track = new Border { Height = height, Width = width, CornerRadius = new CornerRadius(height / 2), Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Left };
        track.Child = new Border
        {
            CornerRadius = new CornerRadius(height / 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = Math.Max(0, Math.Min(1, fraction)) * width,
            Background = new LinearGradientBrush(from, to, 0)
        };
        return track;
    }

    /// <summary>Vòng tiến độ tròn (0..1) quanh một phần tử ở giữa.</summary>
    protected static Grid Ring(double fraction, double size, double thickness, Color color, UIElement center)
    {
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), StrokeThickness = thickness });

        var f = Math.Max(0, Math.Min(0.9999, fraction));

        if (f > 0)
        {
            var radius = (size - thickness) / 2;
            var middle = size / 2;
            var angle = f * 2 * Math.PI;
            var end = new Point(middle + radius * Math.Sin(angle), middle - radius * Math.Cos(angle));
            var figure = new PathFigure { StartPoint = new Point(middle, middle - radius) };
            figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, f > 0.5, SweepDirection.Clockwise, true));
            grid.Children.Add(new Path
            {
                Stroke = new SolidColorBrush(color),
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Data = new PathGeometry([figure])
            });
        }

        if (center is FrameworkElement fe)
        {
            fe.HorizontalAlignment = HorizontalAlignment.Center;
            fe.VerticalAlignment = VerticalAlignment.Center;
        }

        grid.Children.Add(center);
        return grid;
    }

    protected static Color Hex(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            return Color.FromRgb(139, 92, 246);
        }
    }

    private static Brush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// Widget Buddy. Nhỏ gọn: vòng tiến độ hôm nay quanh Buddy. Vòng đổi màu theo ngữ cảnh: tím bình thường, vàng và nhấp nháy khi tối mà còn việc chưa xong
/// ("sắp mất chuỗi"), xanh khi xong hết. Mở rộng: tên, cấp độ, tiến độ và token đóng băng.
/// </summary>
public sealed class BuddyWidgetWindow : ExtraWidgetWindow
{
    private BuddyWidgetData? _data;

    public BuddyWidgetWindow() : base("HabitGrid Buddy")
    {
    }

    /// <summary>Giờ hiện tại (đặt được khi kiểm tra để cố định ngữ cảnh).</summary>
    public Func<DateTime> Now { get; set; } = () => DateTime.Now;

    protected override Size CompactSize => new(92, 92);

    protected override Size ExpandedSize => new(230, 150);

    /// <summary>Ngữ cảnh hiển thị của Buddy.</summary>
    public enum Mood
    {
        Normal,
        AtRisk,
        AllDone
    }

    public static Mood MoodFor(BuddyWidgetData data, DateTime now) =>
        data.TodayDue > 0 && data.TodayDone >= data.TodayDue ? Mood.AllDone
        : data.TodayDue > 0 && now.Hour >= 18 && data.TodayDone < data.TodayDue ? Mood.AtRisk
        : Mood.Normal;

    public void Update(BuddyWidgetData data)
    {
        _data = data;
        Render();
    }

    protected override UIElement BuildCompact()
    {
        var data = _data!;
        var mood = MoodFor(data, Now());
        var color = mood switch
        {
            Mood.AllDone => Color.FromRgb(52, 211, 153),
            Mood.AtRisk => Color.FromRgb(251, 191, 36),
            _ => Color.FromRgb(167, 139, 250)
        };

        var fraction = data.TodayDue == 0 ? 0 : data.TodayFraction;
        var ring = Ring(mood == Mood.AllDone ? 1 : fraction, 76, 5, color, BuildAvatar(data, 0.62));

        if (mood == Mood.AtRisk)
        {
            ring.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.55, new Duration(TimeSpan.FromMilliseconds(900))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        }

        var layout = new Grid();
        layout.Children.Add(ring);

        if (mood == Mood.AllDone)
        {
            var badge = Emoji("✔", 14);
            badge.Foreground = new SolidColorBrush(color);
            badge.HorizontalAlignment = HorizontalAlignment.Right;
            badge.VerticalAlignment = VerticalAlignment.Top;
            layout.Children.Add(badge);
        }

        return layout;
    }

    protected override UIElement BuildExpanded()
    {
        var data = _data!;
        var avatar = BuildAvatar(data, 1);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        info.Children.Add(Text(data.Name, 18, bold: true));
        info.Children.Add(Text(HostText.BuddyLevel(data.Level), 12, opacity: 0.8));
        info.Children.Add(new Border { Height = 8 });
        info.Children.Add(Text(data.TodayDue == 0 ? HostText.NothingDue : $"{HostText.Today}  {HostText.Progress(data.TodayDone, data.TodayDue)}", 12, opacity: 0.9));
        info.Children.Add(new Border { Height = 4 });
        info.Children.Add(Bar(data.TodayFraction, 110, Color.FromRgb(139, 92, 246), Color.FromRgb(236, 72, 153)));

        if (data.FreezeTokens > 0)
        {
            info.Children.Add(new Border { Height = 6 });
            info.Children.Add(Text($"❄ {data.FreezeTokens}", 12, opacity: 0.8));
        }

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(avatar);
        Grid.SetColumn(info, 1);
        row.Children.Add(info);
        return row;
    }

    private static Grid BuildAvatar(BuddyWidgetData data, double scale)
    {
        var avatar = new Grid { Width = 84 * scale, Height = 96 * scale };
        var bodySprite = BuddySprites.TryLoad(BuddySprites.StageId(data.Stage));
        FrameworkElement body = bodySprite is not null
            ? new System.Windows.Controls.Image { Source = bodySprite, Width = 72 * scale, Height = 72 * scale }
            : Emoji(StageEmoji(data.Stage), 58 * scale);
        body.HorizontalAlignment = HorizontalAlignment.Center;
        body.VerticalAlignment = VerticalAlignment.Center;
        avatar.Children.Add(body);

        foreach (var (slot, id, emoji) in data.Worn)
        {
            var sprite = BuddySprites.TryLoad(id);
            var size = (slot == AccessorySlot.Head ? 40 : 30) * scale;
            FrameworkElement piece = sprite is not null
                ? new System.Windows.Controls.Image { Source = sprite, Width = size, Height = size }
                : Emoji(emoji, (slot == AccessorySlot.Head ? 34 : 26) * scale);
            piece.HorizontalAlignment = HorizontalAlignment.Center;
            piece.VerticalAlignment = slot switch { AccessorySlot.Head => VerticalAlignment.Top, AccessorySlot.Face => VerticalAlignment.Center, _ => VerticalAlignment.Bottom };
            piece.Margin = slot switch
            {
                AccessorySlot.Head => new Thickness(0, -10 * scale, 0, 0),
                AccessorySlot.Face => new Thickness(0, 8 * scale, 0, 0),
                _ => new Thickness(0, 0, 0, -6 * scale)
            };
            avatar.Children.Add(piece);
        }

        return avatar;
    }

    private static string StageEmoji(BuddyStage stage) => stage switch
    {
        BuddyStage.Egg => "🥚",
        BuddyStage.Baby => "🐣",
        BuddyStage.Teen => "🐥",
        BuddyStage.Adult => "🦉",
        _ => "🐉"
    };
}

/// <summary>
/// Widget Mục tiêu. Nhỏ gọn: một dòng, tự luân phiên giữa ý định tuần và từng thử thách mỗi 5 giây. Mở rộng: toàn bộ ý định và tối đa 3 thử thách.
/// </summary>
public sealed class GoalsWidgetWindow : ExtraWidgetWindow
{
    private readonly DispatcherTimer _rotate = new() { Interval = TimeSpan.FromSeconds(5) };
    private GoalsWidgetData? _data;
    private int _index;

    public GoalsWidgetWindow() : base("HabitGrid Goals")
    {
        _rotate.Tick += (_, _) =>
        {
            _index++;

            if (!IsExpanded && _data is not null && Rotation(_data).Count > 1)
            {
                Render();
            }
        };
        _rotate.Start();
        Closed += (_, _) => _rotate.Stop();
    }

    protected override Size CompactSize => new(190, 52);

    protected override Size ExpandedSize => new(270, 190);

    public void Update(GoalsWidgetData data)
    {
        _data = data;
        Render();
    }

    /// <summary>Các mục luân phiên ở dạng nhỏ gọn: (nhãn, tiến độ 0..1, mô tả ngắn).</summary>
    public static IReadOnlyList<(string Label, double Fraction, string Detail)> Rotation(GoalsWidgetData data)
    {
        var items = new List<(string, double, string)>();

        if (data.Intent is { } intent)
        {
            items.Add(($"🎯 {intent.Name}", intent.Fraction, HostText.Progress(intent.Done, intent.Due)));
        }

        foreach (var c in data.Challenges)
        {
            items.Add(($"🏁 {c.Challenge.Title}", c.Fraction, HostText.GoalsDay(c.DayNumber, c.Challenge.Days)));
        }

        return items;
    }

    protected override UIElement BuildCompact()
    {
        var items = Rotation(_data!);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        if (items.Count == 0)
        {
            stack.Children.Add(Text("🎯 " + HostText.GoalsTitle, 12, bold: true));
            stack.Children.Add(Text(HostText.GoalsNone, 11, opacity: 0.75));
            return stack;
        }

        var (label, fraction, detail) = items[_index % items.Count];
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Text(label, 12, bold: true));
        var right = Text(detail, 11, opacity: 0.85);
        right.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(right, 1);
        head.Children.Add(right);
        stack.Children.Add(head);
        stack.Put(Bar(fraction, 168, Color.FromRgb(139, 92, 246), Color.FromRgb(236, 72, 153), 5), 6);
        return stack;
    }

    protected override UIElement BuildExpanded()
    {
        var data = _data!;
        var stack = new StackPanel();
        stack.Put(Text("🎯 " + HostText.GoalsTitle, 14, bold: true));

        if (data.IsEmpty)
        {
            stack.Put(Text(HostText.GoalsEmpty, 12, opacity: 0.8, wrap: true));
        }

        if (data.Intent is { } intent)
        {
            stack.Put(Text($"{HostText.GoalsIntention}: {IconText(intent.Icon)} {intent.Name}", 12));
            stack.Put(Row(intent.Fraction, HostText.Progress(intent.Done, intent.Due), Hex(intent.ColorHue)));
        }

        foreach (var challenge in data.Challenges)
        {
            var emoji = ChallengeCatalog.Find(challenge.Challenge.TemplateId)?.Emoji ?? "🏁";
            stack.Put(Text($"{emoji} {challenge.Challenge.Title}", 12));
            stack.Put(Row(challenge.Fraction, HostText.GoalsDay(challenge.DayNumber, challenge.Challenge.Days), Color.FromRgb(139, 92, 246)));
        }

        return stack;
    }

    private static UIElement Row(double fraction, string label, Color accent)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Bar(fraction, 170, accent, Color.FromRgb(236, 72, 153)));
        var text = Text(label, 11, opacity: 0.85);
        text.Margin = new Thickness(8, 0, 0, 0);
        row.Children.Add(text);
        return row;
    }
}

/// <summary>
/// Widget Multi Grid. Nhỏ gọn: mỗi thói quen một dải 13 ngày gần nhất. Mở rộng: heatmap 13 tuần kèm tên và chuỗi.
/// </summary>
public sealed class MultiGridWidgetWindow : ExtraWidgetWindow
{
    private const double Cell = 8;
    private const double Gap = 2;
    private MultiGridData? _data;

    public MultiGridWidgetWindow() : base("HabitGrid Multi Grid")
    {
    }

    protected override Size CompactSize => new(146, 22 + Math.Max(1, _data?.Rows.Count ?? 1) * 12);

    protected override Size ExpandedSize
    {
        get
        {
            var count = Math.Max(1, _data?.Rows.Count ?? 1);
            return new Size(300, 34 + count * 70 + (count - 1) * 8);
        }
    }

    public void Update(MultiGridData data)
    {
        _data = data;
        var size = IsExpanded ? ExpandedSize : CompactSize;
        Width = size.Width;
        Height = size.Height;
        Render();
    }

    /// <summary>13 ngày gần nhất tính đến hôm nay (bỏ các ô tương lai ở cuối lưới) của một hàng; dùng cho dạng nhỏ gọn.</summary>
    public static IReadOnlyList<int> LastDays(MultiGridRow row, int days = 13)
    {
        var today = -1;

        for (var i = row.Cells.Count - 1; i >= 0; i--)
        {
            if (row.Cells[i] != -1)
            {
                today = i;
                break;
            }
        }

        if (today < 0)
        {
            return [];
        }

        // Ô tương lai (-1) nằm sau hôm nay; ô trước ngày bắt đầu theo dõi cũng là -1 nhưng nằm trước, nên giữ nguyên.
        var take = Math.Min(days, today + 1);
        return row.Cells.Skip(today + 1 - take).Take(take).ToList();
    }

    protected override UIElement BuildCompact()
    {
        var data = _data!;
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        if (data.Rows.Count == 0)
        {
            stack.Children.Add(Text(HostText.NothingDue, 11, opacity: 0.8));
            return stack;
        }

        foreach (var row in data.Rows)
        {
            var strip = new Canvas { Width = 13 * (Cell + Gap), Height = Cell + 2 };
            var days = LastDays(row);

            for (var i = 0; i < days.Count; i++)
            {
                if (days[i] < 0)
                {
                    continue;
                }

                var rect = new Rectangle { Width = Cell, Height = Cell, RadiusX = 2, RadiusY = 2, Fill = Fill(row, days[i]) };
                Canvas.SetLeft(rect, i * (Cell + Gap));
                Canvas.SetTop(rect, 1);
                strip.Children.Add(rect);
            }

            stack.Put(strip, 4);
        }

        return stack;
    }

    protected override UIElement BuildExpanded()
    {
        var data = _data!;
        var stack = new StackPanel();

        if (data.Rows.Count == 0)
        {
            stack.Put(Text(HostText.NothingDue, 12, opacity: 0.8));
        }

        foreach (var row in data.Rows)
        {
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            line.ColumnDefinitions.Add(new ColumnDefinition());

            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(Text($"{IconText(row.Icon)} {row.Name}", 12, bold: true));
            titles.Children.Add(Text($"🔥 {row.Streak}", 11, opacity: 0.8));
            line.Children.Add(titles);

            var map = Heatmap(row, data.Weeks);
            Grid.SetColumn(map, 1);
            line.Children.Add(map);
            stack.Put(line, 10);
        }

        return stack;
    }

    private static Brush Fill(MultiGridRow row, int value) => value switch
    {
        1 => new SolidColorBrush(Hex(row.ColorHue)),
        2 => new SolidColorBrush(Color.FromRgb(56, 189, 248)),
        _ => new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF))
    };

    private static Canvas Heatmap(MultiGridRow row, int weeks)
    {
        var canvas = new Canvas { Width = weeks * (Cell + Gap), Height = 7 * (Cell + Gap), HorizontalAlignment = HorizontalAlignment.Right };

        for (var i = 0; i < row.Cells.Count; i++)
        {
            if (row.Cells[i] < 0)
            {
                continue;
            }

            var rect = new Rectangle { Width = Cell, Height = Cell, RadiusX = 2, RadiusY = 2, Fill = Fill(row, row.Cells[i]) };
            Canvas.SetLeft(rect, i / 7 * (Cell + Gap));
            Canvas.SetTop(rect, i % 7 * (Cell + Gap));
            canvas.Children.Add(rect);
        }

        return canvas;
    }
}
