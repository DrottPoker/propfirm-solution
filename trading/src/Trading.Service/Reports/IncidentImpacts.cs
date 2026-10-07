using Trading.Engine;
using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Service.Engine;
using Trading.Service.Persistence;

namespace Trading.Service.Reports;

/// <summary>
/// What a period, such as an outage of the price feed, did to a firm's accounts (ADR 0053). Read from the firm's
/// events since the period began and the accounts as they are now, so it works the same however long ago it was.
/// </summary>
internal sealed class IncidentImpacts(IEngineJournal journal, EngineHost engine, EngineConfiguration configuration, BreachReports breaches)
{
    /// <summary>Loss limits broken this long after the period are counted too: the first prices after it can break them.</summary>
    public static readonly TimeSpan BreachesAfter = TimeSpan.FromMinutes(30);

    /// <summary>The longest period, so the answer stays quick.</summary>
    public static readonly TimeSpan MaxLength = TimeSpan.FromDays(1);

    /// <summary>How long ago a period may have begun.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    private const int EventPage = 5_000;

    // Commands are refused for these while prices are missing.
    private static readonly RejectReason[] MissingPrice = [RejectReason.StalePrice, RejectReason.NoPrice];

    private readonly Dictionary<string, Instrument> _instruments = configuration.Instruments.ToDictionary(i => i.Symbol, StringComparer.Ordinal);

    public async Task<IncidentImpact> BuildAsync(IReadOnlyCollection<string> groupIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var (accounts, groups) = await engine.QueryAsync(
            e => (e.GetAccounts(groupIds), groupIds.Select(e.GetGroup).OfType<TradingGroup>().ToDictionary(g => g.Id, StringComparer.Ordinal)),
            cancellationToken);
        var events = await ReadEventsSinceAsync(groupIds, from, cancellationToken);
        var byAccount = events.ToLookup(e => EventLog.AccountIdOf(e.Envelope.Event) ?? string.Empty, StringComparer.Ordinal);
        var groupOf = await engine.QueryAsync(e => accounts.ToDictionary(a => a.AccountId, a => e.GetGroupId(a.AccountId)!, StringComparer.Ordinal), cancellationToken);

        var states = accounts.Select(a => StartOf(a, byAccount[a.AccountId].Select(e => e.Envelope.Event).ToList(), groups[groupOf[a.AccountId]])).ToList();

        // One price book at the start serves every account.
        var revaluation = new Revaluation(configuration);
        foreach (var currency in states.Select(s => s.Account.Currency).Distinct(StringComparer.Ordinal))
        {
            var traded = states.Where(s => s.Account.Currency == currency).SelectMany(s => s.Positions).Select(p => p.Instrument.Symbol);
            foreach (var symbol in breaches.SymbolsFor(traded, currency))
            {
                if (await journal.FindQuoteAsync(symbol, from, null, cancellationToken) is { } quote)
                {
                    revaluation.Update(quote.Quote);
                }
            }
        }

        var impacts = new List<AccountImpact>();
        foreach (var state in states)
        {
            var accountEvents = byAccount[state.Account.AccountId].Select(e => e.Envelope.Event).ToList();
            var refused = accountEvents
                .OfType<InputRejected>()
                .Where(r => r.Timestamp <= to && MissingPrice.Contains(r.Reason))
                .Select(r => r.Input)
                .ToList();
            var breach = accountEvents.OfType<EquityFloorBreached>().FirstOrDefault(b => b.Timestamp <= to + BreachesAfter);
            if (state.Positions.Count == 0 && refused.Count == 0 && breach is null)
            {
                continue;
            }

            impacts.Add(new AccountImpact(
                state.Account.AccountId,
                state.Account.Currency,
                state.Positions.Count,
                state.Balance,
                revaluation.Equity(state.Balance, state.Positions, state.Account.Currency),
                refused.Count(i => i is PlaceOrder),
                refused.Count(i => i is ClosePosition or CloseAllPositions),
                refused.Count(i => i is not PlaceOrder and not ClosePosition and not CloseAllPositions),
                breach is null ? null : new ImpactBreach(breach.Timestamp, breach.FloorId, breach.Level, breach.Equity),
                state.Account.Status,
                state.Account.Balance,
                state.Account.Equity));
        }

        return new IncidentImpact(from, to, impacts);
    }

    private async Task<List<RecordedEvent>> ReadEventsSinceAsync(IReadOnlyCollection<string> groupIds, DateTimeOffset from, CancellationToken cancellationToken)
    {
        var events = new List<RecordedEvent>();
        while (true)
        {
            var page = await journal.ReadGroupEventsSinceAsync(groupIds, from, events.Count == 0 ? 0 : events[^1].Envelope.Sequence, EventPage, cancellationToken);
            events.AddRange(page);
            if (page.Count < EventPage)
            {
                return events;
            }
        }
    }

    // The balance and open positions when the period began, worked back from the account now and its events since.
    private AccountStart StartOf(AccountSnapshot account, List<EngineEvent> since, TradingGroup group)
    {
        var balance = account.Balance;
        if (since.FirstOrDefault(e => BreachReports.BalanceAfter(e) is not null) is { } first)
        {
            balance = BreachReports.BalanceAfter(first)!.Value - ChangeOf(first);
        }

        var openedSince = since.OfType<PositionOpened>().Select(p => p.PositionId).ToHashSet(StringComparer.Ordinal);
        var partsSince = since.OfType<PositionPartiallyClosed>().ToLookup(p => p.PositionId, StringComparer.Ordinal);
        var positions = new List<RevaluedPosition>();

        // Still open now, or closed since: either way open then, unless opened since.
        foreach (var position in account.Positions.Where(p => !openedSince.Contains(p.PositionId)))
        {
            Add(position.Symbol, position.Side, position.Volume + partsSince[position.PositionId].Sum(p => p.Volume), position.OpenPrice);
        }

        foreach (var closed in since.OfType<PositionClosed>().Where(c => !openedSince.Contains(c.PositionId)))
        {
            Add(closed.Symbol, closed.Side, closed.Volume + partsSince[closed.PositionId].Sum(p => p.Volume), closed.OpenPrice);
        }

        return new AccountStart(account, balance, positions);

        void Add(string symbol, Side side, decimal volume, decimal openPrice)
        {
            if (_instruments.TryGetValue(symbol, out var instrument))
            {
                var conditions = group.Symbols.FirstOrDefault(s => s.Symbol == symbol) ?? new SymbolConditions(symbol, 1, 0, 0m);
                positions.Add(new RevaluedPosition(instrument, conditions, side, volume, openPrice));
            }
        }
    }

    // How much the event changed the balance.
    private static decimal ChangeOf(EngineEvent engineEvent) => engineEvent switch
    {
        PositionOpened opened => -opened.Commission,
        PositionPartiallyClosed part => part.Profit - part.Commission,
        PositionClosed closed => closed.Profit - closed.Commission,
        BalanceAdjusted adjusted => adjusted.Amount,
        _ => 0m,
    };

    private sealed record AccountStart(AccountSnapshot Account, decimal Balance, List<RevaluedPosition> Positions);
}
