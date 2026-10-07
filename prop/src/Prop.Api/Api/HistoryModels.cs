using Prop.Api.History;
using Prop.Api.Trading;

namespace Prop.Api.Api;

/// <summary>
/// How a stage of the challenge has gone, from its trading history: the balance after every change, the levels of
/// the loss limits, the result of each day and statistics over the closed positions. All amounts are in the
/// challenge's currency, and results are after commissions.
/// </summary>
public sealed record PerformanceResponse(
    int Stage,
    string StageName,
    string? TradingAccountId,
    decimal InitialBalance,
    decimal? ProfitTarget,
    IReadOnlyList<BalancePoint> Balance,
    IReadOnlyList<FloorPoint> DailyFloor,
    IReadOnlyList<FloorPoint> MaxLossFloor,
    IReadOnlyList<DayResultResponse> Days,
    TradeStatisticsResponse Statistics);

/// <summary>The balance after a change, and what changed it by how much.</summary>
public sealed record BalancePoint(DateTimeOffset Time, BalanceChangeKind Kind, decimal Change, decimal Balance);

/// <summary>A loss limit's level from <paramref name="Time"/>, until the next point.</summary>
public sealed record FloorPoint(DateTimeOffset Time, decimal Level);

/// <summary>
/// One trading day of the stage: the positions closed on it, their volume in lots and their result, and whether the
/// rule engine counted it as a trading day, which it does when a position was opened on it.
/// </summary>
public sealed record DayResultResponse(DateOnly Day, int Trades, decimal Lots, decimal Result, bool Counted);

/// <summary>
/// The closed positions of the stage. A win or loss is a result above or below zero. <paramref name="ProfitFactor"/>
/// is what the wins made divided by what the losses lost, and null without losses.
/// </summary>
public sealed record TradeStatisticsResponse(
    int Trades,
    int Wins,
    int Losses,
    decimal? WinRatePercent,
    decimal? AverageWin,
    decimal? AverageLoss,
    decimal? BestTrade,
    decimal? WorstTrade,
    decimal? ProfitFactor,
    decimal Lots,
    decimal Result,
    decimal Commission);

/// <summary>
/// The limits the trader set for themselves on the account (ADR 0054), which the firm sees but cannot change.
/// <paramref name="Now"/> is null while the stage is not traded or the trading platform cannot be reached.
/// <paramref name="Locks"/> are the times new orders were locked in the last 30 days, newest first.
/// </summary>
public sealed record OwnLimitsResponse(TradingOwnLimits? Now, IReadOnlyList<LockedDayResponse> Locks);

/// <summary>
/// New orders were locked at <paramref name="Time"/> until <paramref name="Until"/>, the next trading day. <paramref name="Limit"/>
/// is the amount of the trader's own limit that was reached, null when the trader locked the day. <paramref name="DayResult"/>
/// is equity at the lock less the balance the day started with, and <paramref name="PositionsClosed"/> how many positions
/// the lock closed.
/// </summary>
public sealed record LockedDayResponse(DateTimeOffset Time, OwnLockReason Reason, DateTimeOffset Until, decimal? Limit, decimal DayResult, int PositionsClosed);

/// <summary>Closed positions, newest first. Ask with <paramref name="Next"/> as <c>before</c> for the ones before them.</summary>
public sealed record TradesResponse(IReadOnlyList<TradeResponse> Trades, long? Next);

/// <summary>
/// A closed position. <paramref name="Profit"/> is before <paramref name="Commission"/>, which was charged for opening
/// and closing it, and <paramref name="Result"/> after. <paramref name="CloseReason"/> is the trading platform's, such
/// as Manual, StopLoss, TakeProfit, StopOut, EquityFloor or AccountClosed.
/// </summary>
public sealed record TradeResponse(
    string PositionId,
    string Symbol,
    TradeSide Side,
    decimal Volume,
    DateTimeOffset? OpenedAt,
    decimal OpenPrice,
    DateTimeOffset ClosedAt,
    decimal ClosePrice,
    decimal Profit,
    decimal Commission,
    decimal Result,
    string CloseReason)
{
    internal static TradeResponse From(ClosedTrade trade) =>
        new(
            trade.PositionId,
            trade.Symbol,
            trade.Side,
            trade.Volume,
            trade.OpenedAt,
            trade.OpenPrice,
            trade.ClosedAt,
            trade.ClosePrice,
            trade.Profit,
            trade.Commission,
            trade.Result,
            trade.CloseReason);
}

/// <summary>The trader's payouts from every account, newest first, with totals per currency.</summary>
public sealed record TraderPayoutsResponse(IReadOnlyList<PayoutResponse> Payouts, IReadOnlyList<PayoutTotalResponse> Totals);

/// <summary>
/// What the firm has paid the trader, what is on its way (asked for and not yet paid or rejected), and what the
/// trader can ask for right now from funded accounts.
/// </summary>
public sealed record PayoutTotalResponse(string Currency, decimal Paid, decimal OnTheWay, decimal ReadyToRequest);
