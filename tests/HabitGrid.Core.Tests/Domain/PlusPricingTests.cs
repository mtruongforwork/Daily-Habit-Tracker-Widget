using FluentAssertions;
using HabitGrid.Core.Domain.Monetization;
using Xunit;

namespace HabitGrid.Core.Tests.Domain;

public class PlusPricingTests
{
    [Theory]
    [InlineData("39.000 ₫", 39000)]
    [InlineData("1.249.000 ₫", 1249000)]
    [InlineData("$1.99", 1.99)]
    [InlineData("US$ 19.99", 19.99)]
    [InlineData("9,99 €", 9.99)]
    [InlineData("1.234,50 €", 1234.50)]
    [InlineData("$1,234.50", 1234.50)]
    [InlineData("¥1,500", 1500)]
    public void TryParseAmount_ReadsLocalisedPrices(string price, double expected)
    {
        PlusPricing.TryParseAmount(price, out var amount).Should().BeTrue();
        amount.Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Free")]
    [InlineData(null)]
    public void TryParseAmount_RejectsNonPrices(string? price) => PlusPricing.TryParseAmount(price, out _).Should().BeFalse();

    [Theory]
    [InlineData("39.000 ₫", "249.000 ₫", 47)]
    [InlineData("29.000 ₫", "149.000 ₫", 57)]
    [InlineData("$1.99", "$9.99", 58)]
    [InlineData("$2.99", "$19.99", 44)]
    public void YearlySavePercent_ComesFromTheRealPrices(string monthly, string yearly, int expected) =>
        PlusPricing.YearlySavePercent(monthly, yearly).Should().Be(expected);

    [Fact]
    public void YearlySavePercent_IsUnknownWhenAPriceCannotBeRead() =>
        PlusPricing.YearlySavePercent("—", "$9.99").Should().BeNull();
}
