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

/// <summary>The firm approved the trader for a funded account after its own checks, for example KYC and agreement.</summary>
public sealed record ApproveFunding(DateTimeOffset Time) : ChallengeInput(Time);

/// <summary>The firm ends the challenge.</summary>
public sealed record CancelChallenge(DateTimeOffset Time, string Reason) : ChallengeInput(Time);

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
