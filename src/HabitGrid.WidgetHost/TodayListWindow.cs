using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using HabitGrid.Platform.Desktop;
using HabitGrid.Presentation.Services;

namespace HabitGrid.WidgetHost;

/// <summary>
/// Widget "Today List" ghim desktop (SPEC-V2-05 §2.1): vòng tiến độ + danh sách thói quen hôm nay.
/// Bấm dòng = check (đếm: +1), chuột phải = −1. Kéo tiêu đề khi giữ Alt hoặc ở chế độ sắp xếp.
/// </summary>
public sealed class TodayListWindow : Window
{
    private readonly QuickCheckInModel _model;
    private readonly Func<QuickItem, int, Task> _onAction;
    private readonly StackPanel _rows = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _progress = new();
    private readonly Border _barFill = new() { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _barTrack = new() { Height = 6, CornerRadius = new CornerRadius(3) };
    private readonly Border _root = new() { CornerRadius = new CornerRadius(22), Padding = new Thickness(18, 14, 18, 14) };
    private readonly TextBlock _title = new();
    private bool _dark = true;

    public TodayListWindow(QuickCheckInModel model, Func<QuickItem, int, Task> onAction)
    {
        _model = model;
        _onAction = onAction;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 300;
        Title = "HabitGrid Today";

        _title.FontSize = 18;
        _title.FontWeight = FontWeights.Bold;
        _progress.FontSize = 14;
        _progress.FontWeight = FontWeights.SemiBold;
        _progress.HorizontalAlignment = HorizontalAlignment.Right;
        _progress.VerticalAlignment = VerticalAlignment.Center;

        var header = new Grid();
        header.Children.Add(_title);
        header.Children.Add(_progress);
        header.MouseLeftButtonDown += OnHeaderMouseDown;

        var bar = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        bar.Children.Add(_barTrack);
        bar.Children.Add(_barFill);
        bar.SizeChanged += (_, _) => UpdateBar();

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(bar);
        stack.Children.Add(_rows);

        _root.Child = stack;
        Content = _root;
    }

    /// <summary>Đang ở chế độ sắp xếp (kéo thả không cần giữ Alt).</summary>
    public bool EditMode { get; set; }

    /// <summary>Người dùng vừa kéo cửa sổ xong (để lưu vị trí).</summary>
    public event EventHandler? Moved;

    public void ApplyTheme(bool dark)
    {
        _dark = dark;
        Render();
    }

    public void Render()
    {
        var fore = _dark ? Brushes.White : new SolidColorBrush(Color.FromRgb(30, 30, 40));
        var sub = new SolidColorBrush(_dark ? Color.FromArgb(170, 255, 255, 255) : Color.FromArgb(160, 30, 30, 40));
        _root.Background = new SolidColorBrush(_dark ? Color.FromArgb(235, 28, 28, 38) : Color.FromArgb(240, 250, 250, 252));
        _root.BorderBrush = new SolidColorBrush(_dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0));
        _root.BorderThickness = new Thickness(1);
        _title.Text = HostText.Today;
        _title.Foreground = fore;
        _progress.Text = HostText.Progress(_model.DoneCount, _model.DueCount);
        _progress.Foreground = sub;
        _barTrack.Background = new SolidColorBrush(_dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0));
        _barFill.Background = new LinearGradientBrush(Color.FromRgb(139, 92, 246), Color.FromRgb(236, 72, 153), 0);
        UpdateBar();

        _rows.Children.Clear();

        if (_model.Items.Count == 0)
        {
            _rows.Children.Add(new TextBlock { Text = HostText.NothingDue, Foreground = sub, Margin = new Thickness(0, 12, 0, 6) });
            return;
        }

        foreach (var item in _model.Filter(null))
        {
            _rows.Children.Add(BuildRow(item, fore, sub));
        }

        if (_model.QuickTodos.Count > 0)
        {
            _rows.Children.Add(new TextBlock
            {
                Text = HostText.QuickTodoHeader,
                Foreground = sub,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(4, 10, 0, 4)
            });

            foreach (var todo in _model.QuickTodos)
            {
                _rows.Children.Add(BuildTodoRow(todo, fore, sub));
            }
        }

        if (_model.DueCount > 0 && _model.DoneCount == _model.DueCount)
        {
            _rows.Children.Add(new TextBlock { Text = HostText.AllDone, Foreground = sub, Margin = new Thickness(0, 8, 0, 2), HorizontalAlignment = HorizontalAlignment.Center });
        }
    }

    public DesktopHostResult Pin(DesktopHostService host, DesktopPinningMode mode)
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        return host.Attach(handle, mode);
    }

    private UIElement BuildRow(QuickItem item, Brush fore, Brush sub)
    {
        var accent = (Color)ColorConverter.ConvertFromString(item.Habit.ColorHue);
        var check = new Ellipse
        {
            Width = 24,
            Height = 24,
            StrokeThickness = 2,
            Stroke = new SolidColorBrush(item.IsDone ? accent : Color.FromArgb(120, accent.R, accent.G, accent.B)),
            Fill = item.IsDone ? new SolidColorBrush(accent) : Brushes.Transparent
        };

        var tick = new TextBlock
        {
            Text = item.IsDone ? "✓" : string.Empty,
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var mark = new Grid { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
        mark.Children.Add(check);
        mark.Children.Add(tick);

        var isGoal = _model.IsGoalOfDay(item.Id);
        var name = new TextBlock
        {
            Text = isGoal ? $"🎯 {item.Name}" : item.Name,
            Foreground = item.IsDone || item.IsSkipped ? sub : fore,
            TextDecorations = item.IsDone ? TextDecorations.Strikethrough : null,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14
        };

        var row = new Grid { Margin = new Thickness(0, 3, 0, 3), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        name.Margin = new Thickness(10, 0, 8, 0);
        Grid.SetColumn(name, 1);
        row.Children.Add(mark);
        row.Children.Add(name);

        if (item.IsNumeric)
        {
            var count = new TextBlock
            {
                Text = $"{item.Value}/{item.Target}",
                Foreground = sub,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(count, 2);
            row.Children.Add(count);
        }

        row.MouseLeftButtonUp += async (_, e) =>
        {
            e.Handled = true;
            await _onAction(item, 0);
        };
        row.MouseRightButtonUp += async (_, e) =>
        {
            e.Handled = true;
            await _onAction(item, -1);
        };
        return row;
    }

    private UIElement BuildTodoRow(HabitGrid.Core.Domain.QuickTodo todo, Brush fore, Brush sub)
    {
        var box = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1.5),
            BorderBrush = sub,
            Background = todo.IsDone ? new SolidColorBrush(Color.FromRgb(139, 92, 246)) : Brushes.Transparent,
            Child = new TextBlock
            {
                Text = todo.IsDone ? "✓" : string.Empty,
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var title = new TextBlock
        {
            Text = todo.Title,
            Foreground = todo.IsDone ? sub : fore,
            TextDecorations = todo.IsDone ? TextDecorations.Strikethrough : null,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 13
        };

        var row = new Grid { Margin = new Thickness(2, 2, 0, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(title, 1);
        row.Children.Add(box);
        row.Children.Add(title);

        row.MouseLeftButtonUp += async (_, e) =>
        {
            e.Handled = true;
            await _model.ToggleQuickTodoAsync(todo);
            await _model.LoadAsync(_model.Today);
            Render();
        };

        return row;
    }

    private void UpdateBar()
    {
        var width = _barTrack.ActualWidth;
        _barFill.Width = Math.Max(0, width * _model.Fraction);
    }

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
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
            // Nút chuột đã nhả trước khi bắt đầu kéo.
        }
    }
}
