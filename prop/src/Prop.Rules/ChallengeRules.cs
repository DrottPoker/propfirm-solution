using System.Collections.Immutable;

namespace Prop.Rules;

/// <summary>
/// The rules of a challenge as pure functions: the same state and input always give the same result.
/// The trading platform is the authority on equity and enforces the loss limits as floors on every price.
/// The rule engine sets those floors, resets the daily floor every trading day, counts trading days and
/// decides when a stage is passed.
/// </summary>
public static class ChallengeRules
{
    /// <summary>Starts a challenge with a copy of the definition and asks for the first stage's account.</summary>
    public static ChallengeStep Start(string challengeId, ChallengeDefinition definition, DateTimeOffset time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(challengeId);
        var errors = definition.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException($"Invalid challenge definition: {string.Join(" ", errors)}", nameof(definition));
        }

        var state = new ChallengeState(
            challengeId,
            definition,
            Stage: 0,
            ChallengeStatus.OpeningAccount,
            AccountId: null,
            CurrentDay: null,
            ImmutableSortedSet<DateOnly>.Empty,
            Account: null,
            LastSequence: 0);
        return new ChallengeStep(state, [new OpenAccountRequested(time, 0, definition.InitialBalance, definition.Currency)]);
    }

    public static ChallengeStep Apply(ChallengeState state, ChallengeInput input) => input switch
    {
        AccountOpened opened => OnAccountOpened(state, opened),
        TradingDayStarted day => OnTradingDayStarted(state, day),
        ApproveFunding approve => OnApproveFunding(state, approve),
        CancelChallenge cancel => OnCancel(state, cancel),

        // Repeated, late and other accounts' facts change nothing.
        AccountFact fact when !IsNewFactForCurrentAccount(state, fact) => Unchanged(state),
        AccountUpdated updated => OnAccountUpdated(state with { LastSequence = updated.Sequence }, updated),
        PositionOpened opened => OnPositionOpened(state with { LastSequence = opened.Sequence }, opened),
        FloorBreached breached => OnFloorBreached(state with { LastSequence = breached.Sequence }, breached),
        AccountDisabled disabled => OnAccountDisabled(state with { LastSequence = disabled.Sequence }, disabled),
        _ => throw new ArgumentException($"Unknown input {input.GetType().Name}.", nameof(input)),
    };

    private static ChallengeStep OnAccountOpened(ChallengeState state, AccountOpened input)
    {
        if (state.HasEnded)
        {
            // The account was opened while the challenge ended, so it must not stay open.
            return new ChallengeStep(state, [new CloseAccountRequested(input.Time, input.AccountId)]);
        }

        if (state.Status != ChallengeStatus.OpeningAccount)
        {
            return input.AccountId == state.AccountId ? Unchanged(state) : Ignored(state, input, "No account is being opened.");
        }

        var initial = state.Definition.InitialBalance;
        var started = state with
        {
            Status = ChallengeStatus.Active,
            AccountId = input.AccountId,
            CurrentDay = state.CurrentDay is { } current && current > input.Day ? current : input.Day,
            TradingDays = ImmutableSortedSet<DateOnly>.Empty,
            Account = new AccountFigures(initial, 0),
            LastSequence = Math.Max(state.LastSequence, input.Sequence),
        };
        return new ChallengeStep(
            started,
            [
                new StageStarted(input.Time, started.Stage, input.AccountId),
                new FloorRequested(input.Time, input.AccountId, FloorIds.MaxLoss, MaxLossFloor(started)),
                DailyFloor(started, input.Time),
            ]);
    }

    private static ChallengeStep OnTradingDayStarted(ChallengeState state, TradingDayStarted input)
    {
        if (state.CurrentDay is { } current && input.Day <= current)
        {
            return Unchanged(state);
        }

        var next = state with { CurrentDay = input.Day };
        return next.Status == ChallengeStatus.Active ? new ChallengeStep(next, [DailyFloor(next, input.Time)]) : Unchanged(next);
    }

    private static ChallengeStep OnApproveFunding(ChallengeState state, ApproveFunding input)
    {
        if (state.Status != ChallengeStatus.AwaitingFunding)
        {
            return Ignored(state, input, "The challenge is not waiting for funding.");
        }

        var definition = state.Definition;
        return new ChallengeStep(
            state with { Status = ChallengeStatus.OpeningAccount },
            [new OpenAccountRequested(input.Time, state.Stage, definition.InitialBalance, definition.Currency)]);
    }

    private static ChallengeStep OnCancel(ChallengeState state, CancelChallenge input)
    {
        if (state.HasEnded)
        {
            return Ignored(state, input, "The challenge has already ended.");
        }

        List<ChallengeOutput> outputs = [];
        if (state is { Status: ChallengeStatus.Active, AccountId: { } accountId })
        {
            outputs.Add(new CloseAccountRequested(input.Time, accountId));
        }

        outputs.Add(new ChallengeCancelled(input.Time, input.Reason));
        return new ChallengeStep(state with { Status = ChallengeStatus.Cancelled }, outputs);
    }

    private static ChallengeStep OnAccountUpdated(ChallengeState state, AccountUpdated input)
    {
        var updated = state with { Account = new AccountFigures(input.Balance, input.OpenPositions) };
        return IsTargetReached(updated) ? Pass(updated, input.Time) : Unchanged(updated);
    }

    private static ChallengeStep OnPositionOpened(ChallengeState state, PositionOpened input)
    {
        if (state.TradingDays.Contains(input.Day))
        {
            return Unchanged(state);
        }

        var counted = state with { TradingDays = state.TradingDays.Add(input.Day) };
        return new ChallengeStep(counted, [new TradingDayCounted(input.Time, counted.Stage, input.Day, counted.TradingDays.Count)]);
    }

    private static ChallengeStep OnFloorBreached(ChallengeState state, FloorBreached input)
    {
        var reason = input.FloorId switch
        {
            FloorIds.Daily => FailureReason.DailyLoss,
            FloorIds.MaxLoss => FailureReason.MaxLoss,
            _ => FailureReason.OtherFloor,
        };

        // The trading platform has already closed the positions and disabled the account.
        return new ChallengeStep(
            state with { Status = ChallengeStatus.Failed },
            [new ChallengeFailed(input.Time, state.Stage, input.AccountId, reason, input.FloorId, input.Level, input.Equity)]);
    }

    private static ChallengeStep OnAccountDisabled(ChallengeState state, AccountDisabled input) =>
        new(
            state with { Status = ChallengeStatus.Cancelled },
            [new ChallengeCancelled(input.Time, "The trading account was disabled on the trading platform.")]);

    // The target is measured on the balance, so it only counts once every position is closed.
    private static bool IsTargetReached(ChallengeState state) =>
        state.Rules.ProfitTargetPercent is { } target
        && state.Account is { OpenPositions: 0 } account
        && account.Balance >= state.Definition.InitialBalance + state.Definition.PercentOfInitialBalance(target)
        && state.TradingDays.Count >= state.Rules.MinTradingDays;

    private static ChallengeStep Pass(ChallengeState state, DateTimeOffset time)
    {
        var accountId = state.AccountId!;
        List<ChallengeOutput> outputs =
        [
            new StagePassed(time, state.Stage, accountId, state.Account!.Balance, state.TradingDays.Count),
            new CloseAccountRequested(time, accountId),
        ];

        var definition = state.Definition;
        var next = state with
        {
            Stage = state.Stage + 1,
            AccountId = null,
            TradingDays = ImmutableSortedSet<DateOnly>.Empty,
            Account = null,
        };

        if (next.Stage < definition.FundedStage)
        {
            outputs.Add(new OpenAccountRequested(time, next.Stage, definition.InitialBalance, definition.Currency));
            return new ChallengeStep(next with { Status = ChallengeStatus.OpeningAccount }, outputs);
        }

        outputs.Add(new FundingAwaited(time));
        return new ChallengeStep(next with { Status = ChallengeStatus.AwaitingFunding }, outputs);
    }

    private static FloorSpec MaxLossFloor(ChallengeState state)
    {
        var initial = state.Definition.InitialBalance;
        var distance = state.Definition.PercentOfInitialBalance(state.Rules.MaxLoss.Percent);
        return state.Rules.MaxLoss.Kind == MaxLossKind.Fixed ? new FixedFloor(initial - distance) : new TrailingFloor(distance, initial);
    }

    private static FloorRequested DailyFloor(ChallengeState state, DateTimeOffset time)
    {
        var rule = state.Rules.DailyLoss;
        return new FloorRequested(
            time,
            state.AccountId!,
            FloorIds.Daily,
            new StartOfDayFloor(state.Definition.PercentOfInitialBalance(rule.Percent), rule.Reference));
    }

    private static bool IsNewFactForCurrentAccount(ChallengeState state, AccountFact fact) =>
        state.Status == ChallengeStatus.Active && fact.AccountId == state.AccountId && fact.Sequence > state.LastSequence;

    private static ChallengeStep Unchanged(ChallengeState state) => new(state, []);

    private static ChallengeStep Ignored(ChallengeState state, ChallengeInput input, string reason) =>
        new(state, [new InputIgnored(input.Time, input.GetType().Name, reason)]);
}
