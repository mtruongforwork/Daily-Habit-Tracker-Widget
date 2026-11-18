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
    [InlineData("49.000 ₫", "199.000 ₫", 66)]
    public void YearlySavePercent_ComesFromTheRealPrices(string monthly, string yearly, int expected) =>
        PlusPricing.YearlySavePercent(monthly, yearly).Should().Be(expected);

    [Theory]
    [InlineData(1u, "Week", 7)]
    [InlineData(7u, "Day", 7)]
    [InlineData(1u, "Month", 30)]
    [InlineData(1u, "Year", 365)]
    public void TrialDays_ConvertsStoreTrialPeriods(uint length, string unit, int days) =>
        PlusPromo.TrialDays(length, unit).Should().Be(days);

    [Fact]
    public void TrialDays_IsUnknownForMinutesOrHours() => PlusPromo.TrialDays(3, "Hour").Should().BeNull();

    [Fact]
    public void YearlySavePercent_IsUnknownWhenAPriceCannotBeRead() =>
        PlusPricing.YearlySavePercent("—", "$9.99").Should().BeNull();
}
