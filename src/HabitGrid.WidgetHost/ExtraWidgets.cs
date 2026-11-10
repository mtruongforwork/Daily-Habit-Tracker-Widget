using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Challenges;
using HabitGrid.Core.Domain.Gamification;
using HabitGrid.Platform.Desktop;
using HabitGrid.Presentation.Services;
using HabitGrid.Presentation.ViewModels;

namespace HabitGrid.WidgetHost;

/// <summary>
/// Nền chung của các widget Plus (M1): cửa sổ không viền, nền tối bo góc (chữ trắng đọc được trên mọi hình nền),
/// kéo được ở chế độ sắp xếp (hoặc giữ Alt), nhớ vị trí qua sự kiện <see cref="Moved"/>, ghim lớp desktop như các widget khác.
/// </summary>
public abstract class ExtraWidgetWindow : Window
{
    private static readonly Brush CardBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xE6, 0x1B, 0x15, 0x30)));
    private static readonly Brush RimBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)));

    protected ExtraWidgetWindow(string title, double width, double height)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = width;
        Height = height;
        Title = title;

        MouseLeftButtonDown += (_, _) =>
        {
            if (!EditMode && !Keyboard.IsKeyDown(Key.LeftAlt) && !Keyboard.IsKeyDown(Key.RightAlt))
            {
                return;
            }

            try
            {
                DragMove();
                Moved?.Invoke(this, EventArgs.Empty);
            }
            catch (InvalidOperationException)
            {
                // Nhả chuột trước khi kéo.
            }
        };
    }

    public bool EditMode { get; set; }

    public event EventHandler? Moved;

    /// <summary>Đặt nội dung vào thẻ nền chung.</summary>
    protected void SetCard(UIElement content) =>
        Content = new Border
        {
            CornerRadius = new CornerRadius(22),
            Background = CardBrush,
            BorderBrush = RimBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 14, 16, 14),
            Child = content
        };

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

    /// <summary>
    /// WPF chỉ vẽ emoji đơn sắc (không hỗ trợ phông màu): ép màu trắng để thấy rõ trên nền tối thay vì mặc định đen.
    /// </summary>
    protected static TextBlock Emoji(string emoji, double size) => new() { Text = emoji, FontSize = size, Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI Emoji") };

    protected static Border Bar(double fraction, double width, Color from, Color to)
    {
        var track = new Border { Height = 7, Width = width, CornerRadius = new CornerRadius(3.5), Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Left };
        track.Child = new Border
        {
            CornerRadius = new CornerRadius(3.5),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = Math.Max(0, Math.Min(1, fraction)) * width,
            Background = new LinearGradientBrush(from, to, 0)
        };
        return track;
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

/// <summary>Widget Buddy: linh vật đeo phụ kiện, cấp độ, tiến độ hôm nay và số token đóng băng.</summary>
public sealed class BuddyWidgetWindow : ExtraWidgetWindow
{
    public BuddyWidgetWindow() : base("HabitGrid Buddy", 230, 150)
    {
    }

    public void Update(BuddyWidgetData data)
    {
        var avatar = new Grid { Width = 84, Height = 96 };
        var body = Emoji(StageEmoji(data.Stage), 58);
        body.HorizontalAlignment = HorizontalAlignment.Center;
        body.VerticalAlignment = VerticalAlignment.Center;
        avatar.Children.Add(body);

        foreach (var (slot, emoji) in data.Worn)
        {
            var piece = Emoji(emoji, slot == AccessorySlot.Head ? 34 : 26);
            piece.HorizontalAlignment = HorizontalAlignment.Center;
            piece.VerticalAlignment = slot switch { AccessorySlot.Head => VerticalAlignment.Top, AccessorySlot.Face => VerticalAlignment.Center, _ => VerticalAlignment.Bottom };
            piece.Margin = slot switch { AccessorySlot.Head => new Thickness(0, -8, 0, 0), AccessorySlot.Face => new Thickness(0, 10, 0, 0), _ => new Thickness(0, 0, 0, -4) };
            avatar.Children.Add(piece);
        }

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
        SetCard(row);
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

/// <summary>Widget Mục tiêu: ý định tuần này và tối đa 3 thử thách đang chạy.</summary>
public sealed class GoalsWidgetWindow : ExtraWidgetWindow
{
    public GoalsWidgetWindow() : base("HabitGrid Goals", 270, 190)
    {
    }

    public void Update(GoalsWidgetData data)
    {
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

        SetCard(stack);
    }

    private static UIElement Row(double fraction, string label, Color accent)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Bar(fraction, 170, accent, Color.FromRgb(236, 72, 153)));
        row.Children.Add(Text(label, 11, opacity: 0.85));
        ((TextBlock)row.Children[1]).Margin = new Thickness(8, 0, 0, 0);
        return row;
    }
}

/// <summary>Widget Multi Grid: 2–4 thói quen, mỗi thói quen một heatmap 13 tuần nhỏ kèm chuỗi.</summary>
public sealed class MultiGridWidgetWindow : ExtraWidgetWindow
{
    private const double Cell = 8;
    private const double Gap = 2;

    public MultiGridWidgetWindow() : base("HabitGrid Multi Grid", 300, 120)
    {
    }

    public void Update(MultiGridData data)
    {
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
            stack.Put(line);
        }

        // Mỗi hàng cao 70 (heatmap 7 ô), cách nhau 8, cộng viền và lề của thẻ.
        var count = Math.Max(1, data.Rows.Count);
        Height = 34 + count * 70 + (count - 1) * 8;
        SetCard(stack);
    }

    private static Canvas Heatmap(MultiGridRow row, int weeks)
    {
        var accent = Hex(row.ColorHue);
        var done = new SolidColorBrush(accent);
        var frozen = new SolidColorBrush(Color.FromRgb(56, 189, 248));
        var empty = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        var canvas = new Canvas { Width = weeks * (Cell + Gap), Height = 7 * (Cell + Gap), HorizontalAlignment = HorizontalAlignment.Right };

        for (var i = 0; i < row.Cells.Count; i++)
        {
            var value = row.Cells[i];

            if (value < 0)
            {
                continue;
            }

            var rect = new Rectangle
            {
                Width = Cell,
                Height = Cell,
                RadiusX = 2,
                RadiusY = 2,
                Fill = value switch { 1 => done, 2 => frozen, _ => empty }
            };
            Canvas.SetLeft(rect, i / 7 * (Cell + Gap));
            Canvas.SetTop(rect, i % 7 * (Cell + Gap));
            canvas.Children.Add(rect);
        }

        return canvas;
    }
}
