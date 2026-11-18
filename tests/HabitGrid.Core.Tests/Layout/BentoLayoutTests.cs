using FluentAssertions;
using HabitGrid.Core.Domain.Layout;
using Xunit;

namespace HabitGrid.Core.Tests.Layout;

public class BentoLayoutTests
{
    [Theory]
    [InlineData(1920, 8)]
    [InlineData(1100, 8)]
    [InlineData(1099, 4)]
    [InlineData(720, 4)]
    [InlineData(719, 2)]
    public void TracksFor_FollowsTheBreakpoints(double width, int tracks) => BentoLayout.TracksFor(width).Should().Be(tracks);

    [Fact]
    public void Arrange_FillsRowsLeftToRight()
    {
        // Trang Hôm nay, hàng 1: lịch tuần 3 cột + tâm trạng 1 cột (6 + 2 rãnh).
        var slots = BentoLayout.Arrange([new(6), new(2), new(4), new(2), new(2)], 8);

        slots.Should().Equal(new BentoSlot(0, 0, 6, 1), new BentoSlot(6, 0, 2, 1), new BentoSlot(0, 1, 4, 1), new BentoSlot(4, 1, 2, 1), new BentoSlot(6, 1, 2, 1));
    }

    [Fact]
    public void Arrange_SupportsOneAndAHalfColumns()
    {
        // Trang Lịch: lịch tháng 2.5 cột × 2 hàng (5 rãnh), checklist 1.5 cột và tổng kết 1.5 cột xếp chồng bên phải.
        var slots = BentoLayout.Arrange([new(5, 2), new(3), new(3)], 8);

        slots.Should().Equal(new BentoSlot(0, 0, 5, 2), new BentoSlot(5, 0, 3, 1), new BentoSlot(5, 1, 3, 1));
        BentoLayout.RowCount(slots).Should().Be(2);
    }

    [Fact]
    public void Arrange_FillsHolesLeftByTallItems()
    {
        var slots = BentoLayout.Arrange([new(2, 2), new(6), new(4), new(2)], 8);

        slots[2].Should().Be(new BentoSlot(2, 1, 4, 1));
        slots[3].Should().Be(new BentoSlot(6, 1, 2, 1));
    }

    [Fact]
    public void Arrange_ClampsWideItemsOnNarrowGrids()
    {
        var slots = BentoLayout.Arrange([new(6), new(2)], 2);

        slots.Should().Equal(new BentoSlot(0, 0, 2, 1), new BentoSlot(0, 1, 2, 1));
    }

    [Fact]
    public void Arrange_NeverOverlaps()
    {
        var random = new Random(7);
        var items = Enumerable.Range(0, 40).Select(_ => new BentoItem(random.Next(1, 9), random.Next(1, 3))).ToList();

        foreach (var tracks in new[] { 8, 4, 2 })
        {
            var cells = new HashSet<(int, int)>();

            foreach (var slot in BentoLayout.Arrange(items, tracks))
            {
                (slot.Column + slot.Span).Should().BeLessThanOrEqualTo(tracks);

                for (var r = slot.Row; r < slot.Row + slot.RowSpan; r++)
                {
                    for (var c = slot.Column; c < slot.Column + slot.Span; c++)
                    {
                        cells.Add((r, c)).Should().BeTrue("hai ô không được chồng lên nhau");
                    }
                }
            }
        }
    }
}
