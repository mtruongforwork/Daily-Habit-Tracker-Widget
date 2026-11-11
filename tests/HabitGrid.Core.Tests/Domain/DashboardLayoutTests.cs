using FluentAssertions;
using HabitGrid.Core.Domain.Dashboard;
using Xunit;

namespace HabitGrid.Core.Tests.Domain;

public class DashboardLayoutTests
{
    [Fact]
    public void Default_ShowsEverythingInTheOriginalOrder()
    {
        var layout = DashboardLayout.Default();

        layout.VisibleIds.Should().Equal(DashboardSections.Default);
        layout.Serialize().Should().Be("week,mood,recap,routines,challenges,hero");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyGivesTheDefault(string? text)
    {
        DashboardLayout.Parse(text).Items.Should().Equal(DashboardLayout.Default().Items);
    }

    [Fact]
    public void RoundTrip_KeepsOrderAndHiddenState()
    {
        var layout = DashboardLayout.Default().Move("hero", -5).WithVisible("mood", false);

        var again = DashboardLayout.Parse(layout.Serialize());

        again.Items.Should().Equal(layout.Items);
        again.VisibleIds.First().Should().Be("hero");
        again.IsVisible("mood").Should().BeFalse();
    }

    [Fact]
    public void Parse_DropsUnknownAndDuplicates_AndAppendsMissing()
    {
        var layout = DashboardLayout.Parse("hero,bogus,hero,-week,routines");

        layout.Items.Select(i => i.Id).Should().Equal("hero", "week", "routines", "mood", "recap", "challenges");
        layout.IsVisible("week").Should().BeFalse();
        layout.IsVisible("mood").Should().BeTrue("khối thiếu được thêm lại ở dạng hiện");
    }

    [Fact]
    public void Move_ClampsAtTheEnds_AndIgnoresUnknownIds()
    {
        var layout = DashboardLayout.Default();

        layout.Move("week", -1).Items.Should().Equal(layout.Items);
        layout.Move("hero", 1).Items.Should().Equal(layout.Items);
        layout.Move("nope", 1).Should().BeSameAs(layout);
        layout.Move("week", 99).Items.Last().Id.Should().Be("week");
        layout.Move("mood", 1).Items.Select(i => i.Id).Take(3).Should().Equal("week", "recap", "mood");
    }

    [Fact]
    public void Reorder_AppliesTheDraggedOrder_KeepsVisibility_AndToleratesBadInput()
    {
        var layout = DashboardLayout.Default().WithVisible("mood", false);

        var result = layout.Reorder(["hero", "mood", "bogus", "hero", "week"]);

        result.Items.Select(i => i.Id).Should().Equal("hero", "mood", "week", "recap", "routines", "challenges");
        result.IsVisible("mood").Should().BeFalse();
        result.IsVisible("hero").Should().BeTrue();
    }

    [Fact]
    public void Layout_IsImmutable()
    {
        var original = DashboardLayout.Default();

        _ = original.WithVisible("hero", false);
        _ = original.Move("week", 3);

        original.Serialize().Should().Be("week,mood,recap,routines,challenges,hero");
    }
}
