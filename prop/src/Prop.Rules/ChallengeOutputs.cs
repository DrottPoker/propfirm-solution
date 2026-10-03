namespace Prop.Rules;

/// <summary>What the rule engine decided or asks the service to do.</summary>
public abstract record ChallengeOutput(DateTimeOffset Time);

/// <summary>Open a trading account for the stage, with the challenge's initial balance.</summary>
public sealed record OpenAccountRequested(DateTimeOffset Time, int Stage, decimal InitialBalance, string Currency) : ChallengeOutput(Time);

/// <summary>Set or replace a floor on the account. The trading platform checks it on every price.</summary>
public sealed record FloorRequested(DateTimeOffset Time, string AccountId, string FloorId, FloorSpec Floor) : ChallengeOutput(Time);

/// <summary>Close the account, after its stage was passed or the challenge was cancelled.</summary>
public sealed record CloseAccountRequested(DateTimeOffset Time, string AccountId) : ChallengeOutput(Time);

public sealed record StageStarted(DateTimeOffset Time, int Stage, string AccountId) : ChallengeOutput(Time);

/// <summary>Another day with at least one opened position.</summary>
public sealed record TradingDayCounted(DateTimeOffset Time, int Stage, DateOnly Day, int TradingDays) : ChallengeOutput(Time);

public sealed record StagePassed(DateTimeOffset Time, int Stage, string AccountId, decimal Balance, int TradingDays) : ChallengeOutput(Time);

/// <summary>Every evaluation stage is passed. The firm decides when the trader gets the funded account.</summary>
public sealed record FundingAwaited(DateTimeOffset Time) : ChallengeOutput(Time);

public enum FailureReason
{
    DailyLoss,
    MaxLoss,

    /// <summary>A floor the rule engine did not set, for example one the firm set directly on the trading platform.</summary>
    OtherFloor,
}

/// <summary>A floor was breached. <paramref name="Level"/> and <paramref name="Equity"/> come from the trading platform's evidence.</summary>
public sealed record ChallengeFailed(
    DateTimeOffset Time,
    int Stage,
    string AccountId,
    FailureReason Reason,
    string FloorId,
    decimal Level,
    decimal Equity) : ChallengeOutput(Time);

public sealed record ChallengeCancelled(DateTimeOffset Time, string Reason) : ChallengeOutput(Time);

/// <summary>The trader asked for a payout. Its profit is withdrawn next.</summary>
public sealed record PayoutRequested(DateTimeOffset Time, Payout Payout) : ChallengeOutput(Time);

/// <summary>
/// Withdraw <paramref name="Amount"/> from the account, with <paramref name="OperationId"/> so it happens
/// once, but only if at least <paramref name="MinBalance"/> is left. The trading platform refuses it if the
/// balance fell since the payout was requested.
/// </summary>
public sealed record WithdrawalRequested(DateTimeOffset Time, string AccountId, string OperationId, decimal Amount, decimal MinBalance)
    : ChallengeOutput(Time);

/// <summary>The profit is off the trading account, which starts a new payout period. The firm should approve the payout.</summary>
public sealed record PayoutWithdrawn(DateTimeOffset Time, Payout Payout, decimal BalanceAfter) : ChallengeOutput(Time);

public sealed record PayoutApproved(DateTimeOffset Time, Payout Payout) : ChallengeOutput(Time);

public sealed record PayoutPaid(DateTimeOffset Time, Payout Payout, string? Reference) : ChallengeOutput(Time);

/// <summary>The firm refused the payout. The withdrawn profit stays off the account.</summary>
public sealed record PayoutRejected(DateTimeOffset Time, Payout Payout, string Reason) : ChallengeOutput(Time);

/// <summary>The withdrawal was refused, so the payout did not happen and the account is as it was.</summary>
public sealed record PayoutFailed(DateTimeOffset Time, Payout Payout, string Reason) : ChallengeOutput(Time);

/// <summary>An input from the service, the firm or the trader that does not fit the challenge's state. Facts are never reported here.</summary>
public sealed record InputIgnored(DateTimeOffset Time, string Input, string Reason) : ChallengeOutput(Time);

/// <summary>The floor names the rule engine uses on the trading platform.</summary>
public static class FloorIds
{
    public const string Daily = "daily";
    public const string MaxLoss = "max-loss";
}

/// <summary>A floor for the trading platform to enforce.</summary>
public abstract record FloorSpec;

/// <summary>
/// The level is the day's starting point minus <paramref name="Distance"/>. The trading platform fixes the
/// level when it applies the floor, from the account as it is at that moment.
/// </summary>
public sealed record StartOfDayFloor(decimal Distance, DailyLossReference Reference) : FloorSpec;

public sealed record FixedFloor(decimal Level) : FloorSpec;

/// <summary>Trails the highest equity by <paramref name="Distance"/> and stops rising at <paramref name="LockLevel"/>.</summary>
public sealed record TrailingFloor(decimal Distance, decimal LockLevel) : FloorSpec;
