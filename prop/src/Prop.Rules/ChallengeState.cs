using System.Collections.Immutable;

namespace Prop.Rules;

public enum ChallengeStatus
{
    /// <summary>Waiting for the trading account of the current stage.</summary>
    OpeningAccount,

    /// <summary>The current stage is being traded.</summary>
    Active,

    /// <summary>Every evaluation stage is passed. Waiting for the firm to approve funding.</summary>
    AwaitingFunding,

    Failed,
    Cancelled,
}

/// <summary>The latest balance and number of open positions reported by the trading platform.</summary>
public sealed record AccountFigures(decimal Balance, int OpenPositions);

/// <summary>
/// Where a challenge stands. Created by <see cref="ChallengeRules.Start"/> and changed only by
/// <see cref="ChallengeRules.Apply"/>. <paramref name="Stage"/> counts the evaluation stages from 0,
/// followed by the funded stage. On the funded stage, <paramref name="TradingDays"/> are those since the
/// last payout. <paramref name="Payout"/> is the payout in progress, until it is paid, rejected or failed.
/// <para>
/// While the stage is traded, <paramref name="StageDeadline"/> is the trading day the stage fails on unless it
/// was passed before, and <paramref name="InactivityDeadline"/> the trading day the challenge ends on unless a
/// position is opened before. <paramref name="PausedOn"/> is the trading day the challenge was paused, while it
/// is: the deadlines then move on by the days it was paused.
/// </para>
/// </summary>
public sealed record ChallengeState(
    string ChallengeId,
    ChallengeDefinition Definition,
    int Stage,
    ChallengeStatus Status,
    string? AccountId,
    DateOnly? CurrentDay,
    ImmutableSortedSet<DateOnly> TradingDays,
    AccountFigures? Account,
    long LastSequence,
    Payout? Payout = null,
    DateOnly? StageDeadline = null,
    DateOnly? InactivityDeadline = null,
    DateOnly? PausedOn = null)
{
    public bool HasEnded => Status is ChallengeStatus.Failed or ChallengeStatus.Cancelled;

    public bool IsPaused => PausedOn is not null;

    public bool IsFunded => Stage == Definition.FundedStage;

    public StageRules Rules => Definition.Stage(Stage);
}

/// <summary>The new state and what the rule engine decided.</summary>
public sealed record ChallengeStep(ChallengeState State, IReadOnlyList<ChallengeOutput> Outputs);
