using Prop.Api.Api;
using Prop.Api.Challenges;
using Prop.Rules;

namespace Prop.Api.History;

/// <summary>
/// Results and statistics from an account's trading history. The portal never calculates money, so everything it
/// shows is worked out here, with decimals. Percentages and ratios are rounded for showing, amounts to whole cents.
/// </summary>
internal static class AccountPerformance
{
    public static TradeStatisticsResponse Statistics(IReadOnlyList<ClosedTrade> trades)
    {
        var wins = trades.Where(t => t.Result > 0m).Select(t => t.Result).ToList();
        var losses = trades.Where(t => t.Result < 0m).Select(t => t.Result).ToList();
        return new TradeStatisticsResponse(
            trades.Count,
            wins.Count,
            losses.Count,
            trades.Count == 0 ? null : Round(100m * wins.Count / trades.Count, 1),
            wins.Count == 0 ? null : Round(wins.Sum() / wins.Count, 2),
            losses.Count == 0 ? null : Round(losses.Sum() / losses.Count, 2),
            trades.Count == 0 ? null : trades.Max(t => t.Result),
            trades.Count == 0 ? null : trades.Min(t => t.Result),
            losses.Count == 0 ? null : Round(wins.Sum() / -losses.Sum(), 2),
            trades.Sum(t => t.Volume),
            trades.Sum(t => t.Result),
            trades.Sum(t => t.Commission));
    }

    /// <summary>
    /// Each day with a closed position or counted as a trading day, newest first. A position belongs to the trading
    /// day it closed on, by the challenge's trading day.
    /// </summary>
    public static List<DayResultResponse> Days(IReadOnlyList<ClosedTrade> trades, IReadOnlySet<DateOnly> countedDays, TradingDayDefinition tradingDay)
    {
        var closedByDay = trades.GroupBy(t => TradingDays.DayOf(t.ClosedAt, tradingDay)).ToDictionary(g => g.Key, g => g.ToList());
        return
        [
            .. closedByDay.Keys.Union(countedDays).OrderDescending().Select(day =>
            {
                var closed = closedByDay.GetValueOrDefault(day) ?? [];
                return new DayResultResponse(day, closed.Count, closed.Sum(t => t.Volume), closed.Sum(t => t.Result), countedDays.Contains(day));
            }),
        ];
    }

    /// <summary>
    /// Equity now against the balance when the trading day started, without what was deposited or withdrawn since.
    /// An account opened during the day started it at <paramref name="initialBalance"/>. Null without equity, or
    /// until the history has the account.
    /// </summary>
    public static decimal? Today(decimal? equity, DayStart? dayStart, decimal initialBalance) =>
        equity is { } now && dayStart is { HasHistory: true } start ? now - (start.Balance ?? initialBalance) - start.Adjustments : null;

    /// <summary><paramref name="part"/> as a percentage of <paramref name="whole"/>, to two decimals.</summary>
    public static decimal Percent(decimal part, decimal whole) => Round(part / whole * 100m, 2);

    /// <summary>How far <paramref name="gained"/> has come towards <paramref name="required"/>, from 0 to 100, to one decimal.</summary>
    public static decimal Progress(decimal gained, decimal required) =>
        required <= 0m ? 100m : Math.Clamp(Round(gained / required * 100m, 1), 0m, 100m);

    private static decimal Round(decimal value, int decimals) => decimal.Round(value, decimals, MidpointRounding.AwayFromZero);
}
