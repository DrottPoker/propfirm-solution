namespace Trading.Engine.Tests;

public sealed class TradingHoursTests
{
    // Sunday 17:00 to Friday 17:00 New York time, as the forex week.
    public static readonly TradingHours Forex = new(
        "America/New_York",
        [new TradingSession(DayOfWeek.Sunday, new TimeOnly(17, 0), DayOfWeek.Friday, new TimeOnly(17, 0))]);

    // CME Globex: 18:00 to 17:00 the next day New York time, Sunday to Friday, with a break of an hour each day.
    public static readonly TradingHours Globex = new(
        "America/New_York",
        [.. Enumerable.Range(0, 5).Select(d => new TradingSession((DayOfWeek)d, new TimeOnly(18, 0), (DayOfWeek)(d + 1), new TimeOnly(17, 0)))]);

    private static readonly TradingHours Eurex = new(
        "Europe/Berlin",
        [.. Enumerable.Range(1, 5).Select(d => new TradingSession((DayOfWeek)d, new TimeOnly(1, 10), (DayOfWeek)d, new TimeOnly(22, 0)))]);

    [Theory]
    [InlineData("2026-10-04T20:59:59Z", false)] // Sunday 16:59:59 in New York
    [InlineData("2026-10-04T21:00:00Z", true)] // Sunday 17:00
    [InlineData("2026-10-07T12:00:00Z", true)] // Wednesday
    [InlineData("2026-10-09T20:59:59Z", true)] // Friday 16:59:59
    [InlineData("2026-10-09T21:00:00Z", false)] // Friday 17:00
    [InlineData("2026-10-10T12:00:00Z", false)] // Saturday
    public void ForexIsOpenFromSundayToFridayEvening(string at, bool open) =>
        Assert.Equal(open, Forex.IsOpen(DateTimeOffset.Parse(at, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void SessionsFollowTheMarketsClockChanges()
    {
        // New York leaves summer time on 1 November 2026, so the week opens an hour later in UTC.
        Assert.False(Forex.IsOpen(Utc(2026, 11, 1, 21, 30)));
        Assert.True(Forex.IsOpen(Utc(2026, 11, 1, 22, 0)));

        // Berlin leaves summer time on 25 October 2026, a week before New York.
        Assert.Equal(Utc(2026, 10, 18, 23, 10), Eurex.NextChange(Utc(2026, 10, 18, 12, 0)));
        Assert.Equal(Utc(2026, 10, 26, 0, 10), Eurex.NextChange(Utc(2026, 10, 25, 12, 0)));
    }

    [Fact]
    public void GlobexClosesForAnHourEachDay()
    {
        // Tuesday 17:00 to 18:00 in New York.
        Assert.True(Globex.IsOpen(Utc(2026, 10, 6, 20, 59)));
        Assert.False(Globex.IsOpen(Utc(2026, 10, 6, 21, 30)));
        Assert.True(Globex.IsOpen(Utc(2026, 10, 6, 22, 0)));

        Assert.Equal(Utc(2026, 10, 6, 21, 0), Globex.NextChange(Utc(2026, 10, 6, 20, 0)));
        Assert.Equal(Utc(2026, 10, 6, 22, 0), Globex.NextChange(Utc(2026, 10, 6, 21, 30)));
    }

    [Fact]
    public void ClosedMarketChangesWhenItOpensNext()
    {
        Assert.Equal(Utc(2026, 10, 11, 21, 0), Forex.NextChange(Utc(2026, 10, 10, 12, 0)));
        Assert.Equal(Utc(2026, 10, 9, 21, 0), Forex.NextChange(Utc(2026, 10, 5, 8, 0)));
    }

    [Fact]
    public void PeriodsAreWholeAndInOrder()
    {
        var periods = Globex.PeriodsBetween(Utc(2026, 10, 5, 8, 0), Utc(2026, 10, 12, 8, 0));

        Assert.Equal(6, periods.Count);
        Assert.Equal(new MarketPeriod(Utc(2026, 10, 4, 22, 0), Utc(2026, 10, 5, 21, 0)), periods[0]);
        Assert.Equal(new MarketPeriod(Utc(2026, 10, 8, 22, 0), Utc(2026, 10, 9, 21, 0)), periods[4]);
        Assert.Equal(new MarketPeriod(Utc(2026, 10, 11, 22, 0), Utc(2026, 10, 12, 21, 0)), periods[5]);
        Assert.All(periods, p => Assert.Equal(TimeSpan.Zero, p.Opens.Offset));
    }

    [Fact]
    public void SessionsThatTouchAreOnePeriod()
    {
        var hours = new TradingHours(
            "UTC",
            [
                new TradingSession(DayOfWeek.Monday, new TimeOnly(0, 0), DayOfWeek.Tuesday, new TimeOnly(0, 0)),
                new TradingSession(DayOfWeek.Tuesday, new TimeOnly(0, 0), DayOfWeek.Wednesday, new TimeOnly(0, 0)),
            ]);

        var period = Assert.Single(hours.PeriodsBetween(Utc(2026, 10, 5, 12, 0), Utc(2026, 10, 5, 13, 0)));

        Assert.Equal(new MarketPeriod(Utc(2026, 10, 5, 0, 0), Utc(2026, 10, 7, 0, 0)), period);
        Assert.Equal(Utc(2026, 10, 7, 0, 0), hours.NextChange(Utc(2026, 10, 5, 12, 0)));
    }

    [Fact]
    public void ClosuresAreCutOutInTheMarketsTime()
    {
        // Christmas Day in New York, a Friday: the week ends at midnight instead of 17:00.
        var hours = Forex with { Closures = [new TradingClosure(new DateTime(2026, 12, 25), new DateTime(2026, 12, 26))] };

        Assert.True(hours.IsOpen(Utc(2026, 12, 25, 4, 59)));
        Assert.False(hours.IsOpen(Utc(2026, 12, 25, 5, 0)));
        Assert.Equal(Utc(2026, 12, 25, 5, 0), hours.NextChange(Utc(2026, 12, 24, 12, 0)));
        Assert.Equal(Utc(2026, 12, 27, 22, 0), hours.NextChange(Utc(2026, 12, 25, 12, 0)));
    }

    [Fact]
    public void ClosureInTheMiddleSplitsThePeriod()
    {
        var hours = Forex with { Closures = [new TradingClosure(new DateTime(2026, 10, 7, 9, 0, 0), new DateTime(2026, 10, 7, 10, 0, 0))] };

        var periods = hours.PeriodsBetween(Utc(2026, 10, 5, 0, 0), Utc(2026, 10, 9, 0, 0));

        Assert.Equal([Utc(2026, 10, 4, 21, 0), Utc(2026, 10, 7, 14, 0)], periods.Select(p => p.Opens));
        Assert.Equal([Utc(2026, 10, 7, 13, 0), Utc(2026, 10, 9, 21, 0)], periods.Select(p => p.Closes));
    }

    [Fact]
    public void OpeningInTheHourTheClocksSkipIsAtTheFirstTimeThatExists()
    {
        // New York skips 02:00 to 03:00 on 14 March 2027.
        var hours = new TradingHours("America/New_York", [new TradingSession(DayOfWeek.Sunday, new TimeOnly(2, 30), DayOfWeek.Sunday, new TimeOnly(5, 0))]);

        Assert.Equal(Utc(2027, 3, 14, 7, 0), hours.NextChange(Utc(2027, 3, 14, 0, 0)));
    }

    [Fact]
    public void OpeningInTheHourTheClocksRepeatIsTheFirstOfTheTwo()
    {
        // New York repeats 01:00 to 02:00 on 1 November 2026, first in summer time.
        var hours = new TradingHours("America/New_York", [new TradingSession(DayOfWeek.Sunday, new TimeOnly(1, 30), DayOfWeek.Sunday, new TimeOnly(5, 0))]);

        Assert.Equal(Utc(2026, 11, 1, 5, 30), hours.NextChange(Utc(2026, 11, 1, 0, 0)));
    }

    [Fact]
    public void MarketThatDoesNotOpenWithinAMonthHasNoNextChange()
    {
        var hours = Forex with { Closures = [new TradingClosure(new DateTime(2026, 10, 1), new DateTime(2026, 12, 1))] };

        Assert.Null(hours.NextChange(Utc(2026, 10, 5, 8, 0)));
        Assert.False(hours.IsOpen(Utc(2026, 10, 5, 8, 0)));
    }

    [Fact]
    public void SessionLengthIsOnTheLocalClock()
    {
        Assert.Equal(TimeSpan.FromDays(5), Forex.Sessions[0].Length);
        Assert.Equal(TimeSpan.FromHours(23), Globex.Sessions[4].Length);
        Assert.Equal(TimeSpan.FromHours(20) + TimeSpan.FromMinutes(50), Eurex.Sessions[0].Length);
    }

    [Fact]
    public void ValidHoursHaveNoProblem()
    {
        Assert.Null(Forex.FindProblem());
        Assert.Null(Globex.FindProblem());
        Assert.Null(Eurex.FindProblem());
    }

    [Theory]
    [MemberData(nameof(InvalidHours))]
    public void InvalidHoursHaveAProblem(TradingHours hours, string expected) =>
        Assert.Contains(expected, hours.FindProblem(), StringComparison.Ordinal);

    public static TheoryData<TradingHours, string> InvalidHours() =>
        new()
        {
            { Forex with { TimeZone = "Mars/Olympus" }, "time zone" },
            { Forex with { Sessions = [] }, "at least one session" },
            { Forex with { Sessions = [new TradingSession((DayOfWeek)7, new TimeOnly(0, 0), DayOfWeek.Monday, new TimeOnly(1, 0))] }, "unknown day" },
            { Forex with { Sessions = [new TradingSession(DayOfWeek.Monday, new TimeOnly(9, 0), DayOfWeek.Monday, new TimeOnly(9, 0))] }, "closed some time each week" },
            {
                Forex with
                {
                    Sessions =
                    [
                        new TradingSession(DayOfWeek.Monday, new TimeOnly(0, 0), DayOfWeek.Monday, new TimeOnly(12, 0)),
                        new TradingSession(DayOfWeek.Monday, new TimeOnly(11, 0), DayOfWeek.Monday, new TimeOnly(13, 0)),
                    ],
                },
                "overlap"
            },
            {
                Forex with
                {
                    Sessions =
                    [
                        new TradingSession(DayOfWeek.Saturday, new TimeOnly(0, 0), DayOfWeek.Sunday, new TimeOnly(12, 0)),
                        new TradingSession(DayOfWeek.Sunday, new TimeOnly(11, 0), DayOfWeek.Sunday, new TimeOnly(13, 0)),
                    ],
                },
                "overlap"
            },
            { Forex with { Closures = [new TradingClosure(new DateTime(2026, 12, 26), new DateTime(2026, 12, 25))] }, "closure" },
        };

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) => new(year, month, day, hour, minute, 0, TimeSpan.Zero);
}
