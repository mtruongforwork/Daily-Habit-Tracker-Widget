using FluentAssertions;
using HabitGrid.Core.Domain.Widgets;
using Xunit;

namespace HabitGrid.Core.Tests.Widgets;

public class ScreenLayoutTests
{
    private static readonly ScreenLayout.Monitor Main = new(@"\\.\DISPLAY1", 0, 0, 1920, 1080);
    private static readonly ScreenLayout.Monitor Side = new(@"\\.\DISPLAY2", 1920, 0, 2560, 1440);

    [Fact]
    public void Signature_IgnoresTheOrderMonitorsAreListed()
    {
        ScreenLayout.Signature([Main, Side]).Should().Be(ScreenLayout.Signature([Side, Main]));
    }

    [Fact]
    public void Signature_ChangesWhenAMonitorIsUnplugged_OrResolutionChanges()
    {
        var dual = ScreenLayout.Signature([Main, Side]);

        ScreenLayout.Signature([Main]).Should().NotBe(dual, "rút màn hình phụ");
        ScreenLayout.Signature([Main with { Width = 1366, Height = 768 }, Side]).Should().NotBe(dual, "đổi độ phân giải");
        ScreenLayout.Signature([Main, Side with { Left = 0, Top = 1080 }]).Should().NotBe(dual, "đổi vị trí tương đối");
    }

    [Fact]
    public void Signature_ReturnsToTheSameValue_WhenTheMonitorIsPluggedBackIn()
    {
        var before = ScreenLayout.Signature([Main, Side]);
        _ = ScreenLayout.Signature([Main]);

        ScreenLayout.Signature([Main, Side]).Should().Be(before, "cắm lại thì về đúng vị trí đã nhớ");
    }

    [Fact]
    public void Signature_IsShort_AndCaseInsensitiveOnTheDevicePath()
    {
        var a = ScreenLayout.Signature([Main]);
        var b = ScreenLayout.Signature([Main with { Device = @"\\.\display1" }]);

        a.Should().HaveLength(8).And.Be(b);
        ScreenLayout.KeyFor("widget.pos.buddy", a).Should().Be($"widget.pos.buddy@{a}");
    }
}
