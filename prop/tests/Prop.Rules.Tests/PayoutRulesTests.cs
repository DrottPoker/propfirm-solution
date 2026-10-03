using Prop.Rules.Tests.Support;

using static Prop.Rules.Tests.Support.ChallengeDriver;

namespace Prop.Rules.Tests;

/// <summary>The funded trader's payouts with the two-step template: 80 % of the profit, 5 trading days between payouts.</summary>
public sealed class PayoutRulesTests
{
    private static readonly DateOnly FundedMonday = Monday.AddDays(14);

    // Outputs are compared without their times.
    private static DateTimeOffset T => default;

    [Fact]
    public void TheTraderGetsTheirShareAndTheWholeProfitIsWithdrawn()
    {
        var driver = FundedWithProfit(108_000m);

        var quote = ChallengeRules.QuotePayout(driver.State);
        var outputs = driver.RequestPayout();

        Assert.Equal(new PayoutQuote(8_000m, 80m, 6_400m, 5, 5, null), quote);
        var requested = Assert.IsType<PayoutRequested>(outputs[0]).Payout;
        Assert.Equal(new Payout("P1", "A3", 8_000m, 80m, 6_400m, PayoutStatus.Withdrawing, requested.RequestedAt), requested);
        Assert.Equal(new WithdrawalRequested(T, "A3", "P1", 8_000m, 100_000m), WithoutTime(outputs)[1]);
        Assert.Equal(requested, driver.State.Payout);
    }

    [Fact]
    public void TheTradersShareIsRoundedDownToWholeCents()
    {
        var driver = FundedWithProfit(101_234.57m);

        Assert.Equal(987.65m, ChallengeRules.QuotePayout(driver.State).Amount);
    }

    [Theory]
    [InlineData("100000", 0, 5, "There is no profit to pay out.")]
    [InlineData("100000.01", 0, 5, "There is no profit to pay out.")]
    [InlineData("108000", 1, 5, "Close every position before asking for a payout.")]
    [InlineData("108000", 0, 4, "A payout needs 5 trading days since the last one. So far: 4.")]
    public void APayoutNeedsProfitNoOpenPositionsAndEnoughTradingDays(string balance, int openPositions, int days, string refusal)
    {
        var driver = new ChallengeDriver();
        driver.Fund();
        driver.TradeOnDays(FundedMonday, days);
        driver.Update(decimal.Parse(balance, System.Globalization.CultureInfo.InvariantCulture), openPositions);

        var outputs = driver.RequestPayout();

        Assert.Equal(refusal, ChallengeRules.QuotePayout(driver.State).Refusal);
        Assert.Equal([new InputIgnored(T, nameof(RequestPayout), refusal)], WithoutTime(outputs));
        Assert.Null(driver.State.Payout);
    }

    [Fact]
    public void ANewlyOpenedPositionBlocksAPayoutUntilItIsClosed()
    {
        var driver = FundedWithProfit(108_000m);

        driver.OpenPosition(FundedMonday.AddDays(4));
        var whileOpen = ChallengeRules.QuotePayout(driver.State).Refusal;
        driver.Update(108_000m, openPositions: 0);

        Assert.Equal("Close every position before asking for a payout.", whileOpen);
        Assert.True(ChallengeRules.QuotePayout(driver.State).CanRequest);
    }

    [Fact]
    public void OnlyAnActiveFundedAccountHasPayouts()
    {
        var evaluation = new ChallengeDriver();
        evaluation.OpenAccount("A1", Monday);
        evaluation.Update(108_000m);
        var failed = FundedWithProfit(108_000m);
        failed.Breach(FloorIds.Daily, 103_000m, 102_990m);

        Assert.Equal("Payouts are only for an active funded account.", ChallengeRules.QuotePayout(evaluation.State).Refusal);
        Assert.Equal("Payouts are only for an active funded account.", ChallengeRules.QuotePayout(failed.State).Refusal);
    }

    [Fact]
    public void AChallengeBoughtBeforeProfitSplitsHasNoPayouts()
    {
        var template = ChallengeTemplates.TwoStep("old", 100_000m);
        var driver = FundedWithProfit(108_000m);
        var old = driver.State with { Definition = template with { Funded = template.Funded with { ProfitSplitPercent = null } } };

        Assert.Equal("The challenge has no profit split, so it has no payouts.", ChallengeRules.QuotePayout(old).Refusal);
    }

    [Fact]
    public void OnlyOnePayoutIsInProgressAtATime()
    {
        var driver = FundedWithProfit(108_000m);
        driver.RequestPayout("P1");

        var second = driver.RequestPayout("P2");

        Assert.Equal([new InputIgnored(T, nameof(RequestPayout), "A payout is already in progress.")], WithoutTime(second));
        Assert.Equal("P1", driver.State.Payout!.Id);
    }

    [Fact]
    public void TheWithdrawalStartsANewPayoutPeriod()
    {
        var driver = FundedWithProfit(108_000m);
        driver.RequestPayout();

        var outputs = driver.BalanceAdjusted("P1", -8_000m, 100_000m);

        var withdrawn = Assert.IsType<PayoutWithdrawn>(Assert.Single(outputs));
        Assert.Equal((PayoutStatus.Pending, 100_000m), (withdrawn.Payout.Status, withdrawn.BalanceAfter));
        Assert.Equal(PayoutStatus.Pending, driver.State.Payout!.Status);
        Assert.Equal(new AccountFigures(100_000m, 0), driver.State.Account);
        Assert.Empty(driver.State.TradingDays);
    }

    [Fact]
    public void TheFirmApprovesAndThenMarksThePayoutAsPaid()
    {
        var driver = WithdrawnPayout();

        var approved = driver.Apply(new ApprovePayout(driver.NextTime(), "P1"));
        var paid = driver.Apply(new MarkPayoutPaid(driver.NextTime(), "P1", "bank-transfer-17"));

        Assert.Equal(PayoutStatus.Approved, Assert.IsType<PayoutApproved>(Assert.Single(approved)).Payout.Status);
        var marked = Assert.IsType<PayoutPaid>(Assert.Single(paid));
        Assert.Equal((PayoutStatus.Paid, 6_400m, "bank-transfer-17"), (marked.Payout.Status, marked.Payout.Amount, marked.Reference));
        Assert.Null(driver.State.Payout);
    }

    [Fact]
    public void APayoutIsMarkedAsPaidOnlyAfterApproval()
    {
        var driver = WithdrawnPayout();

        var outputs = driver.Apply(new MarkPayoutPaid(driver.NextTime(), "P1", null));

        Assert.Equal([new InputIgnored(T, nameof(MarkPayoutPaid), "Only an approved payout can be marked as paid.")], WithoutTime(outputs));
        Assert.Equal(PayoutStatus.Pending, driver.State.Payout!.Status);
    }

    [Fact]
    public void ThePayoutsIdMustMatch()
    {
        var driver = WithdrawnPayout();

        var outputs = driver.Apply(new ApprovePayout(driver.NextTime(), "P2"));

        Assert.Equal([new InputIgnored(T, nameof(ApprovePayout), "The payout is not waiting for approval.")], WithoutTime(outputs));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheFirmRejectsAPendingOrApprovedPayoutAndTheProfitStaysOff(bool approvedFirst)
    {
        var driver = WithdrawnPayout();
        if (approvedFirst)
        {
            driver.Apply(new ApprovePayout(driver.NextTime(), "P1"));
        }

        var outputs = driver.Apply(new RejectPayout(driver.NextTime(), "P1", "Copy trading is not allowed."));

        var rejected = Assert.IsType<PayoutRejected>(Assert.Single(outputs));
        Assert.Equal((PayoutStatus.Rejected, "Copy trading is not allowed."), (rejected.Payout.Status, rejected.Reason));
        Assert.Null(driver.State.Payout);
        Assert.Equal(100_000m, driver.State.Account!.Balance);
    }

    [Fact]
    public void APayoutCannotBeRejectedWhileItsProfitIsWithdrawn()
    {
        var driver = FundedWithProfit(108_000m);
        driver.RequestPayout();

        var outputs = driver.Apply(new RejectPayout(driver.NextTime(), "P1", "No"));

        Assert.Equal([new InputIgnored(T, nameof(RejectPayout), "The payout is not waiting for the firm.")], WithoutTime(outputs));
    }

    [Fact]
    public void ARefusedWithdrawalFailsThePayoutAndLeavesTheAccountAsItWas()
    {
        var driver = FundedWithProfit(108_000m);
        driver.RequestPayout();

        var outputs = driver.Apply(new WithdrawalRejected(driver.NextTime(), "P1", "InsufficientFunds"));
        var unrelated = driver.Apply(new WithdrawalRejected(driver.NextTime(), "P1", "InsufficientFunds"));

        var failed = Assert.IsType<PayoutFailed>(Assert.Single(outputs));
        Assert.Equal((PayoutStatus.Failed, "InsufficientFunds"), (failed.Payout.Status, failed.Reason));
        Assert.Empty(unrelated);
        Assert.Null(driver.State.Payout);
        Assert.Equal(5, driver.State.TradingDays.Count);
        Assert.True(ChallengeRules.QuotePayout(driver.State).CanRequest);
    }

    [Fact]
    public void TheNextPayoutNeedsNewProfitAndNewTradingDays()
    {
        var driver = WithdrawnPayout();
        driver.Apply(new ApprovePayout(driver.NextTime(), "P1"));
        driver.Apply(new MarkPayoutPaid(driver.NextTime(), "P1", null));
        driver.TradeOnDays(FundedMonday.AddDays(7), 3);
        driver.Update(102_000m);

        Assert.Equal("A payout needs 5 trading days since the last one. So far: 3.", ChallengeRules.QuotePayout(driver.State).Refusal);

        driver.TradeOnDays(FundedMonday.AddDays(10), 2);
        driver.Update(102_000m);
        Assert.Equal(new PayoutQuote(2_000m, 80m, 1_600m, 5, 5, null), ChallengeRules.QuotePayout(driver.State));
    }

    [Fact]
    public void AWithdrawalCountsEvenIfTheFirmCancelledInBetween()
    {
        var driver = FundedWithProfit(108_000m);
        driver.RequestPayout();
        driver.Apply(new CancelChallenge(driver.NextTime(), "Closing the firm"));

        var outputs = driver.Apply(new BalanceAdjusted(driver.NextTime(), "A3", driver.NextSequence(), "P1", -8_000m, 100_000m));

        Assert.IsType<PayoutWithdrawn>(Assert.Single(outputs));
        Assert.Equal((ChallengeStatus.Cancelled, PayoutStatus.Pending), (driver.State.Status, driver.State.Payout!.Status));
    }

    [Fact]
    public void OtherBalanceOperationsOnlyChangeTheBalance()
    {
        var driver = FundedWithProfit(108_000m);

        var outputs = driver.BalanceAdjusted("firm-bonus", 500m, 108_500m);

        Assert.Empty(outputs);
        Assert.Equal(108_500m, driver.State.Account!.Balance);
        Assert.Equal(5, driver.State.TradingDays.Count);
    }

    /// <summary>A funded account traded on five days, with all positions closed at <paramref name="balance"/>.</summary>
    private static ChallengeDriver FundedWithProfit(decimal balance)
    {
        var driver = new ChallengeDriver();
        driver.Fund();
        driver.TradeOnDays(FundedMonday, 5);
        driver.Update(balance);
        return driver;
    }

    /// <summary>A payout of 6 400 whose profit of 8 000 is off the account, waiting for the firm.</summary>
    private static ChallengeDriver WithdrawnPayout()
    {
        var driver = FundedWithProfit(108_000m);
        driver.RequestPayout();
        driver.BalanceAdjusted("P1", -8_000m, 100_000m);
        return driver;
    }
}
