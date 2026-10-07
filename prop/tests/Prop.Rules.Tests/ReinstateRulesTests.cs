using Prop.Rules.Tests.Support;

using static Prop.Rules.Tests.Support.ChallengeDriver;

namespace Prop.Rules.Tests;

/// <summary>A failed stage reinstated by the firm, for example after an outage broke a loss limit (ADR 0053).</summary>
public sealed class ReinstateRulesTests
{
    // Outputs are compared without their times.
    private static DateTimeOffset T => default;

    // Two trading days, a breach on the third and the stage reinstated the day after, with the equity before the outage.
    [Fact]
    public void AFailedStageGoesOnWithTheBalanceItsDaysAndItsDeadlinesMovedOn()
    {
        var driver = Breached();
        var inactivity = driver.State.InactivityDeadline!.Value;

        var outputs = driver.Apply(new ReinstateStage(driver.NextTime(), Monday.AddDays(3), 98_212.50m, KeepTradingDays: true));

        Assert.Equal(
            [
                new StageReinstated(T, 0, "A1", 98_212.50m, true, 1),
                new ReopenAccountRequested(T, "A1", 98_212.50m),
                new FloorRequested(T, "A1", FloorIds.MaxLoss, new FixedFloor(90_000m)),
                new FloorRequested(T, "A1", FloorIds.Daily, new StartOfDayFloor(5_000m, DailyLossReference.Balance)),
            ],
            WithoutTime(outputs));
        var state = driver.State;
        Assert.Equal((ChallengeStatus.Active, 98_212.50m, 2), (state.Status, state.Account!.Balance, state.TradingDays.Count));
        Assert.Equal(inactivity.AddDays(1), state.InactivityDeadline);
        Assert.Null(state.EndedOn);

        // The account trades again, and passes at the same target as before.
        driver.Update(98_500m);
        Assert.Equal(98_500m, driver.State.Account!.Balance);
    }

    [Fact]
    public void WithoutKeepingThemTheTradingDaysStartOver()
    {
        var driver = Breached();

        driver.Apply(new ReinstateStage(driver.NextTime(), Monday.AddDays(2), 99_000m, KeepTradingDays: false));

        Assert.Empty(driver.State.TradingDays);
        Assert.IsType<StageReinstated>(driver.Outputs[^4]);
    }

    [Fact]
    public void OnlyAFailedStageIsReinstated()
    {
        var active = new ChallengeDriver();
        active.OpenAccount("A1", Monday);
        var cancelled = new ChallengeDriver();
        cancelled.OpenAccount("A1", Monday);
        cancelled.Apply(new CancelChallenge(cancelled.NextTime(), "Done"));
        var breached = Breached();

        Assert.IsType<InputIgnored>(Assert.Single(active.Apply(new ReinstateStage(active.NextTime(), Monday, 100_000m, true))));
        Assert.IsType<InputIgnored>(Assert.Single(cancelled.Apply(new ReinstateStage(cancelled.NextTime(), Monday, 100_000m, true))));
        Assert.IsType<InputIgnored>(Assert.Single(breached.Apply(new ReinstateStage(breached.NextTime(), Monday, 0m, true))));
    }

    private static ChallengeDriver Breached()
    {
        var driver = new ChallengeDriver();
        driver.OpenAccount("A1", Monday);
        driver.TradeOnDays(Monday, 2);
        driver.StartDay(Monday.AddDays(2));
        driver.Breach(FloorIds.Daily, 95_000m, 94_982.50m);
        Assert.Equal((ChallengeStatus.Failed, Monday.AddDays(2)), (driver.State.Status, driver.State.EndedOn));
        return driver;
    }
}
