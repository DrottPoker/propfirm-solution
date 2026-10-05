using System.Globalization;

using Prop.Api.Billing;

namespace Prop.Api.Tests;

/// <summary>The arithmetic of billing: the package, graduated prices beyond it, parts of months and the lines of each charge.</summary>
public sealed class BillingRulesTests
{
    private static readonly BillingTerms Terms = new("USD", 700m, 500m, 25, [new SlotPrice(26, 5m), new SlotPrice(101, 4m)], 10_000, 5, 200m);

    // Monday 5 October 2026: 27 of October's 31 days are left, counting the day itself.
    private static readonly DateTimeOffset October5 = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    // Wednesday 28 October 2026: November is already due, since it is charged from 27 October.
    private static readonly DateTimeOffset October28 = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1, "500")]
    [InlineData(25, "500")]
    [InlineData(26, "505")]
    [InlineData(100, "875")]
    [InlineData(101, "879")]
    [InlineData(600, "2875")]
    public void ThePackageIncludesItsSlotsAndEachSlotBeyondItCostsThePriceOfItsTier(int slots, string price)
    {
        Assert.Equal(decimal.Parse(price, CultureInfo.InvariantCulture), BillingRules.MonthlyPrice(slots, Terms));
    }

    [Fact]
    public void MoreSlotsBeyondThePackageNeverCostLess()
    {
        for (var slots = Terms.PackageSlots + 1; slots <= 1_000; slots++)
        {
            Assert.True(BillingRules.MonthlyPrice(slots, Terms) > BillingRules.MonthlyPrice(slots - 1, Terms));
        }
    }

    [Fact]
    public void AMonthIsChargedForAtLeastThePackageAndTheTakenSlots()
    {
        Assert.Equal(
            (25, 30, 25, 40),
            (Terms.SlotsToCharge(null, 3), Terms.SlotsToCharge(30, 3), Terms.SlotsToCharge(20, 3), Terms.SlotsToCharge(30, 40)));
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
    public void TheFirstPaymentIsTheStartupFeeAndThePackageAndExtraSlotsForTheRestOfTheMonth()
    {
        Assert.Equal(
            [
                new ChargeLine("Startup fee", 1, 700m),
                new ChargeLine("Package with 25 slots, October 2026 (27 of 31 days)", 25, 435.48m),
                new ChargeLine("25 extra slots, October 2026 (27 of 31 days)", 25, 108.87m),
            ],
            BillingRules.Activation(October5, 50, Terms));
    }

    [Fact]
    public void TheFirstPaymentAlsoPaysTheNextMonthWhenItIsDue()
    {
        Assert.Equal(
            [
                new ChargeLine("Startup fee", 1, 700m),
                new ChargeLine("Package with 25 slots, October 2026 (4 of 31 days)", 25, 64.52m),
                new ChargeLine("25 extra slots, October 2026 (4 of 31 days)", 25, 16.13m),
                new ChargeLine("Package with 25 slots, November 2026", 25, 500m),
                new ChargeLine("25 extra slots, November 2026", 25, 125m),
            ],
            BillingRules.Activation(October28, 50, Terms));
    }

    [Fact]
    public void WithoutAStartupFeeOnlyThePackageAndTheSlotsArePaid()
    {
        Assert.Equal(
            [
                new ChargeLine("Package with 25 slots, October 2026 (27 of 31 days)", 25, 435.48m),
                new ChargeLine("25 extra slots, October 2026 (27 of 31 days)", 25, 108.87m),
            ],
            BillingRules.Activation(October5, 50, Terms with { StartupFee = 0m }));
    }

    [Fact]
    public void TheDepositForTheReviewIsOneLine()
    {
        Assert.Equal([new ChargeLine("Review deposit, taken off the startup fee", 1, 200m)], BillingRules.Deposit(Terms));
    }

    [Fact]
    public void TheDepositPaidIsTakenOffTheStartupFee()
    {
        Assert.Equal(
            [
                new ChargeLine("Startup fee, less the deposit of 200.00 USD", 1, 500m),
                new ChargeLine("Package with 25 slots, October 2026 (27 of 31 days)", 25, 435.48m),
            ],
            BillingRules.Activation(October5, 25, Terms, depositPaid: 200m));
    }

    [Fact]
    public void ADepositAsLargeAsTheStartupFeeLeavesOnlyThePackage()
    {
        Assert.Equal(
            [new ChargeLine("Package with 25 slots, October 2026 (27 of 31 days)", 25, 435.48m)],
            BillingRules.Activation(October5, 25, Terms with { ReviewDeposit = 700m }, depositPaid: 700m));
    }

    [Fact]
    public void MoreSlotsPayTheDifferenceForTheRestOfTheMonth()
    {
        Assert.Equal([new ChargeLine("10 more slots, October 2026 (27 of 31 days)", 10, 43.55m)], BillingRules.MoreSlots(October5, 50, 60, null, Terms));
        Assert.Equal([new ChargeLine("1 more slot, October 2026 (27 of 31 days)", 1, 4.35m)], BillingRules.MoreSlots(October5, 50, 51, null, Terms));
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
    public void AMonthIsThePackageAndTheSlotsBeyondIt()
    {
        var november = new DateOnly(2026, 11, 1);

        Assert.Equal(
            [new ChargeLine("Package with 25 slots, November 2026", 25, 500m), new ChargeLine("95 extra slots, November 2026", 95, 455m)],
            BillingRules.Renewal(november, 120, Terms));
        Assert.Equal([new ChargeLine("Package with 25 slots, November 2026", 25, 500m)], BillingRules.Renewal(november, 25, Terms));
    }

    [Fact]
    public void TheExampleTermsWork()
    {
        Assert.Empty(Terms.Problems());
    }

    [Theory]
    [InlineData("currency")]
    [InlineData("startup fee")]
    [InlineData("package price")]
    [InlineData("package slots")]
    [InlineData("max slots")]
    [InlineData("no prices")]
    [InlineData("first tier")]
    [InlineData("falling tiers")]
    [InlineData("fractions of cents")]
    [InlineData("charge days")]
    [InlineData("deposit above the startup fee")]
    [InlineData("negative deposit")]
    public void InvalidTermsAreFound(string problem)
    {
        var terms = problem switch
        {
            "currency" => Terms with { Currency = "usd" },
            "startup fee" => Terms with { StartupFee = -1m },
            "package price" => Terms with { PackagePrice = 0m },
            "package slots" => Terms with { PackageSlots = 0, SlotPrices = [new SlotPrice(1, 5m)] },
            "max slots" => Terms with { MaxSlots = 24 },
            "no prices" => Terms with { SlotPrices = [] },
            "first tier" => Terms with { SlotPrices = [new SlotPrice(25, 5m)] },
            "falling tiers" => Terms with { SlotPrices = [new SlotPrice(26, 5m), new SlotPrice(26, 4m)] },
            "fractions of cents" => Terms with { SlotPrices = [new SlotPrice(26, 4.999m)] },
            "charge days" => Terms with { ChargeDaysBeforeMonth = 28 },
            "deposit above the startup fee" => Terms with { ReviewDeposit = 700.01m },
            "negative deposit" => Terms with { ReviewDeposit = -1m },
            _ => throw new ArgumentOutOfRangeException(nameof(problem)),
        };

        Assert.NotEmpty(terms.Problems());
    }

    [Fact]
    public void BuiltInIdChecksCostTheMonthsPriceAndTheChecksBeyondThoseIncluded()
    {
        var prices = new Configuration.IdentityCheckPriceOptions();
        var november = new DateOnly(2026, 11, 1);

        var within = BillingRules.IdentityChecks(november, monthly: true, checks: 25, address: 0, sanctions: 0, prices);
        var beyond = BillingRules.IdentityChecks(november, monthly: true, checks: 37, address: 3, sanctions: 1, prices);
        var turnedOff = BillingRules.IdentityChecks(november, monthly: false, checks: 4, address: 0, sanctions: 0, prices);
        var nothing = BillingRules.IdentityChecks(november, monthly: false, checks: 0, address: 0, sanctions: 0, prices);

        Assert.Equal([new ChargeLine("KYC, November 2026, 25 checks included", 1, 15m)], within);
        Assert.Equal(
            [
                new ChargeLine("KYC, November 2026, 25 checks included", 1, 15m),
                new ChargeLine("12 KYC checks beyond the 25 included", 12, 9.6m),
                new ChargeLine("3 proof of address checks", 3, 0.9m),
                new ChargeLine("1 sanctions screening", 1, 0.3m),
            ],
            beyond);
        Assert.Empty(turnedOff);
        Assert.Empty(nothing);
    }
}
