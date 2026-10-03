using Prop.Rules.Tests.Support;

using static Prop.Rules.Tests.Support.ChallengeDriver;

namespace Prop.Rules.Tests;

public sealed class ChallengeRulesTests
{
    // Outputs are compared without their times.
    private static DateTimeOffset T => default;

    [Fact]
    public void StartAsksForTheFirstStagesAccount()
    {
        var driver = new ChallengeDriver();

        Assert.Equal(ChallengeStatus.OpeningAccount, driver.State.Status);
        Assert.Equal([new OpenAccountRequested(T, 0, 100_000m, "USD")], WithoutTime(driver.Outputs));
    }

    [Fact]
    public void OpeningTheAccountSetsTheLossFloors()
    {
        var driver = new ChallengeDriver();

        var outputs = driver.OpenAccount("A1", Monday);

        Assert.Equal(ChallengeStatus.Active, driver.State.Status);
        Assert.Equal(
            [
                new StageStarted(T, 0, "A1"),
                new FloorRequested(T, "A1", FloorIds.MaxLoss, new FixedFloor(90_000m)),
                new FloorRequested(T, "A1", FloorIds.Daily, new StartOfDayFloor(5_000m, DailyLossReference.Balance)),
            ],
            WithoutTime(outputs));
    }

    [Fact]
    public void TrailingMaxLossStopsRisingAtTheInitialBalance()
    {
        var template = ChallengeTemplates.TwoStep("trailing", 100_000m);
        var trailing = template with
        {
            Evaluation = [template.Evaluation[0] with { MaxLoss = new MaxLossRule(10, MaxLossKind.Trailing) }, template.Evaluation[1]],
        };
        var driver = new ChallengeDriver(trailing);

        var outputs = driver.OpenAccount("A1", Monday);

        Assert.Contains(new FloorRequested(T, "A1", FloorIds.MaxLoss, new TrailingFloor(10_000m, 100_000m)), WithoutTime(outputs));
    }

    [Fact]
    public void EveryNewTradingDayResetsTheDailyFloorOnce()
    {
        var driver = new ChallengeDriver();
        driver.OpenAccount("A1", Monday);

        var tuesday = driver.StartDay(Monday.AddDays(1));
        var repeated = driver.StartDay(Monday.AddDays(1));
        var late = driver.StartDay(Monday);

        Assert.Equal([new FloorRequested(T, "A1", FloorIds.Daily, new StartOfDayFloor(5_000m, DailyLossReference.Balance))], WithoutTime(tuesday));
        Assert.Empty(repeated);
        Assert.Empty(late);
        Assert.Equal(Monday.AddDays(1), driver.State.CurrentDay);
    }

    [Fact]
    public void ADayWithAnOpenedPositionCountsOnce()
    {
        var driver = new ChallengeDriver();
        driver.OpenAccount("A1", Monday);

        var first = driver.OpenPosition(Monday);
        var sameDay = driver.OpenPosition(Monday);
        var wednesday = driver.OpenPosition(Monday.AddDays(2));
        var lateTuesday = driver.OpenPosition(Monday.AddDays(1));

        Assert.Equal([new TradingDayCounted(T, 0, Monday, 1)], WithoutTime(first));
        Assert.Empty(sameDay);
        Assert.Equal([new TradingDayCounted(T, 0, Monday.AddDays(2), 2)], WithoutTime(wednesday));
        Assert.Equal([new TradingDayCounted(T, 0, Monday.AddDays(1), 3)], WithoutTime(lateTuesday));
    }

    [Fact]
    public void StageIsPassedOnTheBalanceOnceEveryPositionIsClosed()
    {
        var driver = new ChallengeDriver();
        driver.OpenAccount("A1", Monday);
        driver.TradeOnDays(Monday, 4);

        var withOpenPosition = driver.Update(110_000m, 110_400m, openPositions: 1);
        var closed = driver.Update(110_000m, 110_000m);

        Assert.Empty(withOpenPosition);
        Assert.Equal(
            [
                new StagePassed(T, 0, "A1", 110_000m, 4),
                new CloseAccountRequested(T, "A1"),
                new OpenAccountRequested(T, 1, 100_000m, "USD"),
            ],
            WithoutTime(closed));
        Assert.Equal((1, ChallengeStatus.OpeningAccount, null), (driver.State.Stage, driver.State.Status, driver.State.AccountId));
    }

    [Fact]
    public void EquityAboveTheTargetIsNotEnough()
    {
        var driver = new ChallengeDriver();
        driver.OpenAccount("A1", Monday);
        driver.TradeOnDays(Monday, 4);

        Assert.Empty(driver.Update(105_000m, 112_000m, openPositions: 1));
        Assert.Empty(driver.Update(109_999.99m, 109_999.99m));
        Assert.Equal(ChallengeStatus.Active, driver.State.Status);
    }

    [Fact]
    public void StageWaitsForTheMinimumNumberOfTradingDays()
    {
        var driver = new ChallengeDriver();
        driver.OpenAccount("A1", Monday);
        driver.TradeOnDays(Monday, 2);

        var afterTwoDays = driver.Update(111_000m, 111_000m);
        driver.TradeOnDays(Monday.AddDays(2), 1);
        var afterThreeDays = driver.Update(111_010m, 111_010m);
        driver.TradeOnDays(Monday.AddDays(3), 1);
        var afterFourDays = driver.Update(111_020m, 111_020m);

        Assert.Empty(afterTwoDays);
        Assert.Empty(afterThreeDays);
        Assert.Contains(new StagePassed(T, 0, "A1", 111_020m, 4), WithoutTime(afterFourDays));
    }

    [Fact]
    public void AfterTheLastEvaluationStageTheFirmApprovesTheFundedAccount()
    {
        var driver = new ChallengeDriver();
        driver.PassStage("A1", Monday, 110_000m);

        var lastStage = driver.PassStage("A2", Monday.AddDays(7), 105_000m);
        var approved = driver.Apply(new ApproveFunding(driver.NextTime()));
        var funded = driver.OpenAccount("A3", Monday.AddDays(14));

        Assert.Equal(
            [new StagePassed(T, 1, "A2", 105_000m, 4), new CloseAccountRequested(T, "A2"), new FundingAwaited(T)],
            WithoutTime(lastStage));
        Assert.Equal([new OpenAccountRequested(T, 2, 100_000m, "USD")], WithoutTime(approved));
        Assert.Equal(
            [
                new StageStarted(T, 2, "A3"),
                new FloorRequested(T, "A3", FloorIds.MaxLoss, new FixedFloor(90_000m)),
                new FloorRequested(T, "A3", FloorIds.Daily, new StartOfDayFloor(5_000m, DailyLossReference.Balance)),
            ],
            WithoutTime(funded));
    }

    [Fact]
    public void TheFundedStageHasNoTarget()
    {
        var driver = new ChallengeDriver();
        driver.PassStage("A1", Monday, 110_000m);
        driver.PassStage("A2", Monday.AddDays(7), 105_000m);
        driver.Apply(new ApproveFunding(driver.NextTime()));

        var outputs = driver.PassStage("A3", Monday.AddDays(14), 200_000m);

        Assert.Empty(outputs);
        Assert.Equal((2, ChallengeStatus.Active), (driver.State.Stage, driver.State.Status));
    }

    [Fact]
    public void FundingIsOnlyApprovedAfterTheEvaluation()
    {
        var driver = new ChallengeDriver();
        driver.OpenAccount("A1", Monday);

        var outputs = driver.Apply(new ApproveFunding(driver.NextTime()));

        Assert.Equal([new InputIgnored(T, nameof(ApproveFunding), "The challenge is not waiting for funding.")], WithoutTime(outputs));
        Assert.Equal(ChallengeStatus.Active, driver.State.Status);
    }

    [Theory]
    [InlineData(FloorIds.Daily, FailureReason.DailyLoss)]
    [InlineData(FloorIds.MaxLoss, FailureReason.MaxLoss)]
    [InlineData("set-by-firm", FailureReason.OtherFloor)]
    public void ABreachedFloorFailsTheChallengeWithTheEvidence(string floorId, FailureReason reason)
    {
        var driver = new ChallengeDriver();
        driver.OpenAccount("A1", Monday);

        var outputs = driver.Breach(floorId, 95_000m, 94_980.50m);
        var afterwards = driver.Update(120_000m, 120_000m);

        Assert.Equal([new ChallengeFailed(T, 0, "A1", reason, floorId, 95_000m, 94_980.50m)], WithoutTime(outputs));
        Assert.Equal(ChallengeStatus.Failed, driver.State.Status);
        Assert.Empty(afterwards);
    }

    [Fact]
    public void RepeatedLateAndOtherAccountsFactsChangeNothing()
    {
        var driver = new ChallengeDriver();
        driver.OpenAccount("A1", Monday);
        var fact = new PositionOpened(driver.NextTime(), "A1", 100, Monday);

        var first = driver.Apply(fact);
        var repeated = driver.Apply(fact);
        var late = driver.Apply(new PositionOpened(driver.NextTime(), "A1", 99, Monday.AddDays(1)));
        var otherAccount = driver.Apply(new PositionOpened(driver.NextTime(), "B7", 101, Monday.AddDays(1)));

        Assert.Single(first);
        Assert.Empty(repeated);
        Assert.Empty(late);
        Assert.Empty(otherAccount);
        Assert.Equal([Monday], driver.State.TradingDays);
    }

    [Fact]
    public void FactsAboutAPassedStagesAccountAreIgnored()
    {
        var driver = new ChallengeDriver();
        driver.PassStage("A1", Monday, 110_000m);
        driver.OpenAccount("A2", Monday.AddDays(7));

        // Closing A1 after the pass disables it on the trading platform.
        var outputs = driver.Apply(new AccountDisabled(driver.NextTime(), "A1", driver.NextSequence()));

        Assert.Empty(outputs);
        Assert.Equal(ChallengeStatus.Active, driver.State.Status);
    }

    [Fact]
    public void CancellingClosesTheOpenAccountOnce()
    {
        var driver = new ChallengeDriver();
        driver.OpenAccount("A1", Monday);

        var cancelled = driver.Apply(new CancelChallenge(driver.NextTime(), "Refunded"));
        var again = driver.Apply(new CancelChallenge(driver.NextTime(), "Refunded"));

        Assert.Equal([new CloseAccountRequested(T, "A1"), new ChallengeCancelled(T, "Refunded")], WithoutTime(cancelled));
        Assert.Equal([new InputIgnored(T, nameof(CancelChallenge), "The challenge has already ended.")], WithoutTime(again));
        Assert.Equal(ChallengeStatus.Cancelled, driver.State.Status);
    }

    [Fact]
    public void AnAccountOpenedAfterTheChallengeEndedIsClosedAgain()
    {
        var driver = new ChallengeDriver();
        driver.Apply(new CancelChallenge(driver.NextTime(), "Refunded"));

        var outputs = driver.OpenAccount("A1", Monday);

        Assert.Equal([new CloseAccountRequested(T, "A1")], WithoutTime(outputs));
        Assert.Equal(ChallengeStatus.Cancelled, driver.State.Status);
    }

    [Fact]
    public void AnAccountDisabledOnTheTradingPlatformCancelsTheChallenge()
    {
        var driver = new ChallengeDriver();
        driver.OpenAccount("A1", Monday);

        var outputs = driver.Apply(new AccountDisabled(driver.NextTime(), "A1", driver.NextSequence()));

        Assert.Equal([new ChallengeCancelled(T, "The trading account was disabled on the trading platform.")], WithoutTime(outputs));
        Assert.Equal(ChallengeStatus.Cancelled, driver.State.Status);
    }

    [Fact]
    public void ARepeatedAccountOpeningChangesNothing()
    {
        var driver = new ChallengeDriver();
        driver.OpenAccount("A1", Monday);

        var repeated = driver.OpenAccount("A1", Monday);
        var another = driver.OpenAccount("A9", Monday);

        Assert.Empty(repeated);
        Assert.Equal([new InputIgnored(T, nameof(AccountOpened), "No account is being opened.")], WithoutTime(another));
        Assert.Equal("A1", driver.State.AccountId);
    }
}
