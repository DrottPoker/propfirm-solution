using Prop.Rules.Tests.Support;

using static Prop.Rules.Tests.Support.ChallengeDriver;

namespace Prop.Rules.Tests;

public sealed class ExpiryRulesTests
{
    private static readonly ChallengeDefinition TwoStep = ChallengeTemplates.TwoStep("two-step-100k", 100_000m);

    private static readonly FloorRequested DailyFloor = new(T, "A1", FloorIds.Daily, new StartOfDayFloor(5_000m, DailyLossReference.Balance));

    // Outputs are compared without their times.
    private static DateTimeOffset T => default;

    [Fact]
    public void AStageExpiresWhenItsTimeLimitHasPassed()
    {
        var driver = new ChallengeDriver(WithTimeLimit(10));
        driver.OpenAccount("A1", Monday);
        driver.OpenPosition(Monday.AddDays(9));

        var lastDay = driver.StartDay(Monday.AddDays(10));
        var expired = driver.StartDay(Monday.AddDays(11));

        Assert.Equal([DailyFloor], WithoutTime(lastDay));
        Assert.Equal(
            [new ChallengeExpired(T, 0, "A1", ExpiryReason.TimeLimit, Monday.AddDays(11)), new CloseAccountRequested(T, "A1")],
            WithoutTime(expired));
        Assert.Equal(ChallengeStatus.Failed, driver.State.Status);
    }

    [Fact]
    public void EachStageGetsItsOwnTimeLimitFromTheDayItStarts()
    {
        var driver = new ChallengeDriver(WithTimeLimit(10));
        driver.PassStage("A1", Monday, 110_000m);

        Assert.Null(driver.State.StageDeadline);
        driver.OpenAccount("A2", Monday.AddDays(7));

        Assert.Equal(Monday.AddDays(18), driver.State.StageDeadline);
    }

    [Fact]
    public void TheChallengeEndsAfterTheInactivityDaysWithoutANewPosition()
    {
        var driver = new ChallengeDriver();
        driver.OpenAccount("A1", Monday);

        var lastDay = driver.StartDay(Monday.AddDays(30));
        var expired = driver.StartDay(Monday.AddDays(31));

        Assert.Equal([DailyFloor], WithoutTime(lastDay));
        Assert.Equal(
            [new ChallengeExpired(T, 0, "A1", ExpiryReason.Inactivity, Monday.AddDays(31)), new CloseAccountRequested(T, "A1")],
            WithoutTime(expired));
    }

    [Fact]
    public void ANewPositionRestartsTheInactivityDays()
    {
        var driver = new ChallengeDriver();
        driver.OpenAccount("A1", Monday);
        driver.StartDay(Monday.AddDays(20));
        driver.OpenPosition(Monday.AddDays(20));

        Assert.Equal([DailyFloor], WithoutTime(driver.StartDay(Monday.AddDays(31))));
        Assert.Equal(Monday.AddDays(51), driver.State.InactivityDeadline);
        Assert.Contains(new ChallengeExpired(T, 0, "A1", ExpiryReason.Inactivity, Monday.AddDays(51)), WithoutTime(driver.StartDay(Monday.AddDays(51))));
    }

    [Fact]
    public void TheFundedAccountAlsoEndsFromInactivity()
    {
        var driver = new ChallengeDriver();
        driver.Fund();

        var expired = driver.StartDay(Monday.AddDays(14 + 31));

        Assert.Equal(
            [new ChallengeExpired(T, 2, "A3", ExpiryReason.Inactivity, Monday.AddDays(45)), new CloseAccountRequested(T, "A3")],
            WithoutTime(expired));
    }

    [Fact]
    public void DaysMissedWhileTheServiceWasDownStillCount()
    {
        var driver = new ChallengeDriver();
        driver.OpenAccount("A1", Monday);

        var expired = driver.StartDay(Monday.AddDays(40));

        Assert.Contains(new ChallengeExpired(T, 0, "A1", ExpiryReason.Inactivity, Monday.AddDays(40)), WithoutTime(expired));
    }

    [Fact]
    public void TheTimeLimitComesFirstWhenBothRunOutTheSameDay()
    {
        var driver = new ChallengeDriver(WithTimeLimit(30));
        driver.OpenAccount("A1", Monday);

        Assert.Contains(new ChallengeExpired(T, 0, "A1", ExpiryReason.TimeLimit, Monday.AddDays(31)), WithoutTime(driver.StartDay(Monday.AddDays(31))));
    }

    [Fact]
    public void WithoutTheRulesAChallengeNeverExpires()
    {
        var driver = new ChallengeDriver(TwoStep with { InactivityDays = null });
        driver.OpenAccount("A1", Monday);

        Assert.Equal([DailyFloor], WithoutTime(driver.StartDay(Monday.AddDays(400))));
        Assert.Null(driver.State.InactivityDeadline);
    }

    [Fact]
    public void AChallengeWaitingForTheFirmDoesNotExpire()
    {
        var driver = new ChallengeDriver();
        driver.PassStage("A1", Monday, 110_000m);
        driver.PassStage("A2", Monday.AddDays(7), 105_000m);

        Assert.Empty(driver.StartDay(Monday.AddDays(100)));
        Assert.Equal(ChallengeStatus.AwaitingFunding, driver.State.Status);
    }

    [Fact]
    public void FactsAboutAnExpiredAccountChangeNothing()
    {
        var driver = new ChallengeDriver();
        driver.OpenAccount("A1", Monday);
        driver.StartDay(Monday.AddDays(31));

        Assert.Empty(driver.Apply(new AccountDisabled(driver.NextTime(), "A1", driver.NextSequence())));
        Assert.Equal(ChallengeStatus.Failed, driver.State.Status);
    }

    private static ChallengeDefinition WithTimeLimit(int days) =>
        TwoStep with { Evaluation = [.. TwoStep.Evaluation.Select(s => s with { MaxDays = days })] };
}
