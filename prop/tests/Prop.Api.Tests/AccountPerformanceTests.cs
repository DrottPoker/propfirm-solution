using Prop.Api.History;
using Prop.Api.Trading;
using Prop.Rules;

namespace Prop.Api.Tests;

/// <summary>The results and statistics the trader's dashboard shows, worked out from the trading history.</summary>
public sealed class AccountPerformanceTests
{
    // Trading days start at midnight in Stockholm, which is 22:00 UTC in October.
    private static readonly TradingDayDefinition Stockholm = new("Europe/Stockholm", TimeOnly.MinValue);

    [Fact]
    public void StatisticsCountResultsAfterCommissionsAndLeaveEvenTradesOutOfWinsAndLosses()
    {
        var trades = new[]
        {
            Trade(profit: 310m, commission: 10m, volume: 1m),
            Trade(profit: -95m, commission: 5m, volume: 0.5m),
            Trade(profit: 54m, commission: 4m, volume: 0.4m),
            Trade(profit: 2m, commission: 2m, volume: 0.1m),
        };

        var statistics = AccountPerformance.Statistics(trades);

        Assert.Equal((4, 2, 1), (statistics.Trades, statistics.Wins, statistics.Losses));
        Assert.Equal(50.0m, statistics.WinRatePercent);
        Assert.Equal((175m, -100m), (statistics.AverageWin, statistics.AverageLoss));
        Assert.Equal((300m, -100m), (statistics.BestTrade, statistics.WorstTrade));
        Assert.Equal(3.5m, statistics.ProfitFactor);
        Assert.Equal((2m, 250m, 21m), (statistics.Lots, statistics.Result, statistics.Commission));
    }

    [Fact]
    public void StatisticsWithoutTradesOrLossesLeaveOutWhatCannotBeWorkedOut()
    {
        var none = AccountPerformance.Statistics([]);
        var onlyWins = AccountPerformance.Statistics([Trade(profit: 100m), Trade(profit: 50m)]);

        Assert.Equal((0, null, null, null, null, null, 0m), (none.Trades, none.WinRatePercent, none.AverageWin, none.BestTrade, none.WorstTrade, none.ProfitFactor, none.Result));
        Assert.Equal((100m, null, null), (onlyWins.WinRatePercent, onlyWins.AverageLoss, onlyWins.ProfitFactor));
    }

    [Fact]
    public void AveragesAndRatiosAreRoundedForShowing()
    {
        var statistics = AccountPerformance.Statistics([Trade(profit: 100m), Trade(profit: 100m), Trade(profit: 0.01m), Trade(profit: -30m)]);

        Assert.Equal(66.67m, statistics.AverageWin);
        Assert.Equal(75.0m, statistics.WinRatePercent);
        Assert.Equal(6.67m, statistics.ProfitFactor);
    }

    [Fact]
    public void ATradeBelongsToTheTradingDayItClosedOnAndCountedDaysWithoutClosesAreShownToo()
    {
        var trades = new[]
        {
            // Monday 10:00 and 23:30 UTC: the second is already Tuesday in Stockholm.
            Trade(profit: 100m, volume: 1m, closedAt: new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero)),
            Trade(profit: -40m, volume: 0.5m, closedAt: new DateTimeOffset(2026, 10, 5, 22, 30, 0, TimeSpan.Zero)),
            Trade(profit: 10m, volume: 0.2m, closedAt: new DateTimeOffset(2026, 10, 6, 9, 0, 0, TimeSpan.Zero)),
        };
        var counted = new HashSet<DateOnly> { new(2026, 10, 5), new(2026, 10, 7) };

        var days = AccountPerformance.Days(trades, counted, Stockholm);

        Assert.Equal(
            [
                (new DateOnly(2026, 10, 7), 0, 0m, 0m, true),
                (new DateOnly(2026, 10, 6), 2, 0.7m, -30m, false),
                (new DateOnly(2026, 10, 5), 1, 1m, 100m, true),
            ],
            days.Select(d => (d.Day, d.Trades, d.Lots, d.Result, d.Counted)));
    }

    [Fact]
    public void TodayIsEquityAgainstTheBalanceWhenTheDayStartedWithoutDepositsOrWithdrawals()
    {
        // 2 000 made today, and a payout of 3 000 withdrawn.
        Assert.Equal(2_000m, AccountPerformance.Today(104_000m, new DayStart(105_000m, -3_000m, HasHistory: true), 100_000m));

        // Opened during the day, so the day started at the initial balance.
        Assert.Equal(-250m, AccountPerformance.Today(99_750m, new DayStart(null, 0m, HasHistory: true), 100_000m));

        Assert.Null(AccountPerformance.Today(null, new DayStart(100_000m, 0m, HasHistory: true), 100_000m));
        Assert.Null(AccountPerformance.Today(100_500m, new DayStart(null, 0m, HasHistory: false), 100_000m));
        Assert.Null(AccountPerformance.Today(100_500m, null, 100_000m));
    }

    [Theory]
    [InlineData(2_500, 10_000, 25)]
    [InlineData(-1_000, 10_000, 0)]
    [InlineData(12_000, 10_000, 100)]
    [InlineData(1, 3, 33.3)]
    public void ProgressToTheTargetGoesFromZeroToAHundred(decimal gained, decimal required, decimal expected)
    {
        Assert.Equal(expected, AccountPerformance.Progress(gained, required));
    }

    [Fact]
    public void APercentageHasTwoDecimals()
    {
        Assert.Equal(3.66m, AccountPerformance.Percent(3_655.20m, 100_000m));
        Assert.Equal(-3.54m, AccountPerformance.Percent(-1_769.60m, 50_000m));
    }

    private static ClosedTrade Trade(decimal profit, decimal commission = 0m, decimal volume = 1m, DateTimeOffset? closedAt = null) =>
        new("p", "EURUSD", TradeSide.Buy, volume, 1.1m, null, 1.1m, closedAt ?? new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero), profit, commission, "Manual", 1);
}
