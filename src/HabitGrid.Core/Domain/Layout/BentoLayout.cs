namespace HabitGrid.Core.Domain.Layout;

/// <summary>Một ô Bento: rộng <see cref="Span"/> rãnh (lưới 8 rãnh; 1 "cột" của thiết kế = 2 rãnh) và cao <see cref="RowSpan"/> hàng.</summary>
public readonly record struct BentoItem(int Span, int RowSpan = 1);

/// <summary>Vị trí đã xếp của một ô.</summary>
public readonly record struct BentoSlot(int Column, int Row, int Span, int RowSpan);

/// <summary>
/// Xếp ô cho lưới Bento (thay cho CSS grid trong thiết kế v3). Lưới rộng có 8 rãnh để làm được ô "1.5 cột" (3 rãnh);
/// cửa sổ hẹp dùng 4 hoặc 2 rãnh, ô rộng hơn số rãnh thì tự thu cho vừa hàng. Xếp theo thứ tự, mỗi ô vào chỗ trống đầu tiên vừa
/// (lấp lỗ như grid-auto-flow: dense), nên không để lại khoảng trống thừa khi ô cao thấp lẫn lộn.
/// </summary>
public static class BentoLayout
{
    public const int WideTracks = 8;
    public const int MediumTracks = 4;
    public const int NarrowTracks = 2;

    /// <summary>Bề rộng (DIP) từ đó dùng lưới đầy đủ 4 cột (8 rãnh).</summary>
    public const double WideFrom = 1000;

    /// <summary>Bề rộng (DIP) từ đó dùng 2 cột (4 rãnh); hẹp hơn thì 1 cột.</summary>
    public const double MediumFrom = 720;

    public static int TracksFor(double width) => width >= WideFrom ? WideTracks : width >= MediumFrom ? MediumTracks : NarrowTracks;

    public static IReadOnlyList<BentoSlot> Arrange(IReadOnlyList<BentoItem> items, int tracks)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfLessThan(tracks, 1);

        var taken = new List<bool[]>();
        var slots = new List<BentoSlot>(items.Count);

        bool Free(int row, int column, int span, int rowSpan)
        {
            for (var r = row; r < row + rowSpan; r++)
            {
                while (taken.Count <= r)
                {
                    taken.Add(new bool[tracks]);
                }

                for (var c = column; c < column + span; c++)
                {
                    if (taken[r][c])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        foreach (var item in items)
        {
            var span = Math.Clamp(item.Span, 1, tracks);
            var rowSpan = Math.Max(1, item.RowSpan);
            var placed = false;

            for (var row = 0; !placed; row++)
            {
                for (var column = 0; column + span <= tracks; column++)
                {
                    if (!Free(row, column, span, rowSpan))
                    {
                        continue;
                    }

                    for (var r = row; r < row + rowSpan; r++)
                    {
                        for (var c = column; c < column + span; c++)
                        {
                            taken[r][c] = true;
                        }
                    }

                    slots.Add(new BentoSlot(column, row, span, rowSpan));
                    placed = true;
                    break;
                }
            }
        }

        return slots;
    }

    /// <summary>Số hàng mà các ô đã xếp chiếm.</summary>
    public static int RowCount(IReadOnlyList<BentoSlot> slots) => slots.Count == 0 ? 0 : slots.Max(s => s.Row + s.RowSpan);
}
