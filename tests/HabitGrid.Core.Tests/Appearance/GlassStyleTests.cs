using FluentAssertions;
using HabitGrid.Core.Domain;
using Xunit;

namespace HabitGrid.Core.Tests.Appearance;

public class GlassStyleTests
{
    [Theory]
    [InlineData(null, 65)]
    [InlineData("", 65)]
    [InlineData("abc", 65)]
    [InlineData("0", 0)]
    [InlineData("100", 100)]
    [InlineData("250", 100)]
    [InlineData("-5", 0)]
    [InlineData("40", 40)]
    public void Parse_ClampsAndFallsBack(string? stored, int expected) => GlassStyle.Parse(stored).Should().Be(expected);

    [Fact]
    public void ZeroStrength_IsFullyOpaque_BothThemes()
    {
        GlassStyle.CanvasAlpha(true, 0).Should().Be(255);
        GlassStyle.CanvasAlpha(false, 0).Should().Be(255);
    }

    [Fact]
    public void MoreGlass_MeansLessOpaque_ButNeverTransparent()
    {
        var alphas = new[] { 0, 25, 50, 75, 100 }.Select(s => (int)GlassStyle.CanvasAlpha(true, s)).ToList();

        alphas.Should().BeInDescendingOrder().And.OnlyHaveUniqueItems();
        GlassStyle.CanvasAlpha(true, 100).Should().BeGreaterThan(60, "tối vẫn phải đủ đục để chữ đọc được");
        GlassStyle.CanvasAlpha(false, 100).Should().BeGreaterThan(GlassStyle.CanvasAlpha(true, 100), "bản sáng giữ đục hơn để chữ tối đọc được");
    }

    [Fact]
    public void OutOfRangeStrength_IsClamped() =>
        GlassStyle.CanvasAlpha(true, 999).Should().Be(GlassStyle.CanvasAlpha(true, 100));
}
