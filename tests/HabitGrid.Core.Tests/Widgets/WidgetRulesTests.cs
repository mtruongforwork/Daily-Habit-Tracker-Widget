using FluentAssertions;
using HabitGrid.Core.Domain.Widgets;
using Xunit;

namespace HabitGrid.Core.Tests.Widgets;

public class WidgetRulesTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 3)]
    public void GridLimit_FollowsThePlan(bool plus, int expected) => WidgetRules.MaxGrids(plus).Should().Be(expected);

    [Theory]
    [InlineData(ExtraWidget.Buddy)]
    [InlineData(ExtraWidget.Goals)]
    [InlineData(ExtraWidget.MultiGrid)]
    public void ExtraWidgets_ArePlusOnly_AndOffByDefault(ExtraWidget widget)
    {
        WidgetRules.CanUse(widget, false).Should().BeFalse();
        WidgetRules.CanUse(widget, true).Should().BeTrue();
        WidgetRules.ShouldShow(widget, true, null).Should().BeFalse("mặc định tắt");
        WidgetRules.ShouldShow(widget, true, "0").Should().BeFalse();
        WidgetRules.ShouldShow(widget, true, "1").Should().BeTrue();
        WidgetRules.ShouldShow(widget, false, "1").Should().BeFalse("hết Plus thì ẩn, cấu hình vẫn giữ");
    }

    [Fact]
    public void Keys_AreDistinct_AndStable()
    {
        var all = Enum.GetValues<ExtraWidget>();
        all.Select(WidgetRules.EnabledKey).Should().OnlyHaveUniqueItems();
        all.Select(WidgetRules.PositionKey).Should().OnlyHaveUniqueItems();
        WidgetRules.EnabledKey(ExtraWidget.MultiGrid).Should().Be("widgets.multigrid.enabled");
        WidgetRules.PositionKey(ExtraWidget.Buddy).Should().Be("widget.pos.buddy");
    }

    [Fact]
    public void TotalBudget_IsSixWidgets_ForPlus() =>
        (WidgetRules.PlusMaxGrids + Enum.GetValues<ExtraWidget>().Length).Should().Be(6);
}
