using FluentAssertions;
using HabitGrid.Core.Domain.Widgets;
using Xunit;

namespace HabitGrid.Core.Tests.Widgets;

public class IslandPlacementTests
{
    [Theory]
    [InlineData(null, WidgetPlacementMode.KeepPosition)]
    [InlineData("", WidgetPlacementMode.KeepPosition)]
    [InlineData("keep", WidgetPlacementMode.KeepPosition)]
    [InlineData("garbage", WidgetPlacementMode.KeepPosition)]
    [InlineData("topcenter", WidgetPlacementMode.TopCenter)]
    [InlineData(" TopCenter ", WidgetPlacementMode.TopCenter)]
    public void Parse_IsTolerant_AndDefaultsToKeepPosition(string? stored, WidgetPlacementMode expected)
    {
        WidgetPlacements.Parse(stored).Should().Be(expected);
    }

    [Fact]
    public void ToStored_RoundTrips()
    {
        foreach (var mode in Enum.GetValues<WidgetPlacementMode>())
        {
            WidgetPlacements.Parse(WidgetPlacements.ToStored(mode)).Should().Be(mode);
        }
    }

    [Fact]
    public void TopCenter_CentresOnASingleMonitor_SixPixelsBelowTheTopEdge()
    {
        var slot = WidgetLayout.TopCenter(0, 0, 1920, 156);

        slot.Left.Should().Be(882);
        slot.Top.Should().Be(WidgetLayout.IslandTopGap).And.Be(6);
        (slot.Left + (156 / 2)).Should().Be(960, "tâm viên thuốc trùng tâm màn hình");
    }

    [Fact]
    public void TopCenter_WorksForAnOffsetPrimaryMonitor()
    {
        // Màn hình chính nằm lệch (có màn phụ bên trái nên gốc không phải 0,0) và màn phụ nằm trên có toạ độ âm.
        var onSecondRight = WidgetLayout.TopCenter(1920, 0, 2560, 156);
        onSecondRight.Left.Should().Be(1920 + ((2560 - 156) / 2));

        var negative = WidgetLayout.TopCenter(-1920, -1080, 1920, 156);
        negative.Left.Should().Be(-1920 + ((1920 - 156) / 2));
        negative.Top.Should().Be(-1080 + 6);
    }

    [Theory]
    [InlineData(1280, 156)]
    [InlineData(1920, 156)]
    [InlineData(3840, 132)]
    [InlineData(3840, 340)]
    public void TopCenter_IsSymmetric_ForAnyWidthAndResolution(int monitorWidth, int width)
    {
        var slot = WidgetLayout.TopCenter(0, 0, monitorWidth, width);

        (slot.Left * 2) .Should().BeInRange(monitorWidth - width - 1, monitorWidth - width + 1);
    }

    [Fact]
    public void IslandRest_IsSmallerThanTheNormalPill()
    {
        WidgetLayout.IslandRestWidth.Should().BeLessThan(WidgetLayout.HubWidth);
        WidgetLayout.IslandRestHeight.Should().BeLessThan(WidgetLayout.HubHeight);
        WidgetLayout.IslandFocusWidth.Should().BeLessThanOrEqualTo(WidgetLayout.IslandRestWidth);
        WidgetLayout.IslandListWidth.Should().BeLessThanOrEqualTo(WidgetLayout.IslandPanelWidth);
    }
}
