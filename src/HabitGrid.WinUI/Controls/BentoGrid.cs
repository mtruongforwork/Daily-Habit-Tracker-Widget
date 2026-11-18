using HabitGrid.Core.Domain.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace HabitGrid.WinUI.Controls;

/// <summary>
/// Lưới Bento của giao diện v3 (thay cho CSS grid trong thiết kế): 8 rãnh ở cửa sổ rộng, 4 rãnh ở cửa sổ vừa, 2 rãnh ở cửa sổ hẹp.
/// Mỗi ô khai <see cref="SpanProperty"/> (số rãnh, 1 "cột" thiết kế = 2 rãnh) và <see cref="RowSpanProperty"/>. Vị trí do
/// <see cref="BentoLayout"/> tính (có unit test); chiều cao mỗi hàng bằng ô cao nhất trong hàng, ô nhiều hàng kéo giãn hàng cuối nếu cần.
/// </summary>
public sealed class BentoGrid : Panel
{
    public static readonly DependencyProperty SpanProperty =
        DependencyProperty.RegisterAttached("Span", typeof(int), typeof(BentoGrid), new PropertyMetadata(2, OnCellChanged));

    public static readonly DependencyProperty RowSpanProperty =
        DependencyProperty.RegisterAttached("RowSpan", typeof(int), typeof(BentoGrid), new PropertyMetadata(1, OnCellChanged));

    private IReadOnlyList<BentoSlot> _slots = [];
    private double[] _rows = [];
    private double _track;

    public static int GetSpan(DependencyObject element) => (int)element.GetValue(SpanProperty);

    public static void SetSpan(DependencyObject element, int value) => element.SetValue(SpanProperty, value);

    public static int GetRowSpan(DependencyObject element) => (int)element.GetValue(RowSpanProperty);

    public static void SetRowSpan(DependencyObject element, int value) => element.SetValue(RowSpanProperty, value);

    /// <summary>Đặt ô vào lưới với độ rộng theo "cột" thiết kế (0.5 cột = 1 rãnh; 1.5 cột = 3 rãnh) và trả về chính ô đó.</summary>
    public T Add<T>(T element, double columns, int rows = 1) where T : UIElement
    {
        SetSpan(element, Math.Max(1, (int)Math.Round(columns * 2)));
        SetRowSpan(element, rows);
        Children.Add(element);
        return element;
    }

    public double Gap { get; set; } = 16;

    /// <summary>Chiều cao tối thiểu của một hàng (để các ô ít nội dung không quá dẹt).</summary>
    public double MinRowHeight { get; set; }

    /// <summary>Số rãnh đang dùng (8, 4 hoặc 2) theo bề rộng lần đo gần nhất.</summary>
    public int Tracks { get; private set; } = BentoLayout.WideTracks;

    private static void OnCellChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && VisualTreeHelper.GetParent(element) is BentoGrid grid)
        {
            grid.InvalidateMeasure();
        }
    }

    private double CellWidth(BentoSlot slot) => (slot.Span * _track) + ((slot.Span - 1) * Gap);

    private double RowsHeight(int row, int count)
    {
        var height = 0.0;

        for (var r = row; r < row + count; r++)
        {
            height += _rows[r];
        }

        return height + ((count - 1) * Gap);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? BentoLayout.WideFrom : availableSize.Width;
        Tracks = BentoLayout.TracksFor(width);
        _track = Math.Max(0, (width - ((Tracks - 1) * Gap)) / Tracks);

        var visible = Children.Where(c => c.Visibility == Visibility.Visible).ToList();
        _slots = BentoLayout.Arrange(visible.Select(c => new BentoItem(GetSpan(c), GetRowSpan(c))).ToList(), Tracks);
        _rows = new double[BentoLayout.RowCount(_slots)];

        for (var r = 0; r < _rows.Length; r++)
        {
            _rows[r] = MinRowHeight;
        }

        // Ô một hàng quyết định chiều cao hàng; ô nhiều hàng kéo giãn hàng cuối của nó nếu cao hơn tổng các hàng.
        for (var i = 0; i < visible.Count; i++)
        {
            var slot = _slots[i];
            visible[i].Measure(new Size(CellWidth(slot), double.PositiveInfinity));

            if (slot.RowSpan == 1)
            {
                _rows[slot.Row] = Math.Max(_rows[slot.Row], visible[i].DesiredSize.Height);
            }
        }

        for (var i = 0; i < visible.Count; i++)
        {
            var slot = _slots[i];

            if (slot.RowSpan > 1)
            {
                var deficit = visible[i].DesiredSize.Height - RowsHeight(slot.Row, slot.RowSpan);

                if (deficit > 0)
                {
                    _rows[slot.Row + slot.RowSpan - 1] += deficit;
                }
            }
        }

        foreach (var hidden in Children.Where(c => c.Visibility != Visibility.Visible))
        {
            hidden.Measure(new Size(0, 0));
        }

        var total = _rows.Length == 0 ? 0 : RowsHeight(0, _rows.Length);
        return new Size(width, total);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visible = Children.Where(c => c.Visibility == Visibility.Visible).ToList();

        for (var i = 0; i < visible.Count && i < _slots.Count; i++)
        {
            var slot = _slots[i];
            var x = slot.Column * (_track + Gap);
            var y = slot.Row == 0 ? 0 : RowsHeight(0, slot.Row) + Gap;
            visible[i].Arrange(new Rect(x, y, CellWidth(slot), RowsHeight(slot.Row, slot.RowSpan)));
        }

        return finalSize;
    }
}
