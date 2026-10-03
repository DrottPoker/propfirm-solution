using Prop.Api.Challenges;
using Prop.Rules;

namespace Prop.Api.Tests;

public sealed class TradingDaysTests
{
    private static readonly TradingDayDefinition Stockholm = new("Europe/Stockholm", TimeOnly.MinValue);
    private static readonly TradingDayDefinition NewYorkEvening = new("America/New_York", new TimeOnly(17, 0));

    [Theory]
    [InlineData("2026-10-05T21:59:59Z", "2026-10-05")]
    [InlineData("2026-10-05T22:00:00Z", "2026-10-06")]
    [InlineData("2026-12-01T22:59:59Z", "2026-12-01")]
    [InlineData("2026-12-01T23:00:00Z", "2026-12-02")]
    public void AStockholmDayStartsAtLocalMidnightSummerAndWinter(string time, string day)
    {
        Assert.Equal(DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture), TradingDays.DayOf(DateTimeOffset.Parse(time, System.Globalization.CultureInfo.InvariantCulture), Stockholm));
    }

    // A day that starts at 17:00 is named by the date it starts on.
    [Theory]
    [InlineData("2026-10-04T20:59:59Z", "2026-10-03")]
    [InlineData("2026-10-04T21:00:00Z", "2026-10-04")]
    [InlineData("2026-10-05T14:00:00Z", "2026-10-04")]
    public void ADayThatStartsInTheEveningIsNamedByItsStart(string time, string day)
    {
        Assert.Equal(DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture), TradingDays.DayOf(DateTimeOffset.Parse(time, System.Globalization.CultureInfo.InvariantCulture), NewYorkEvening));
    }

    [Fact]
    public void TheNextStartFollowsTheClocksWhenTheyChange()
    {
        // Summer time ends in Stockholm on 25 October 2026.
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero), TradingDays.NextStart(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero), Stockholm));
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 23, 0, 0, TimeSpan.Zero), TradingDays.NextStart(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero), Stockholm));
    }

    [Fact]
    public void AStartInTheSkippedHourMovesToTheFirstTimeThatExists()
    {
        // Clocks go from 02:00 to 03:00 in Stockholm on 28 March 2027.
        var halfPastTwo = new TradingDayDefinition("Europe/Stockholm", new TimeOnly(2, 30));

        var start = TradingDays.NextStart(new DateTimeOffset(2027, 3, 27, 12, 0, 0, TimeSpan.Zero), halfPastTwo);

        Assert.Equal(new DateTimeOffset(2027, 3, 28, 1, 0, 0, TimeSpan.Zero), start);
    }

    [Fact]
    public void OnlyRealTimeZonesAreKnown()
    {
        Assert.True(TradingDays.IsKnownTimeZone("Europe/Stockholm"));
        Assert.False(TradingDays.IsKnownTimeZone("Mars/Olympus_Mons"));
    }
}
