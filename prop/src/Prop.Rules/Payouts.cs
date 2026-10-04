namespace Prop.Rules;

public enum PayoutStatus
{
    /// <summary>The trader asked for the payout. The profit is being taken off the trading account.</summary>
    Withdrawing,

    /// <summary>The profit is off the account. Waiting for the firm to approve.</summary>
    Pending,

    /// <summary>The firm approved and sends the money itself.</summary>
    Approved,

    /// <summary>The firm marked the payout as paid.</summary>
    Paid,

    /// <summary>The firm refused the payout. The withdrawn profit is returned only when the firm chose so.</summary>
    Rejected,

    /// <summary>The trading platform refused the withdrawal, so nothing was taken off the account.</summary>
    Failed,
}

/// <summary>
/// A funded trader's payout. The whole <paramref name="Profit"/> above the initial balance is withdrawn
/// from <paramref name="AccountId"/>, which starts over from the initial balance. The trader gets
/// <paramref name="Amount"/>, which is <paramref name="ProfitSplitPercent"/> of the profit rounded down to
/// whole cents. The firm keeps the rest.
/// </summary>
public sealed record Payout(
    string Id,
    string AccountId,
    decimal Profit,
    decimal ProfitSplitPercent,
    decimal Amount,
    PayoutStatus Status,
    DateTimeOffset RequestedAt);

/// <summary>
/// What a payout requested now would pay, or why one cannot be requested. Made by
/// <see cref="ChallengeRules.QuotePayout"/> from the figures the trading platform last reported. With a consistency rule,
/// <paramref name="BestDayProfit"/> is what the best trading day since the last payout made, and
/// <paramref name="ConsistencyPercent"/> the most of the profit it may be.
/// </summary>
public sealed record PayoutQuote(
    decimal Profit,
    decimal ProfitSplitPercent,
    decimal Amount,
    int TradingDays,
    int MinTradingDays,
    string? Refusal,
    decimal? BestDayProfit = null,
    decimal? ConsistencyPercent = null)
{
    public bool CanRequest => Refusal is null;
}
