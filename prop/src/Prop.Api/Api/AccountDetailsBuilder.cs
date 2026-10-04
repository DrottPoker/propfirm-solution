using Prop.Api.Challenges;
using Prop.Api.Firms;
using Prop.Api.History;
using Prop.Api.Payments;
using Prop.Api.Trading;
using Prop.Rules;

namespace Prop.Api.Api;

/// <summary>
/// Builds accounts as the portal shows them, for one account or all of a trader's at once: the trading accounts
/// valued right now, the stages, the results, the payouts, what a breach closed and how a failed challenge can be tried
/// again. The database is asked once per kind of data, not once per account, since the trader's dashboard asks every
/// few seconds.
/// </summary>
internal sealed class AccountDetailsBuilder(
    PayoutQueries payouts,
    TradingHistoryQueries history,
    ITradingPlatform trading,
    OrderService orders,
    DiscountStore discounts,
    TimeProvider time)
{
    public async Task<List<AccountDetailsResponse>> BuildAsync(Firm firm, IReadOnlyList<AccountView> views, CancellationToken cancellationToken)
    {
        if (views.Count == 0)
        {
            return [];
        }

        var now = time.GetUtcNow();
        var ids = views.Select(v => v.Account.Id).ToArray();
        var valuing = Task.WhenAll(views.Select(v => LiveAsync(firm, v, cancellationToken)));
        var stages = await history.StagesAsync(ids, cancellationToken);
        var accountPayouts = (await payouts.ListByAccountsAsync(firm.Id, ids, cancellationToken)).ToLookup(p => p.ChallengeAccountId);
        var sequences = await history.LastSequencesAsync(ids, cancellationToken);
        var dayStarts = await history.DayStartsAsync(
            [.. views.Where(IsTrading).Select(v => (v.Account.State.AccountId!, DayStartedAt(v.Account.State.Definition, now)))],
            cancellationToken);
        var breaches = await history.BreachClosesAsync([.. views.Select(v => v.Ending).OfType<ChallengeFailed>().Select(f => f.AccountId).Distinct()], cancellationToken);
        var retries = await RetriesAsync(firm, views, now, cancellationToken);
        var live = await valuing;
        return
        [
            .. views.Select((view, i) => Build(
                view,
                live[i],
                stages[view.Account.Id],
                accountPayouts[view.Account.Id],
                sequences.GetValueOrDefault(view.Account.Id),
                IsTrading(view) ? dayStarts.GetValueOrDefault(view.Account.State.AccountId!) : null,
                breaches,
                retries.GetValueOrDefault(view.Account.State.Definition.Id),
                now)),
        ];
    }

    // A failed challenge that is still for sale can be bought again, with the firm's best code for retries on it.
    private async Task<Dictionary<string, RetryOffer>> RetriesAsync(Firm firm, IReadOnlyList<AccountView> views, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var failed = views.Where(v => v.Account.State.Status == ChallengeStatus.Failed).Select(v => v.Account.State.Definition.Id).ToHashSet(StringComparer.Ordinal);
        if (failed.Count == 0)
        {
            return [];
        }

        var shop = (await orders.ShopAsync(firm, cancellationToken)).Where(i => failed.Contains(i.Challenge.Id)).ToList();
        if (shop.Count == 0)
        {
            return [];
        }

        var codes = (await discounts.ListAsync(firm.Id, now, cancellationToken)).Where(c => c.ForRetries).ToList();
        return shop.ToDictionary(
            i => i.Challenge.Id,
            i =>
            {
                var best = codes
                    .Select(c => (Code: c, DiscountRules.Apply(c, i.Price, now).Price))
                    .Where(c => c.Price is not null)
                    .OrderByDescending(c => c.Price!.Discount)
                    .FirstOrDefault();
                return new RetryOffer(i.Challenge.Id, i.Price.Amount, i.Price.Currency, best.Code?.Code, best.Price?.Amount);
            },
            StringComparer.Ordinal);
    }

    private static AccountDetailsResponse Build(
        AccountView view,
        LiveFigures? live,
        IEnumerable<StageRecord> stageRecords,
        IEnumerable<PayoutView> payoutViews,
        long lastSequence,
        DayStart? dayStart,
        Dictionary<string, (List<BreachClose> Closes, decimal? BalanceAfter)> breaches,
        RetryOffer? retry,
        DateTimeOffset now)
    {
        var account = view.Account;
        var state = account.State;
        var definition = state.Definition;
        var initial = definition.InitialBalance;
        var accountPayouts = payoutViews.ToList();

        var balance = live?.Balance ?? view.Trading?.Balance;
        var equity = live?.Equity;
        var valued = state.AccountId is null ? null : equity ?? balance;
        var stageResult = valued - initial;
        var target = state.Rules.ProfitTargetPercent is { } targetPercent ? definition.PercentOfInitialBalance(targetPercent) : (decimal?)null;
        var targetGained = target is null || state.AccountId is null ? (decimal?)null : (balance ?? initial) - initial;
        var trading = IsTrading(view);
        var results = new ResultsResponse(
            balance,
            equity,
            live is null ? null : live.Equity - live.Balance,
            stageResult,
            stageResult is { } result ? AccountPerformance.Percent(result, initial) : null,
            trading ? AccountPerformance.Today(equity, dayStart, initial) : null,
            trading && dayStart is { HasHistory: true } ? dayStart.Balance ?? initial : null,
            trading ? DayStartedAt(definition, now) : null,
            trading ? TradingDays.NextStart(now, definition.TradingDay) : null,
            targetGained,
            targetGained is null ? null : target,
            targetGained is { } gained ? AccountPerformance.Progress(gained, target!.Value) : null,
            accountPayouts.Where(p => p.Status == PayoutStatus.Paid).Sum(p => p.Amount));

        return new AccountDetailsResponse(
            AccountResponse.From(view),
            definition,
            live,
            Stages(state, [.. stageRecords]),
            results,
            view.Ending is ChallengeFailed failed ? Breach(failed, breaches) : null,
            view.Ending is ChallengeExpired expired ? new ExpiryEvidence(expired.Time, expired.Reason, expired.Day) : null,
            view.Ending?.Time,
            [.. accountPayouts.Select(PayoutResponse.From)],
            $"{lastSequence}.{account.Steps}",
            state.Status == ChallengeStatus.Failed ? retry : null);
    }

    private static BreachEvidence Breach(ChallengeFailed failed, Dictionary<string, (List<BreachClose> Closes, decimal? BalanceAfter)> breaches)
    {
        var closed = breaches.GetValueOrDefault(failed.AccountId);
        return new BreachEvidence(failed.Time, failed.FloorId, failed.Level, failed.Equity, failed.Reason, closed.Closes ?? [], closed.BalanceAfter);
    }

    private static List<StageResponse> Stages(ChallengeState state, List<StageRecord> records)
    {
        var definition = state.Definition;
        return
        [
            .. Enumerable.Range(0, definition.FundedStage + 1).Select(stage =>
            {
                var record = records.FirstOrDefault(r => r.Stage == stage);
                var progress = stage < state.Stage ? StageProgress.Passed : stage == state.Stage ? StageProgress.Current : StageProgress.Upcoming;
                var tradingDays = progress switch
                {
                    StageProgress.Passed => record?.PassedTradingDays,
                    StageProgress.Current when state.AccountId is not null => state.TradingDays.Count,
                    _ => null,
                };
                var rules = definition.Stage(stage);
                return new StageResponse(
                    stage,
                    rules.Name,
                    progress,
                    record?.TradingAccountId,
                    record?.StartedAt,
                    record?.PassedAt,
                    record?.PassedBalance - definition.InitialBalance,
                    tradingDays,
                    rules.ProfitTargetPercent is { } target ? definition.PercentOfInitialBalance(target) : null,
                    definition.PercentOfInitialBalance(rules.DailyLoss.Percent),
                    definition.PercentOfInitialBalance(rules.MaxLoss.Percent));
            }),
        ];
    }

    /// <summary>The trading account valued at the latest prices, while the stage is traded. Null when the platform cannot be reached.</summary>
    private async Task<LiveFigures?> LiveAsync(Firm firm, AccountView view, CancellationToken cancellationToken)
    {
        if (!IsTrading(view) || firm.Trading is not { } firmTrading)
        {
            return null;
        }

        try
        {
            var state = view.Account.State;
            return await trading.GetAccountAsync(firmTrading, state.AccountId!, cancellationToken) is { } snapshot
                ? new LiveFigures(snapshot.Balance, snapshot.Equity, [.. snapshot.Floors.Select(f => new FloorFigure(f.FloorId, f.Level, f.Headroom, Distance(state, f.FloorId)))])
                : null;
        }
        catch (TradingPlatformUnavailableException)
        {
            // The figures the trading platform last reported are shown instead.
            return null;
        }
    }

    /// <summary>The whole loss a floor of the rule engine allows, from the stage's rules.</summary>
    private static decimal? Distance(ChallengeState state, string floorId) => floorId switch
    {
        FloorIds.Daily => state.Definition.PercentOfInitialBalance(state.Rules.DailyLoss.Percent),
        FloorIds.MaxLoss => state.Definition.PercentOfInitialBalance(state.Rules.MaxLoss.Percent),
        _ => null,
    };

    private static bool IsTrading(AccountView view) => view.Account.State is { Status: ChallengeStatus.Active, AccountId: not null };

    private static DateTimeOffset DayStartedAt(ChallengeDefinition definition, DateTimeOffset now) =>
        TradingDays.StartOf(TradingDays.DayOf(now, definition.TradingDay), definition.TradingDay);
}
