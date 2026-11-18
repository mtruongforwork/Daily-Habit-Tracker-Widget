using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace HabitGrid.WinUI.Controls;

/// <summary>
/// Vòng tiến độ tự vẽ (ProgressRing của WinUI không dựng ổn định khi render ngoài màn hình): vòng nền + cung gradient bắt đầu từ 12 giờ,
/// kèm quầng mờ phía sau, nội dung tùy ý ở giữa. Dùng chung cho thẻ tổng kết (Lịch, Insights...).
/// </summary>
public sealed class RingProgress : Grid
{
    private readonly Microsoft.UI.Xaml.Shapes.Path _arc;
    private readonly Microsoft.UI.Xaml.Shapes.Path _glow;
    private readonly double _size;
    private readonly double _thickness;

    public RingProgress(double size, double thickness, UIElement? center = null)
    {
        _size = size;
        _thickness = thickness;
        Width = size;
        Height = size;

        var brand = (Brush)Application.Current.Resources["AccentGradientBrush"];
        Children.Add(new Ellipse { Width = size - thickness, Height = size - thickness, StrokeThickness = thickness, Stroke = (Brush)Application.Current.Resources["HeatEmptyBrush"], HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        _glow = new Microsoft.UI.Xaml.Shapes.Path { Stroke = brand, StrokeThickness = thickness * 2.2, Opacity = 0.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        _arc = new Microsoft.UI.Xaml.Shapes.Path { Stroke = brand, StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        Children.Add(_glow);
        Children.Add(_arc);

        if (center is not null)
        {
            if (center is FrameworkElement element)
            {
                element.HorizontalAlignment = HorizontalAlignment.Center;
                element.VerticalAlignment = VerticalAlignment.Center;
            }

            Children.Add(center);
        }
    }

    public void SetFraction(double fraction)
    {
        var geometry = Arc(fraction);
        _arc.Data = geometry;
        _glow.Data = Arc(fraction);
    }

    private Geometry? Arc(double fraction)
    {
        var f = Math.Clamp(fraction, 0, 0.9999);

        if (f <= 0.001)
        {
            return null;
        }

        var middle = _size / 2;
        var radius = (_size - _thickness) / 2;
        var angle = f * 2 * Math.PI;
        var figure = new PathFigure { StartPoint = new Windows.Foundation.Point(middle, middle - radius) };
        figure.Segments.Add(new ArcSegment
        {
            Point = new Windows.Foundation.Point(middle + (radius * Math.Sin(angle)), middle - (radius * Math.Cos(angle))),
            Size = new Windows.Foundation.Size(radius, radius),
            IsLargeArc = f > 0.5,
            SweepDirection = SweepDirection.Clockwise
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
}
