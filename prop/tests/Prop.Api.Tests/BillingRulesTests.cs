using System.Globalization;

using Prop.Api.Billing;

namespace Prop.Api.Tests;

/// <summary>The arithmetic of billing: graduated slot prices, parts of months and the lines of each charge.</summary>
public sealed class BillingRulesTests
{
    private static readonly BillingTerms Terms = new("USD", 500m, [new SlotPrice(1, 5m), new SlotPrice(101, 4.5m), new SlotPrice(501, 4m)], 10, 10_000, 5);

    // Monday 5 October 2026: 27 of October's 31 days are left, counting the day itself.
    private static readonly DateTimeOffset October5 = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    // Wednesday 28 October 2026: November is already due, since it is charged from 27 October.
    private static readonly DateTimeOffset October28 = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "0")]
    [InlineData(50, "250")]
    [InlineData(100, "500")]
    [InlineData(101, "504.5")]
    [InlineData(600, "2700")]
    public void EachSlotCostsThePriceOfItsTier(int slots, string price)
    {
        Assert.Equal(decimal.Parse(price, CultureInfo.InvariantCulture), BillingRules.MonthlyPrice(slots, Terms.SlotPrices));
    }

    [Fact]
    public void MoreSlotsNeverCostLess()
    {
        for (var slots = 1; slots <= 1_000; slots++)
        {
            Assert.True(BillingRules.MonthlyPrice(slots, Terms.SlotPrices) > BillingRules.MonthlyPrice(slots - 1, Terms.SlotPrices));
        }
    }

    [Fact]
    public void APartOfAMonthIsPaidForTheDaysLeftCountingTheDayItself()
    {
        Assert.Equal((27, 31), BillingRules.DaysLeft(October5));
        Assert.Equal(217.74m, BillingRules.ForRestOfMonth(250m, October5));
        Assert.Equal(8.06m, BillingRules.ForRestOfMonth(250m, new DateTimeOffset(2026, 10, 31, 23, 0, 0, TimeSpan.Zero)));
        Assert.Equal(250m, BillingRules.ForRestOfMonth(250m, new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void AMonthIsChargedTheConfiguredDaysBeforeItStarts()
    {
        var november = new DateOnly(2026, 11, 1);

        Assert.Equal(new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero), BillingRules.ChargeTimeOf(november, 5));
        Assert.False(BillingRules.IsNextMonthDue(new DateTimeOffset(2026, 10, 26, 23, 59, 59, TimeSpan.Zero), Terms));
        Assert.True(BillingRules.IsNextMonthDue(new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero), Terms));
        Assert.Equal(new DateOnly(2026, 10, 1), BillingRules.MonthOf(October28));
    }

    [Fact]
    public void TheFirstPaymentIsTheStartupFeeAndTheRestOfTheMonth()
    {
        Assert.Equal(
            [new ChargeLine("Startup fee", 1, 500m), new ChargeLine("50 slots, October 2026 (27 of 31 days)", 50, 217.74m)],
            BillingRules.Activation(October5, 50, Terms));
    }

    [Fact]
    public void TheFirstPaymentAlsoPaysTheNextMonthWhenItIsDue()
    {
        Assert.Equal(
            [
                new ChargeLine("Startup fee", 1, 500m),
                new ChargeLine("50 slots, October 2026 (4 of 31 days)", 50, 32.26m),
                new ChargeLine("50 slots, November 2026", 50, 250m),
            ],
            BillingRules.Activation(October28, 50, Terms));
    }

    [Fact]
    public void WithoutAStartupFeeOnlyTheSlotsArePaid()
    {
        Assert.Equal([new ChargeLine("50 slots, October 2026 (27 of 31 days)", 50, 217.74m)], BillingRules.Activation(October5, 50, Terms with { StartupFee = 0m }));
    }

    [Fact]
    public void MoreSlotsPayTheDifferenceForTheRestOfTheMonth()
    {
        Assert.Equal([new ChargeLine("10 more slots, October 2026 (27 of 31 days)", 10, 43.55m)], BillingRules.MoreSlots(October5, 50, 60, null, Terms));
    }

    [Fact]
    public void MoreSlotsAlsoPayTheNextMonthWhenItIsPaidWithFewer()
    {
        Assert.Equal(
            [new ChargeLine("10 more slots, October 2026 (4 of 31 days)", 10, 6.45m), new ChargeLine("10 more slots, November 2026", 10, 50m)],
            BillingRules.MoreSlots(October28, 50, 60, 50, Terms));
        Assert.Single(BillingRules.MoreSlots(October28, 50, 60, 70, Terms));
    }

    [Fact]
    public void AMonthsSlotsAreOneLine()
    {
        Assert.Equal([new ChargeLine("120 slots, November 2026", 120, 590m)], BillingRules.Renewal(new DateOnly(2026, 11, 1), 120, Terms));
    }

    [Fact]
    public void TheExampleTermsWork()
    {
        Assert.Empty(Terms.Problems());
    }

    [Theory]
    [InlineData("currency")]
    [InlineData("startup fee")]
    [InlineData("no prices")]
    [InlineData("first tier")]
    [InlineData("falling tiers")]
    [InlineData("fractions of cents")]
    [InlineData("min slots")]
    [InlineData("charge days")]
    public void InvalidTermsAreFound(string problem)
    {
        var terms = problem switch
        {
            "currency" => Terms with { Currency = "usd" },
            "startup fee" => Terms with { StartupFee = -1m },
            "no prices" => Terms with { SlotPrices = [] },
            "first tier" => Terms with { SlotPrices = [new SlotPrice(2, 5m)] },
            "falling tiers" => Terms with { SlotPrices = [new SlotPrice(1, 5m), new SlotPrice(1, 4m)] },
            "fractions of cents" => Terms with { SlotPrices = [new SlotPrice(1, 4.999m)] },
            "min slots" => Terms with { MinSlots = 0 },
            "charge days" => Terms with { ChargeDaysBeforeMonth = 28 },
            _ => throw new ArgumentOutOfRangeException(nameof(problem)),
        };

        Assert.NotEmpty(terms.Problems());
    }
}
