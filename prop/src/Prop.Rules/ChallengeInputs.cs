namespace Prop.Rules;

/// <summary>Input to a challenge. The inputs of a challenge are applied one at a time, in the order they arrived.</summary>
public abstract record ChallengeInput(DateTimeOffset Time);

/// <summary>
/// The trading account for the current stage was opened during trading day <paramref name="Day"/>.
/// <paramref name="Sequence"/> is the trading platform's sequence number of the account's creation, so
/// that the account's later facts are known to be newer.
/// </summary>
public sealed record AccountOpened(DateTimeOffset Time, string AccountId, long Sequence, DateOnly Day) : ChallengeInput(Time);

/// <summary>A new trading day started, by the challenge's trading day definition.</summary>
public sealed record TradingDayStarted(DateTimeOffset Time, DateOnly Day) : ChallengeInput(Time);

/// <summary>
/// The firm's month is not paid, so the challenge pauses during trading day <paramref name="Day"/>: the trader
/// cannot open new positions, and the days until the time limit and the inactivity rule stop counting.
/// </summary>
public sealed record PauseChallenge(DateTimeOffset Time, DateOnly Day) : ChallengeInput(Time);

/// <summary>The firm has paid, so the paused challenge goes on during trading day <paramref name="Day"/>.</summary>
public sealed record ResumeChallenge(DateTimeOffset Time, DateOnly Day) : ChallengeInput(Time);

/// <summary>The firm approved the trader for a funded account after its own checks, for example KYC and agreement.</summary>
public sealed record ApproveFunding(DateTimeOffset Time) : ChallengeInput(Time);

/// <summary>The firm ends the challenge.</summary>
public sealed record CancelChallenge(DateTimeOffset Time, string Reason) : ChallengeInput(Time);

/// <summary>
/// The firm opens the failed stage again on its trading account during trading day <paramref name="Day"/>, with
/// <paramref name="Balance"/>, for example after an outage broke a loss limit (ADR 0053). With
/// <paramref name="KeepTradingDays"/> the trading days counted so far still count.
/// </summary>
public sealed record ReinstateStage(DateTimeOffset Time, DateOnly Day, decimal Balance, bool KeepTradingDays) : ChallengeInput(Time);

/// <summary>The funded trader asks for a payout of the profit. The service chooses the payout's id.</summary>
public sealed record RequestPayout(DateTimeOffset Time, string PayoutId) : ChallengeInput(Time);

/// <summary>The firm approves the payout after its own checks, for example KYC.</summary>
public sealed record ApprovePayout(DateTimeOffset Time, string PayoutId) : ChallengeInput(Time);

/// <summary>The firm has sent the money. <paramref name="Reference"/> is the firm's own, for example a bank transfer id.</summary>
public sealed record MarkPayoutPaid(DateTimeOffset Time, string PayoutId, string? Reference) : ChallengeInput(Time);

/// <summary>
/// The firm refuses the payout. With <paramref name="ReturnProfit"/> the withdrawn profit goes back on the trader's account,
/// for example when the trader only has to finish the firm's checks; otherwise it is forfeited, for example for a broken rule.
/// </summary>
public sealed record RejectPayout(DateTimeOffset Time, string PayoutId, string Reason, bool ReturnProfit = false) : ChallengeInput(Time);

/// <summary>
/// The trading platform refused a withdrawal the rule engine asked for, for example because the balance
/// fell after the payout was requested. Reported by the service, which sees every refusal, also those that
/// never reach the trading engine.
/// </summary>
public sealed record WithdrawalRejected(DateTimeOffset Time, string OperationId, string Reason) : ChallengeInput(Time);

/// <summary>
/// A fact from the trading platform about an account. <paramref name="Sequence"/> is the platform's
/// sequence number. Facts can arrive more than once and late: a fact is only applied if it is about the
/// current account and newer than every fact applied before.
/// </summary>
public abstract record AccountFact(DateTimeOffset Time, string AccountId, long Sequence) : ChallengeInput(Time);

/// <summary>The balance and the number of open positions after a position was closed.</summary>
public sealed record AccountUpdated(DateTimeOffset Time, string AccountId, long Sequence, decimal Balance, int OpenPositions)
    : AccountFact(Time, AccountId, Sequence);

/// <summary>A position was opened during trading day <paramref name="Day"/>, which makes it a trading day.</summary>
public sealed record PositionOpened(DateTimeOffset Time, string AccountId, long Sequence, DateOnly Day)
    : AccountFact(Time, AccountId, Sequence);

/// <summary>
/// Equity fell below a floor, so the trading platform closed everything and disabled the account.
/// The platform's event holds the full evidence: prices and positions.
/// </summary>
public sealed record FloorBreached(DateTimeOffset Time, string AccountId, long Sequence, string FloorId, decimal Level, decimal Equity)
    : AccountFact(Time, AccountId, Sequence);

/// <summary>The account was disabled on the trading platform for another reason, for example by the firm.</summary>
public sealed record AccountDisabled(DateTimeOffset Time, string AccountId, long Sequence)
    : AccountFact(Time, AccountId, Sequence);

/// <summary>
/// Money was deposited (positive <paramref name="Amount"/>) or withdrawn (negative), for example a
/// payout's profit with the payout's id as <paramref name="OperationId"/>. Not a trading result.
/// </summary>
public sealed record BalanceAdjusted(DateTimeOffset Time, string AccountId, long Sequence, string OperationId, decimal Amount, decimal Balance)
    : AccountFact(Time, AccountId, Sequence);
