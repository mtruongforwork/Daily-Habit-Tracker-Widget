using FluentAssertions;
using HabitGrid.Core.Domain.Widgets;
using Xunit;

namespace HabitGrid.Core.Tests.Widgets;

public class WidgetLayoutTests
{
    private static bool Overlap(WidgetLayout.Item a, WidgetLayout.Slot sa, WidgetLayout.Item b, WidgetLayout.Slot sb) =>
        sa.Left < sb.Left + b.Width && sb.Left < sa.Left + a.Width && sa.Top < sb.Top + b.Height && sb.Top < sa.Top + a.Height;

    [Theory]
    [InlineData(null, PinChoice.Top)]
    [InlineData("", PinChoice.Top)]
    [InlineData("junk", PinChoice.Top)]
    [InlineData("above", PinChoice.AboveDesktop)]
    [InlineData(" DESKTOP ", PinChoice.Desktop)]
    public void Pin_ParsesWithASafeDefault(string? stored, PinChoice expected) => PinChoices.Parse(stored).Should().Be(expected);

    [Fact]
    public void Pin_RoundTrips()
    {
        foreach (var choice in Enum.GetValues<PinChoice>())
        {
            PinChoices.Parse(PinChoices.ToStored(choice)).Should().Be(choice);
        }
    }

    [Fact]
    public void FirstItem_SitsAtTheTopRightCorner_WithAMargin()
    {
        var plan = WidgetLayout.Plan(0, 0, 1920, 1040, [new("a", 100, 100)]);

        plan["a"].Should().Be(new WidgetLayout.Slot(1920 - WidgetLayout.Margin - 100, WidgetLayout.Margin));
    }

    [Fact]
    public void Items_StackDownTheColumn_WithoutOverlapping()
    {
        WidgetLayout.Item[] items = [new("a", 92, 92), new("b", 170, 46), new("c", 132, 74)];

        var plan = WidgetLayout.Plan(0, 0, 1920, 1040, items);

        plan["b"].Top.Should().Be(plan["a"].Top + 92 + WidgetLayout.Gap);
        for (var i = 0; i < items.Length; i++)
            for (var j = i + 1; j < items.Length; j++)
                Overlap(items[i], plan[items[i].Key], items[j], plan[items[j].Key]).Should().BeFalse($"{items[i].Key} và {items[j].Key} không được chồng nhau");
    }

    [Fact]
    public void WhenTheColumnIsFull_ItContinuesInANewColumnToTheLeft()
    {
        WidgetLayout.Item[] items = [new("a", 200, 500), new("b", 150, 500), new("c", 150, 500)];

        var plan = WidgetLayout.Plan(0, 0, 1920, 1040, items);

        plan["b"].Top.Should().Be(WidgetLayout.Margin, "cột mới bắt đầu từ trên");
        plan["b"].Left.Should().BeLessThan(plan["a"].Left, "cột mới nằm bên trái");
        plan["b"].Left.Should().Be(plan["a"].Left - WidgetLayout.Gap - 150 + (200 - 200), "sát cột trước chừa khe");
        Overlap(items[0], plan["a"], items[1], plan["b"]).Should().BeFalse();
    }

    [Fact]
    public void TinyScreens_NeverPlaceAWidgetOutsideTheLeftEdge()
    {
        var plan = WidgetLayout.Plan(0, 0, 150, 300, [new("a", 200, 100), new("b", 200, 100), new("c", 200, 100), new("d", 200, 100)]);

        plan.Values.Should().OnlyContain(s => s.Left >= 0);
    }

    [Fact]
    public void RespectsANonZeroWorkAreaOrigin()
    {
        var plan = WidgetLayout.Plan(1920, 0, 3840, 1040, [new("a", 100, 100)]);

        plan["a"].Left.Should().BeGreaterThan(1920).And.BeLessThan(3840);
    }
}

public class WidgetLayoutModeTests
{
    [Theory]
    [InlineData(null, WidgetLayoutMode.Hub)]
    [InlineData("", WidgetLayoutMode.Hub)]
    [InlineData("hub", WidgetLayoutMode.Hub)]
    [InlineData("junk", WidgetLayoutMode.Hub)]
    [InlineData("separate", WidgetLayoutMode.Separate)]
    [InlineData(" SEPARATE ", WidgetLayoutMode.Separate)]
    public void DefaultsToASingleHubWidget(string? stored, WidgetLayoutMode expected) =>
        WidgetLayoutModes.Parse(stored).Should().Be(expected);

    [Fact]
    public void ModeRoundTrips()
    {
        foreach (var mode in Enum.GetValues<WidgetLayoutMode>())
        {
            WidgetLayoutModes.Parse(WidgetLayoutModes.ToStored(mode)).Should().Be(mode);
        }
    }

    [Fact]
    public void FreeSeesBuddyAndToday_PlusSeesEverything()
    {
        WidgetLayoutModes.Sections(false).Should().Be(HubSection.Buddy | HubSection.Today);
        WidgetLayoutModes.Sections(true).Should().Be(HubSection.Buddy | HubSection.Today | HubSection.Goals | HubSection.Grid);
    }

    [Fact]
    public void TheHubIsSmall_AndFitsInTheCornerColumn()
    {
        WidgetLayout.HubWidth.Should().BeLessThan(260);
        WidgetLayout.HubHeight.Should().BeLessThan(90);
        WidgetLayout.Plan(0, 0, 1920, 1040, [new("hub", WidgetLayout.HubWidth, WidgetLayout.HubHeight)])["hub"]
            .Left.Should().Be(1920 - WidgetLayout.Margin - WidgetLayout.HubWidth);
    }
}
