using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace HabitGrid.WinUI.Controls;

/// <summary>
/// Biểu đồ vùng mượt (v3, thay cho các cột chữ nhật đặc ở "Xu hướng theo tuần"): đường cong qua các điểm 0..1, vùng dưới tô gradient mờ dần,
/// lưới ngang mờ 0/50/100%. Điểm không có dữ liệu (null) được bỏ qua. Vẽ lại theo bề rộng thật của khung.
/// </summary>
public sealed class AreaChart : Grid
{
    private readonly IReadOnlyList<(string Label, double? Value)> _values;
    private readonly Canvas _canvas = new();
    private readonly Grid _labels = new();
    private readonly double _plotHeight;

    public AreaChart(IReadOnlyList<(string Label, double? Value)> values, double height = 140)
    {
        _values = values;
        _plotHeight = height;
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(height) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Children.Add(_canvas);
        Grid.SetRow(_labels, 1);
        Children.Add(_labels);

        var known = values.Where(v => v.Value is not null).Select(v => v.Value!.Value).ToList();
        AutomationProperties.SetName(this, known.Count == 0 ? string.Empty : string.Join(", ", values.Where(v => v.Value is not null).Select(v => $"{v.Label}: {Math.Round(v.Value!.Value * 100)}%")));
        SizeChanged += (_, e) => Draw(e.NewSize.Width);
    }

    private void Draw(double width)
    {
        _canvas.Children.Clear();
        _labels.Children.Clear();
        _labels.ColumnDefinitions.Clear();

        if (width <= 0 || _values.Count == 0)
        {
            return;
        }

        var muted = (Brush)Application.Current.Resources["StrokeSubtleBrush"];
        var top = 8.0;
        var bottom = _plotHeight - 4;
        double Y(double v) => bottom - (Math.Clamp(v, 0, 1) * (bottom - top));
        double X(int i) => _values.Count == 1 ? width / 2 : 6 + (i * (width - 12) / (_values.Count - 1));

        foreach (var level in new[] { 0, 0.5, 1 })
        {
            _canvas.Children.Add(new Line { X1 = 0, X2 = width, Y1 = Y(level), Y2 = Y(level), Stroke = muted, StrokeThickness = 1, StrokeDashArray = level == 0 ? null : new DoubleCollection { 3, 4 } });
        }

        var points = _values.Select((v, i) => (Index: i, v.Value)).Where(p => p.Value is not null).Select(p => new Windows.Foundation.Point(X(p.Index), Y(p.Value!.Value))).ToList();

        if (points.Count >= 2)
        {
            var line = Curve(points, close: false);
            var area = Curve(points, close: true, baseline: bottom);
            var fill = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 1) };
            var accent = ((SolidColorBrush)Application.Current.Resources["AccentBrandBrush"]).Color;
            fill.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(0x70, accent.R, accent.G, accent.B), Offset = 0 });
            fill.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(0x00, accent.R, accent.G, accent.B), Offset = 1 });
            _canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = area, Fill = fill });
            _canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = line, Stroke = (Brush)Application.Current.Resources["AccentGradientBrush"], StrokeThickness = 3, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        }

        foreach (var point in points.TakeLast(1))
        {
            var dot = new Ellipse { Width = 10, Height = 10, Fill = (Brush)Application.Current.Resources["AccentBrandBrush"], Stroke = (Brush)Application.Current.Resources["TextPrimaryBrush"], StrokeThickness = 2 };
            Canvas.SetLeft(dot, point.X - 5);
            Canvas.SetTop(dot, point.Y - 5);
            _canvas.Children.Add(dot);
        }

        // Nhãn trục ngang thưa (tối đa ~6) để không chồng chữ.
        var step = Math.Max(1, (int)Math.Ceiling(_values.Count / 6.0));

        for (var i = 0; i < _values.Count; i++)
        {
            _labels.ColumnDefinitions.Add(new ColumnDefinition());

            if (i % step == 0 || i == _values.Count - 1)
            {
                var caption = new TextBlock { Text = _values[i].Label, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Foreground = (Brush)Application.Current.Resources["TextMutedBrush"], TextWrapping = TextWrapping.NoWrap };
                Grid.SetColumn(caption, i);
                _labels.Children.Add(caption);
            }
        }
    }

    /// <summary>Đường cong mượt qua các điểm (Catmull-Rom đổi sang Bézier bậc ba); <paramref name="close"/> khép vùng xuống đường đáy.</summary>
    private static PathGeometry Curve(IReadOnlyList<Windows.Foundation.Point> p, bool close, double baseline = 0)
    {
        var figure = new PathFigure { StartPoint = close ? new Windows.Foundation.Point(p[0].X, baseline) : p[0], IsClosed = close };

        if (close)
        {
            figure.Segments.Add(new LineSegment { Point = p[0] });
        }

        for (var i = 0; i < p.Count - 1; i++)
        {
            var p0 = p[Math.Max(0, i - 1)];
            var p1 = p[i];
            var p2 = p[i + 1];
            var p3 = p[Math.Min(p.Count - 1, i + 2)];
            figure.Segments.Add(new BezierSegment
            {
                Point1 = new Windows.Foundation.Point(p1.X + ((p2.X - p0.X) / 6), p1.Y + ((p2.Y - p0.Y) / 6)),
                Point2 = new Windows.Foundation.Point(p2.X - ((p3.X - p1.X) / 6), p2.Y - ((p3.Y - p1.Y) / 6)),
                Point3 = p2
            });
        }

        if (close)
        {
            figure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(p[^1].X, baseline) });
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
}
