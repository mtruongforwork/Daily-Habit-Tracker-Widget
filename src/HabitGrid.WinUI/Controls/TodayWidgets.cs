using System.Numerics;
using HabitGrid.Presentation.ViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace HabitGrid.WinUI.Controls;

/// <summary>Dải 7 ngày của tuần: vòng % hoàn thành từng ngày, chọn ngày để xem và sửa (kể cả ngày cũ).</summary>
public sealed class WeekStrip : UserControl
{
    private readonly Grid _grid = new() { ColumnSpacing = 8 };

    public WeekStrip()
    {
        IsTabStop = false;
        Content = _grid;
    }

    /// <summary>Người dùng chọn một ngày (không phát cho ngày tương lai).</summary>
    public event EventHandler<DateOnly>? DaySelected;

    public void Update(IReadOnlyList<DayBubble> days)
    {
        _grid.Children.Clear();
        _grid.ColumnDefinitions.Clear();

        for (var i = 0; i < days.Count; i++)
        {
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var bubble = Create(days[i]);
            Grid.SetColumn(bubble, i);
            _grid.Children.Add(bubble);
        }
    }

    private FrameworkElement Create(DayBubble day)
    {
        var label = DayLabel(day.Day.DayOfWeek);
        var selected = day.IsSelected;

        var pill = new Border
        {
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(0, 10, 0, 10),
            Background = selected
                ? (Brush)Application.Current.Resources["AccentGradientBrush"]
                : (Brush)Application.Current.Resources["CardIdleBrush"],
            BorderBrush = (Brush)Application.Current.Resources["StrokeSubtleBrush"],
            BorderThickness = new Thickness(selected ? 0 : 1),
            Opacity = day.IsFuture ? 0.4 : 1
        };

        var stack = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };

        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            FontFamily = (FontFamily)Application.Current.Resources["DisplayBoldFont"],
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = selected
                ? (Brush)Application.Current.Resources["OnGradientBrush"]
                : (Brush)Application.Current.Resources["TextMutedBrush"]
        });

        var ring = new Grid { Width = 38, Height = 38 };
        var trackColor = selected ? Windows.UI.Color.FromArgb(70, 255, 255, 255) : Ui.ParseColor("#33808090");
        ring.Children.Add(new Ellipse { Stroke = new SolidColorBrush(trackColor), StrokeThickness = 4 });

        if (day.Fraction is { } fraction && fraction > 0)
        {
            var color = selected ? Microsoft.UI.Colors.White : Ui.ParseColor("#A78BFA");
            ring.Children.Add(ArcFor(fraction, color));
        }

        ring.Children.Add(new TextBlock
        {
            Text = day.Day.Day.ToString(),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = (FontFamily)Application.Current.Resources["DisplaySemiBoldFont"],
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = selected
                ? (Brush)Application.Current.Resources["OnGradientBrush"]
                : (Brush)Application.Current.Resources["TextPrimaryBrush"]
        });
        stack.Children.Add(ring);
        pill.Child = stack;

        var button = new Button
        {
            Content = pill,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            IsEnabled = !day.IsFuture,
            CornerRadius = new CornerRadius(22)
        };

        var name = day.IsToday ? Ui.T("week.today") : day.Day.ToString("dddd d/M", Ui.CurrentCulture);
        var percent = day.Fraction is { } f ? $"{Math.Round(f * 100)}%" : string.Empty;
        AutomationProperties.SetName(button, $"{name} {percent}".Trim());
        AutomationProperties.SetItemStatus(button, selected ? "selected" : string.Empty);
        button.Click += (_, _) => DaySelected?.Invoke(this, day.Day);
        return button;
    }

    /// <summary>Cung tròn thể hiện phần trăm hoàn thành, bắt đầu từ đỉnh theo chiều kim đồng hồ.</summary>
    private static UIElement ArcFor(double fraction, Windows.UI.Color color)
    {
        const double size = 38;
        const double thickness = 4;
        var radius = (size - thickness) / 2;
        var center = size / 2;

        if (fraction >= 0.999)
        {
            return new Ellipse { Stroke = new SolidColorBrush(color), StrokeThickness = thickness };
        }

        var angle = fraction * 2 * Math.PI;
        var end = new Windows.Foundation.Point(center + radius * Math.Sin(angle), center - radius * Math.Cos(angle));

        var figure = new PathFigure { StartPoint = new Windows.Foundation.Point(center, center - radius), IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Windows.Foundation.Size(radius, radius),
            IsLargeArc = fraction > 0.5,
            SweepDirection = SweepDirection.Clockwise
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        return new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = geometry,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
    }

    private static string DayLabel(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => Ui.T("day.mon"),
        DayOfWeek.Tuesday => Ui.T("day.tue"),
        DayOfWeek.Wednesday => Ui.T("day.wed"),
        DayOfWeek.Thursday => Ui.T("day.thu"),
        DayOfWeek.Friday => Ui.T("day.fri"),
        DayOfWeek.Saturday => Ui.T("day.sat"),
        _ => Ui.T("day.sun")
    };
}

/// <summary>Ô tổng quan lớn: Buddy theo tâm trạng + tiến độ hôm nay + chỗ cho XP/level (SPEC-V2-06).</summary>
public sealed class HeroTile : UserControl
{
    private readonly TextBlock _buddy = new() { FontSize = 56, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _headline = new();
    private readonly TextBlock _progressText = new();
    private readonly Grid _bar = new() { Height = 12, CornerRadius = new CornerRadius(6) };
    private readonly Border _barFill = new() { CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Microsoft.UI.Colors.White) };
    private readonly TextBlock _level = new();
    private readonly Grid _xpBar = new() { Height = 6, CornerRadius = new CornerRadius(3) };
    private readonly Border _xpFill = new() { CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(230, 255, 255, 255)) };

    public HeroTile()
    {
        IsTabStop = false;

        var bubble = new Border
        {
            Width = 96,
            Height = 96,
            CornerRadius = new CornerRadius(48),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(56, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _buddy
        };

        _headline.FontFamily = (FontFamily)Application.Current.Resources["DisplayFont"];
        _headline.FontWeight = Microsoft.UI.Text.FontWeights.ExtraBold;
        _headline.FontSize = 22;
        _headline.Foreground = (Brush)Application.Current.Resources["OnGradientBrush"];
        _headline.TextTrimming = TextTrimming.CharacterEllipsis;

        _progressText.FontSize = 14;
        _progressText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(235, 255, 255, 255));
        _level.FontSize = 12;
        _level.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(215, 255, 255, 255));

        _bar.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(70, 0, 0, 0));
        _bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) });
        _bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _bar.Children.Add(_barFill);

        _xpBar.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 0, 0, 0));
        _xpBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) });
        _xpBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _xpBar.Children.Add(_xpFill);

        var text = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(_headline);
        text.Children.Add(_bar);
        text.Children.Add(_progressText);
        text.Children.Add(new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 0), Children = { _level, _xpBar } });

        var grid = new Grid { ColumnSpacing = 22, Padding = new Thickness(26, 22, 26, 22) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(bubble);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        Content = new Border
        {
            CornerRadius = new CornerRadius(32),
            Background = (Brush)Application.Current.Resources["AccentGradientBrush"],
            Child = grid
        };
    }

    public void Update(TodayViewModel vm)
    {
        _buddy.Text = vm.BuddyMood switch
        {
            BuddyMood.Sleepy => "😴",
            BuddyMood.Okay => "🐣",
            BuddyMood.Happy => "🐥",
            _ => "🥳"
        };

        var name = string.IsNullOrWhiteSpace(vm.BuddyName) ? Ui.T("onboarding.buddy.default") : vm.BuddyName;
        _headline.Text = Ui.Loc.Format(vm.BuddyMood switch
        {
            BuddyMood.Sleepy => "today.buddy.sleepy",
            BuddyMood.Okay => "today.buddy.okay",
            BuddyMood.Happy => "today.buddy.happy",
            _ => "today.buddy.ecstatic"
        }, name);

        _progressText.Text = vm.TotalCount == 0 ? Ui.T("today.relax") : Ui.Progress(vm.DoneCount, vm.TotalCount);
        SetBar(_bar, vm.ProgressFraction);

        // Chưa có hệ XP (SPEC-V2-06): thanh XP theo level giả định để giữ chỗ trong bố cục.
        _level.Text = Ui.Loc.Format("today.level", vm.Level, vm.Xp);
        var needed = Math.Max(1, (long)Math.Round(50 * Math.Pow(vm.Level, 1.6)));
        SetBar(_xpBar, Math.Clamp(vm.Xp / (double)needed, 0, 1));

        AutomationProperties.SetName(this, $"{_headline.Text}. {_progressText.Text}");
    }

    private static void SetBar(Grid bar, double fraction)
    {
        bar.ColumnDefinitions[0].Width = new GridLength(Math.Max(fraction, 0.0001), GridUnitType.Star);
        bar.ColumnDefinitions[1].Width = new GridLength(Math.Max(1 - fraction, 0.0001), GridUnitType.Star);
    }
}

/// <summary>Ô streak tổng: số ngày liền cỡ lớn, lửa đổi theo mốc, số freeze.</summary>
public sealed class StreakTile : UserControl
{
    private readonly TextBlock _flame = new() { FontSize = 40 };
    private readonly TextBlock _number = new();
    private readonly TextBlock _label = new();
    private readonly TextBlock _freeze = new();

    public StreakTile()
    {
        IsTabStop = false;
        _number.Style = (Style)Application.Current.Resources["NumberXLTextStyle"];
        _label.Style = (Style)Application.Current.Resources["BodySecondaryTextStyle"];
        _freeze.FontSize = 12;
        _freeze.Foreground = (Brush)Application.Current.Resources["FreezeBrush"];

        var numbers = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        numbers.Children.Add(_flame);
        numbers.Children.Add(_number);

        var stack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(numbers);
        stack.Children.Add(_label);
        stack.Children.Add(new Border { Height = 8 });
        stack.Children.Add(_freeze);

        Content = new Border
        {
            CornerRadius = new CornerRadius(32),
            Padding = new Thickness(26, 22, 26, 22),
            Background = (Brush)Application.Current.Resources["CardIdleBrush"],
            BorderBrush = (Brush)Application.Current.Resources["StrokeSubtleBrush"],
            BorderThickness = new Thickness(1),
            Child = stack
        };
    }

    public void Update(TodayViewModel vm)
    {
        var streak = vm.OverallStreak;

        // Lửa đổi theo mốc streak (7 / 30 / 100 / 365).
        _flame.Text = streak switch
        {
            >= 365 => "💎",
            >= 100 => "🌟",
            >= 30 => "🔥",
            >= 7 => "🔥",
            > 0 => "🕯️",
            _ => "🌱"
        };

        _number.Text = streak.ToString();
        _label.Text = Ui.T("today.streakLabel");
        _freeze.Text = Ui.Loc.Format("today.freeze", vm.FreezeTokens);
        AutomationProperties.SetName(this, $"{streak} {_label.Text}");
    }
}

/// <summary>
/// Ô "Focus nhanh" của hàng Mochi (v3): bắt đầu ngay một phiên Pomodoro 25 phút. Phiên chạy ở widget (tiếp tục kể cả khi đóng app),
/// đồng hồ hiện trên viên thuốc Dynamic Island; trang Focus có đầy đủ lựa chọn.
/// </summary>
public sealed class QuickFocusTile : UserControl
{
    private readonly TextBlock _status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };

    public QuickFocusTile()
    {
        IsTabStop = false;
        _status.Style = (Style)Application.Current.Resources["BodySecondaryTextStyle"];
        _status.Text = Ui.T("today.quickFocus.hint");

        var start = new Button
        {
            Style = (Style)Application.Current.Resources["GradientButtonStyle"],
            Content = new TextBlock { Text = Ui.T("today.quickFocus.start"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(12, 10, 12, 10)
        };
        AutomationProperties.SetName(start, Ui.T("today.quickFocus.start"));
        start.Click += async (_, _) =>
        {
            start.IsEnabled = false;

            try
            {
                var model = App.Host.Get<HabitGrid.Presentation.Services.FocusPageModel>();
                await model.LoadAsync();
                await model.StartAsync(new HabitGrid.Presentation.Services.FocusChoice(HabitGrid.Core.Domain.Focus.FocusMode.Pomodoro, 25, 5, null), DateTime.UtcNow);
                _status.Text = Ui.T("today.quickFocus.started");
            }
            catch (Exception ex)
            {
                App.Host.Get<HabitGrid.Presentation.Services.IAppLogger>().Error("Bắt đầu Focus nhanh lỗi", ex);
                _status.Text = Ui.T("today.quickFocus.failed");
            }
            finally
            {
                start.IsEnabled = true;
            }
        };

        var stack = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = "⏱", FontSize = 34 });
        stack.Children.Add(new TextBlock { Text = Ui.T("today.quickFocus.title"), Style = (Style)Application.Current.Resources["CardTitleTextStyle"], TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(_status);
        stack.Children.Add(start);

        Content = new Border
        {
            CornerRadius = new CornerRadius(32),
            Padding = new Thickness(22, 20, 22, 20),
            Background = (Brush)Application.Current.Resources["CardIdleBrush"],
            BorderBrush = (Brush)Application.Current.Resources["GlassStrokeBrush"],
            BorderThickness = new Thickness(1),
            Child = stack
        };
    }
}

/// <summary>Confetti nhẹ (XAML + Composition, ≤ 80 hạt) cho khoảnh khắc hoàn thành cả ngày.</summary>
public sealed class ConfettiOverlay : UserControl
{
    private static readonly string[] Palette = ["#8B5CF6", "#EC4899", "#06B6D4", "#FACC15", "#10B981", "#FB923C", "#F43F5E"];
    private readonly Canvas _canvas = new();
    private readonly Random _random = new();

    public ConfettiOverlay()
    {
        IsHitTestVisible = false;
        IsTabStop = false;
        Content = _canvas;
    }

    /// <summary>Bắn confetti từ điểm <paramref name="origin"/> (toạ độ trong overlay).</summary>
    public void Burst(Windows.Foundation.Point origin, int count = 70)
    {
        if (!App.Host.Get<ThemeService>().AnimationsEnabled)
        {
            return;
        }

        var compositor = ElementCompositionPreview.GetElementVisual(_canvas).Compositor;

        for (var i = 0; i < Math.Min(count, 80); i++)
        {
            var size = 6 + _random.NextDouble() * 8;
            var piece = new Rectangle
            {
                Width = size,
                Height = size * (0.5 + _random.NextDouble() * 0.8),
                RadiusX = 2,
                RadiusY = 2,
                Fill = new SolidColorBrush(Ui.ParseColor(Palette[_random.Next(Palette.Length)]))
            };

            Canvas.SetLeft(piece, origin.X);
            Canvas.SetTop(piece, origin.Y);
            _canvas.Children.Add(piece);

            var visual = ElementCompositionPreview.GetElementVisual(piece);
            ElementCompositionPreview.SetIsTranslationEnabled(piece, true);

            var angle = -Math.PI / 2 + (_random.NextDouble() - 0.5) * Math.PI * 1.1;
            var speed = 220 + _random.NextDouble() * 420;
            var dx = (float)(Math.Cos(angle) * speed);
            var dyUp = (float)(Math.Sin(angle) * speed);
            var fall = 380f + (float)_random.NextDouble() * 260f;
            var duration = TimeSpan.FromMilliseconds(1100 + _random.Next(700));

            var move = compositor.CreateVector3KeyFrameAnimation();
            move.InsertKeyFrame(0f, Vector3.Zero);
            move.InsertKeyFrame(0.35f, new Vector3(dx * 0.7f, dyUp * 0.6f, 0), compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.6f), new Vector2(0.3f, 1f)));
            move.InsertKeyFrame(1f, new Vector3(dx, dyUp * 0.3f + fall, 0), compositor.CreateCubicBezierEasingFunction(new Vector2(0.5f, 0f), new Vector2(0.9f, 0.6f)));
            move.Duration = duration;

            var spin = compositor.CreateScalarKeyFrameAnimation();
            spin.InsertKeyFrame(1f, (float)((_random.NextDouble() - 0.5) * 900));
            spin.Duration = duration;

            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0.7f, 1f);
            fade.InsertKeyFrame(1f, 0f);
            fade.Duration = duration;

            visual.CenterPoint = new Vector3((float)piece.Width / 2, (float)piece.Height / 2, 0);

            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            visual.StartAnimation("Translation", move);
            visual.StartAnimation("RotationAngleInDegrees", spin);
            visual.StartAnimation("Opacity", fade);
            batch.End();

            var captured = piece;
            batch.Completed += (_, _) => DispatcherQueue.TryEnqueue(() => _canvas.Children.Remove(captured));
        }
    }
}
