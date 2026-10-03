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
/// followed by the funded stage.
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
    long LastSequence)
{
    public bool HasEnded => Status is ChallengeStatus.Failed or ChallengeStatus.Cancelled;

    public StageRules Rules => Definition.Stage(Stage);
}

/// <summary>The new state and what the rule engine decided.</summary>
public sealed record ChallengeStep(ChallengeState State, IReadOnlyList<ChallengeOutput> Outputs);
