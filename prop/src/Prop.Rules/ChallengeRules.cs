using System.Collections.Immutable;

namespace Prop.Rules;

/// <summary>
/// The rules of a challenge as pure functions: the same state and input always give the same result.
/// The trading platform is the authority on equity and enforces the loss limits as floors on every price.
/// The rule engine sets those floors, resets the daily floor every trading day, counts trading days,
/// decides when a stage is passed or has run out of time and takes the funded trader's payouts through to paid.
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
        PauseChallenge pause => OnPause(state, pause),
        ResumeChallenge resume => OnResume(state, resume),
        ApproveFunding approve => OnApproveFunding(state, approve),
        CancelChallenge cancel => OnCancel(state, cancel),
        RequestPayout request => OnRequestPayout(state, request),
        ApprovePayout approve => OnApprovePayout(state, approve),
        MarkPayoutPaid paid => OnMarkPayoutPaid(state, paid),
        RejectPayout reject => OnRejectPayout(state, reject),
        WithdrawalRejected rejected => OnWithdrawalRejected(state, rejected),

        // The payout's withdrawal counts even if the challenge ended after it was requested.
        BalanceAdjusted adjusted when IsPayoutWithdrawal(state, adjusted) => OnPayoutWithdrawn(state, adjusted),

        // Repeated, late and other accounts' facts change nothing.
        AccountFact fact when !IsNewFactForCurrentAccount(state, fact) => Unchanged(state),
        AccountUpdated updated => OnAccountUpdated(state with { LastSequence = updated.Sequence }, updated),
        PositionOpened opened => OnPositionOpened(state with { LastSequence = opened.Sequence }, opened),
        FloorBreached breached => OnFloorBreached(state with { LastSequence = breached.Sequence }, breached),
        AccountDisabled disabled => OnAccountDisabled(state with { LastSequence = disabled.Sequence }, disabled),

        // Deposits and other withdrawals are not trading results: only the balance changes.
        BalanceAdjusted adjusted => Unchanged(state with
        {
            LastSequence = adjusted.Sequence,
            Account = state.Account! with { Balance = adjusted.Balance },
        }),
        _ => throw new ArgumentException($"Unknown input {input.GetType().Name}.", nameof(input)),
    };

    /// <summary>
    /// What a payout requested now would pay, or why the trader cannot request one: the account must be an
    /// active funded account with a profit, no open positions, enough trading days since the last payout and
    /// no other payout in progress. Measured on the balance the trading platform last reported.
    /// </summary>
    public static PayoutQuote QuotePayout(ChallengeState state)
    {
        var rules = state.Rules;
        var split = rules.ProfitSplitPercent ?? 0m;
        var profit = state.Account is { } account ? account.Balance - state.Definition.InitialBalance : 0m;
        var amount = profit > 0m ? decimal.Round(profit * split / 100m, 2, MidpointRounding.ToZero) : 0m;
        var tradingDays = state.TradingDays.Count;

        string? refusal = null;
        if (!state.IsFunded || state.Status != ChallengeStatus.Active)
        {
            refusal = "Payouts are only for an active funded account.";
        }
        else if (rules.ProfitSplitPercent is null)
        {
            refusal = "The challenge has no profit split, so it has no payouts.";
        }
        else if (state.Payout is not null)
        {
            refusal = "A payout is already in progress.";
        }
        else if (amount <= 0m)
        {
            refusal = "There is no profit to pay out.";
        }
        else if (state.Account!.OpenPositions > 0)
        {
            refusal = "Close every position before asking for a payout.";
        }
        else if (tradingDays < rules.MinTradingDays)
        {
            refusal = $"A payout needs {rules.MinTradingDays} trading days since the last one. So far: {tradingDays}.";
        }

        return new PayoutQuote(Math.Max(profit, 0m), split, amount, tradingDays, rules.MinTradingDays, refusal);
    }

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

        // The deadlines count from the day the stage starts. A stage that starts while the challenge is paused
        // is paused from its first day, so its deadlines move on by the days from then.
        var initial = state.Definition.InitialBalance;
        var started = state with
        {
            Status = ChallengeStatus.Active,
            AccountId = input.AccountId,
            CurrentDay = state.CurrentDay is { } current && current > input.Day ? current : input.Day,
            TradingDays = ImmutableSortedSet<DateOnly>.Empty,
            Account = new AccountFigures(initial, 0),
            LastSequence = Math.Max(state.LastSequence, input.Sequence),
            StageDeadline = state.Rules.MaxDays is { } maxDays ? input.Day.AddDays(maxDays + 1) : null,
            InactivityDeadline = InactivityDeadlineAfter(state.Definition, input.Day),
            PausedOn = state.PausedOn is { } pausedOn && pausedOn < input.Day ? input.Day : state.PausedOn,
        };
        List<ChallengeOutput> outputs =
        [
            new StageStarted(input.Time, started.Stage, input.AccountId),
            new FloorRequested(input.Time, input.AccountId, FloorIds.MaxLoss, MaxLossFloor(started)),
            DailyFloor(started, input.Time),
        ];
        if (started.IsPaused)
        {
            outputs.Add(new SuspendAccountRequested(input.Time, input.AccountId));
        }

        return new ChallengeStep(started, outputs);
    }

    // A stage that runs out of time ends when the day starts. A paused challenge's days do not count.
    private static ChallengeStep OnTradingDayStarted(ChallengeState state, TradingDayStarted input)
    {
        if (state.CurrentDay is { } current && input.Day <= current)
        {
            return Unchanged(state);
        }

        var next = state with { CurrentDay = input.Day };
        if (next.Status != ChallengeStatus.Active)
        {
            return Unchanged(next);
        }

        if (!next.IsPaused && ExpiryOn(next, input.Day) is { } reason)
        {
            var accountId = next.AccountId!;
            return new ChallengeStep(
                next with { Status = ChallengeStatus.Failed },
                [new ChallengeExpired(input.Time, next.Stage, accountId, reason, input.Day), new CloseAccountRequested(input.Time, accountId)]);
        }

        return new ChallengeStep(next, [DailyFloor(next, input.Time)]);
    }

    private static ChallengeStep OnPause(ChallengeState state, PauseChallenge input)
    {
        if (state.HasEnded || state.IsPaused)
        {
            return Unchanged(state);
        }

        List<ChallengeOutput> outputs = [new ChallengePaused(input.Time)];
        if (state is { Status: ChallengeStatus.Active, AccountId: { } accountId })
        {
            outputs.Add(new SuspendAccountRequested(input.Time, accountId));
        }

        return new ChallengeStep(state with { PausedOn = input.Day }, outputs);
    }

    // The deadlines move on by the days the challenge was paused, so the trader loses no time.
    private static ChallengeStep OnResume(ChallengeState state, ResumeChallenge input)
    {
        if (state.PausedOn is not { } pausedOn)
        {
            return Unchanged(state);
        }

        var days = Math.Max(0, input.Day.DayNumber - pausedOn.DayNumber);
        var resumed = state with
        {
            PausedOn = null,
            StageDeadline = state.StageDeadline?.AddDays(days),
            InactivityDeadline = state.InactivityDeadline?.AddDays(days),
        };
        List<ChallengeOutput> outputs = [new ChallengeResumed(input.Time, days)];
        if (state is { Status: ChallengeStatus.Active, AccountId: { } accountId })
        {
            outputs.Add(new ResumeAccountRequested(input.Time, accountId));
        }

        return new ChallengeStep(resumed, outputs);
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
        return new ChallengeStep(state with { Status = ChallengeStatus.Cancelled, PausedOn = null }, outputs);
    }

    // The whole profit is withdrawn at once, so the trader cannot keep trading on money that is being paid out.
    private static ChallengeStep OnRequestPayout(ChallengeState state, RequestPayout input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.PayoutId);
        var quote = QuotePayout(state);
        if (quote.Refusal is { } refusal)
        {
            return Ignored(state, input, refusal);
        }

        var accountId = state.AccountId!;
        var payout = new Payout(input.PayoutId, accountId, quote.Profit, quote.ProfitSplitPercent, quote.Amount, PayoutStatus.Withdrawing, input.Time);
        return new ChallengeStep(
            state with { Payout = payout },
            [
                new PayoutRequested(input.Time, payout),
                new WithdrawalRequested(input.Time, accountId, payout.Id, quote.Profit, state.Definition.InitialBalance),
            ]);
    }

    // A new payout period starts on the account, with the trading days counted from now.
    private static ChallengeStep OnPayoutWithdrawn(ChallengeState state, BalanceAdjusted input)
    {
        var payout = state.Payout! with { Status = PayoutStatus.Pending };
        var next = state is { Status: ChallengeStatus.Active } && state.AccountId == input.AccountId && input.Sequence > state.LastSequence
            ? state with
            {
                Payout = payout,
                TradingDays = ImmutableSortedSet<DateOnly>.Empty,
                Account = state.Account! with { Balance = input.Balance },
                LastSequence = input.Sequence,
            }
            : state with { Payout = payout };
        return new ChallengeStep(next, [new PayoutWithdrawn(input.Time, payout, input.Balance)]);
    }

    private static ChallengeStep OnWithdrawalRejected(ChallengeState state, WithdrawalRejected input)
    {
        if (state.Payout is not { Status: PayoutStatus.Withdrawing } payout || payout.Id != input.OperationId)
        {
            return Unchanged(state);
        }

        var failed = payout with { Status = PayoutStatus.Failed };
        return new ChallengeStep(state with { Payout = null }, [new PayoutFailed(input.Time, failed, input.Reason)]);
    }

    private static ChallengeStep OnApprovePayout(ChallengeState state, ApprovePayout input)
    {
        if (state.Payout is not { Status: PayoutStatus.Pending } payout || payout.Id != input.PayoutId)
        {
            return Ignored(state, input, "The payout is not waiting for approval.");
        }

        var approved = payout with { Status = PayoutStatus.Approved };
        return new ChallengeStep(state with { Payout = approved }, [new PayoutApproved(input.Time, approved)]);
    }

    private static ChallengeStep OnMarkPayoutPaid(ChallengeState state, MarkPayoutPaid input)
    {
        if (state.Payout is not { Status: PayoutStatus.Approved } payout || payout.Id != input.PayoutId)
        {
            return Ignored(state, input, "Only an approved payout can be marked as paid.");
        }

        var paid = payout with { Status = PayoutStatus.Paid };
        return new ChallengeStep(state with { Payout = null }, [new PayoutPaid(input.Time, paid, input.Reference)]);
    }

    private static ChallengeStep OnRejectPayout(ChallengeState state, RejectPayout input)
    {
        if (state.Payout is not { Status: PayoutStatus.Pending or PayoutStatus.Approved } payout || payout.Id != input.PayoutId)
        {
            return Ignored(state, input, "The payout is not waiting for the firm.");
        }

        var rejected = payout with { Status = PayoutStatus.Rejected };
        return new ChallengeStep(state with { Payout = null }, [new PayoutRejected(input.Time, rejected, input.Reason)]);
    }

    private static bool IsPayoutWithdrawal(ChallengeState state, BalanceAdjusted input) =>
        state.Payout is { Status: PayoutStatus.Withdrawing } payout && payout.Id == input.OperationId && payout.AccountId == input.AccountId;

    private static ChallengeStep OnAccountUpdated(ChallengeState state, AccountUpdated input)
    {
        var updated = state with { Account = new AccountFigures(input.Balance, input.OpenPositions) };
        return IsTargetReached(updated) ? Pass(updated, input.Time) : Unchanged(updated);
    }

    // The open position counts until the platform reports the positions left after a close. A new position
    // also restarts the days allowed without one.
    private static ChallengeStep OnPositionOpened(ChallengeState state, PositionOpened input)
    {
        var deadline = InactivityDeadlineAfter(state.Definition, input.Day);
        var opened = state with
        {
            Account = state.Account! with { OpenPositions = state.Account.OpenPositions + 1 },
            InactivityDeadline = state.InactivityDeadline is { } current && current > deadline ? current : deadline,
        };
        if (opened.TradingDays.Contains(input.Day))
        {
            return Unchanged(opened);
        }

        var counted = opened with { TradingDays = opened.TradingDays.Add(input.Day) };
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
            state with { Status = ChallengeStatus.Failed, PausedOn = null },
            [new ChallengeFailed(input.Time, state.Stage, input.AccountId, reason, input.FloorId, input.Level, input.Equity)]);
    }

    private static ChallengeStep OnAccountDisabled(ChallengeState state, AccountDisabled input) =>
        new(
            state with { Status = ChallengeStatus.Cancelled, PausedOn = null },
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
            StageDeadline = null,
            InactivityDeadline = null,
        };

        if (next.Stage < definition.FundedStage)
        {
            outputs.Add(new OpenAccountRequested(time, next.Stage, definition.InitialBalance, definition.Currency));
            return new ChallengeStep(next with { Status = ChallengeStatus.OpeningAccount }, outputs);
        }

        outputs.Add(new FundingAwaited(time));
        return new ChallengeStep(next with { Status = ChallengeStatus.AwaitingFunding }, outputs);
    }

    // The time limit comes first when both run out on the same day.
    private static ExpiryReason? ExpiryOn(ChallengeState state, DateOnly day) =>
        state.StageDeadline is { } stageDeadline && day >= stageDeadline ? ExpiryReason.TimeLimit
        : state.InactivityDeadline is { } inactivityDeadline && day >= inactivityDeadline ? ExpiryReason.Inactivity
        : null;

    // Activity on a day leaves that day and the inactivity days after it. The challenge ends when the next one starts.
    private static DateOnly? InactivityDeadlineAfter(ChallengeDefinition definition, DateOnly day) =>
        definition.InactivityDays is { } days ? day.AddDays(days + 1) : null;

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
