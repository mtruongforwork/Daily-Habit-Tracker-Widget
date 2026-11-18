using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace HabitGrid.WinUI.Controls;

/// <summary>Xếp các phần tử theo hàng ngang, hết chỗ thì xuống dòng (WinUI không có sẵn). Dùng thay cho hàng cuộn ngang bị cắt.</summary>
public sealed class WrapPanel : Panel
{
    public double HorizontalSpacing { get; set; } = 8;

    public double VerticalSpacing { get; set; } = 8;

    protected override Size MeasureOverride(Size availableSize)
    {
        var limit = double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width;
        double x = 0, y = 0, line = 0, widest = 0;

        foreach (var child in Children)
        {
            child.Measure(new Size(limit, double.PositiveInfinity));
            var size = child.DesiredSize;

            if (x > 0 && x + size.Width > limit)
            {
                y += line + VerticalSpacing;
                x = 0;
                line = 0;
            }

            x += size.Width + HorizontalSpacing;
            line = Math.Max(line, size.Height);
            widest = Math.Max(widest, x - HorizontalSpacing);
        }

        return new Size(double.IsInfinity(availableSize.Width) ? widest : Math.Min(widest, availableSize.Width), y + line);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, line = 0;

        // Dung sai làm tròn: khi panel được căn giữa, bề rộng lúc xếp có thể nhỏ hơn bề rộng đã đo một phần pixel;
        // không có dung sai thì phần tử cuối bị đẩy xuống dòng mới mà chiều cao đã đo không tính tới (đè lên phần tử bên dưới).
        var limit = Math.Max(finalSize.Width, DesiredSize.Width) + 1;

        foreach (var child in Children)
        {
            var size = child.DesiredSize;

            if (x > 0 && x + size.Width > limit)
            {
                y += line + VerticalSpacing;
                x = 0;
                line = 0;
            }

            child.Arrange(new Rect(x, y, Math.Min(size.Width, limit), size.Height));
            x += size.Width + HorizontalSpacing;
            line = Math.Max(line, size.Height);
        }

        return finalSize;
    }
}
