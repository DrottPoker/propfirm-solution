using Prop.Rules.Tests.Support;

using static Prop.Rules.Tests.Support.ChallengeDriver;

namespace Prop.Rules.Tests;

public sealed class PauseRulesTests
{
    private static readonly ChallengeDefinition TwoStep = ChallengeTemplates.TwoStep("two-step-100k", 100_000m);

    // Outputs are compared without their times.
    private static DateTimeOffset T => default;

    [Fact]
    public void PausingSuspendsTheAccountAndStopsTheDays()
    {
        var driver = new ChallengeDriver();
        driver.OpenAccount("A1", Monday);

        var paused = Pause(driver, Monday.AddDays(5));
        var pastTheDeadline = driver.StartDay(Monday.AddDays(40));

        Assert.Equal([new ChallengePaused(T), new SuspendAccountRequested(T, "A1")], WithoutTime(paused));
        Assert.Equal([new FloorRequested(T, "A1", FloorIds.Daily, new StartOfDayFloor(5_000m, DailyLossReference.Balance))], WithoutTime(pastTheDeadline));
        Assert.Equal((ChallengeStatus.Active, Monday.AddDays(5)), (driver.State.Status, driver.State.PausedOn));
    }

    [Fact]
    public void ResumingMovesTheDeadlinesByTheDaysPaused()
    {
        var limited = TwoStep with { Evaluation = [.. TwoStep.Evaluation.Select(s => s with { MaxDays = 20 })] };
        var driver = new ChallengeDriver(limited);
        driver.OpenAccount("A1", Monday);
        Pause(driver, Monday.AddDays(5));

        var resumed = Resume(driver, Monday.AddDays(15));

        Assert.Equal([new ChallengeResumed(T, 10), new ResumeAccountRequested(T, "A1")], WithoutTime(resumed));
        Assert.Equal((Monday.AddDays(31), Monday.AddDays(41), null), (driver.State.StageDeadline, driver.State.InactivityDeadline, driver.State.PausedOn));
        Assert.DoesNotContain(driver.StartDay(Monday.AddDays(30)), o => o is ChallengeExpired);
        Assert.Contains(new ChallengeExpired(T, 0, "A1", ExpiryReason.TimeLimit, Monday.AddDays(31)), WithoutTime(driver.StartDay(Monday.AddDays(31))));
    }

    [Fact]
    public void PausingTwiceOrResumingWhatIsNotPausedChangesNothing()
    {
        var driver = new ChallengeDriver();
        driver.OpenAccount("A1", Monday);

        Assert.Empty(Resume(driver, Monday));
        Pause(driver, Monday.AddDays(1));
        var before = driver.State;

        Assert.Empty(Pause(driver, Monday.AddDays(2)));
        Assert.Same(before, driver.State);
    }

    [Fact]
    public void AStageThatStartsWhilePausedIsPausedFromItsFirstDay()
    {
        var driver = new ChallengeDriver();

        var paused = Pause(driver, Monday);
        var opened = driver.OpenAccount("A1", Monday.AddDays(2));
        Resume(driver, Monday.AddDays(4));

        Assert.Equal([new ChallengePaused(T)], WithoutTime(paused));
        Assert.Equal(new SuspendAccountRequested(T, "A1"), WithoutTime(opened)[^1]);
        Assert.Equal(Monday.AddDays(2 + 31 + 2), driver.State.InactivityDeadline);
    }

    [Fact]
    public void AStagePassedWhilePausedOpensTheNextAccountSuspended()
    {
        var driver = new ChallengeDriver();
        driver.OpenAccount("A1", Monday);
        driver.TradeOnDays(Monday, 4);
        driver.Update(110_000m, openPositions: 1);
        Pause(driver, Monday.AddDays(3));

        // The trader may still close positions, which passes the stage.
        var passed = driver.Update(110_000m);
        var opened = driver.OpenAccount("A2", Monday.AddDays(4));

        Assert.Contains(new StagePassed(T, 0, "A1", 110_000m, 4), WithoutTime(passed));
        Assert.Contains(new SuspendAccountRequested(T, "A2"), WithoutTime(opened));
        Assert.Equal(Monday.AddDays(4), driver.State.PausedOn);
    }

    [Fact]
    public void AnEndedChallengeIsNeitherPausedNorStillPaused()
    {
        var cancelled = new ChallengeDriver();
        cancelled.OpenAccount("A1", Monday);
        cancelled.Apply(new CancelChallenge(cancelled.NextTime(), "Done"));

        var breached = new ChallengeDriver();
        breached.OpenAccount("A1", Monday);
        Pause(breached, Monday);
        breached.Breach(FloorIds.Daily, 95_000m, 94_990m);

        Assert.Empty(Pause(cancelled, Monday.AddDays(1)));
        Assert.Null(cancelled.State.PausedOn);
        Assert.Null(breached.State.PausedOn);
        Assert.Empty(Resume(breached, Monday.AddDays(1)));
    }

    [Fact]
    public void AFundedTraderCanStillAskForAPayoutWhilePaused()
    {
        var driver = new ChallengeDriver();
        driver.Fund();
        driver.TradeOnDays(Monday.AddDays(14), 5);
        driver.Update(104_000m);
        Pause(driver, Monday.AddDays(19));

        var requested = driver.RequestPayout();

        Assert.Contains(requested, o => o is WithdrawalRequested);
    }

    private static IReadOnlyList<ChallengeOutput> Pause(ChallengeDriver driver, DateOnly day) => driver.Apply(new PauseChallenge(driver.NextTime(), day));

    private static IReadOnlyList<ChallengeOutput> Resume(ChallengeDriver driver, DateOnly day) => driver.Apply(new ResumeChallenge(driver.NextTime(), day));
}
