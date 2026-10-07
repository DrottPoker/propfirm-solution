using Microsoft.Extensions.Caching.Memory;

using Trading.Engine;
using Trading.Engine.Events;
using Trading.Service.Engine;
using Trading.Service.Persistence;

namespace Trading.Service.Reports;

/// <summary>
/// Reports of broken loss limits, read from the journal (ADR 0053). Equity before the breach is counted again price
/// by price with the engine's own valuation, from the account's events and the recorded prices. The markup of the
/// symbols the account held at the breach is taken from the breach itself, so the last point is the engine's equity.
/// </summary>
internal sealed class BreachReports(IEngineJournal journal, EngineHost engine, EngineConfiguration configuration, IMemoryCache cache)
{
    /// <summary>How far back equity is counted: from the oldest position open at the breach, but at most this far.</summary>
    public static readonly TimeSpan MaxHistory = TimeSpan.FromHours(6);

    /// <summary>Equity is counted at least this far back, so the curve shows how it came to the breach.</summary>
    public static readonly TimeSpan MinHistory = TimeSpan.FromMinutes(15);

    /// <summary>A pause between prices at least this long is shown as a period without prices.</summary>
    public static readonly TimeSpan GapAfter = TimeSpan.FromMinutes(1);

    /// <summary>The most points on the curve. More are reduced to the lowest and highest of each period.</summary>
    public const int MaxCurvePoints = 600;

    private const int EventPage = 1_000;

    // An account is disabled when a limit breaks, so its report never changes.
    private static readonly TimeSpan CachedFor = TimeSpan.FromMinutes(30);

    private readonly Dictionary<string, Instrument> _instruments = configuration.Instruments.ToDictionary(i => i.Symbol, StringComparer.Ordinal);

    /// <summary>The report of the account's broken loss limit, or null when none was broken.</summary>
    public async Task<BreachReport?> BuildAsync(string accountId, CancellationToken cancellationToken)
    {
        var key = (nameof(BreachReports), accountId);
        if (cache.TryGetValue(key, out BreachReport? cached))
        {
            return cached;
        }

        var events = await ReadEventsAsync(accountId, cancellationToken);
        var breachIndex = events.FindIndex(e => e.Event is EquityFloorBreached);
        if (breachIndex < 0 || await engine.QueryAsync(e => (e.GetAccount(accountId), e.GetGroupId(accountId) is { } id ? e.GetGroup(id) : null), cancellationToken) is not ({ } account, { } group))
        {
            return null;
        }

        var breach = (EquityFloorBreached)events[breachIndex].Event;

        // The closes and the end of trading came from the same input as the breach.
        var end = breachIndex;
        while (end + 1 < events.Count && events[end + 1].Event.Timestamp == breach.Timestamp)
        {
            end++;
        }

        var prices = await PricesAsync(breach, cancellationToken);
        var conditions = ConditionsOf(group, prices);
        var start = StartOf(breach);

        // Never before the account was created, when it had no balance to count from.
        if (events[0].Event is AccountCreated created && created.Timestamp > start)
        {
            start = created.Timestamp;
        }
        var (curve, gaps) = await CurveAsync(events, breachIndex, start, breach, account.Currency, conditions, cancellationToken);
        var balance = breach.Equity - breach.Positions.Sum(p => p.Profit);
        var balanceAfter = events.Take(end + 1).Skip(breachIndex + 1).Select(e => BalanceAfter(e.Event)).LastOrDefault(b => b is not null) ?? balance;

        var report = new BreachReport(
            accountId,
            account.Currency,
            breach.Timestamp,
            breach.FloorId,
            breach.Level,
            breach.Equity,
            balance,
            balanceAfter,
            prices,
            breach.Positions,
            curve,
            gaps,
            [.. events.Take(end + 1).Where(e => e.Event.Timestamp >= start)]);
        cache.Set(key, report, CachedFor);
        return report;
    }

    private static DateTimeOffset StartOf(EquityFloorBreached breach)
    {
        var oldest = breach.Positions.Count > 0 ? breach.Positions.Min(p => p.OpenTime) : breach.Timestamp;
        var earliest = breach.Timestamp - MaxHistory;
        var latest = breach.Timestamp - MinHistory;
        return oldest < earliest ? earliest : oldest > latest ? latest : oldest;
    }

    private async Task<List<EventEnvelope>> ReadEventsAsync(string accountId, CancellationToken cancellationToken)
    {
        var events = new List<EventEnvelope>();
        while (true)
        {
            var page = await journal.ReadEventsAsync(accountId, events.Count == 0 ? 0 : events[^1].Sequence, EventPage, cancellationToken);
            events.AddRange(page);
            if (page.Count < EventPage)
            {
                return events;
            }
        }
    }

    // The prices that broke the limit, with the feed's prices behind them as of the breach.
    private async Task<IReadOnlyList<BreachPrice>> PricesAsync(EquityFloorBreached breach, CancellationToken cancellationToken)
    {
        var prices = new List<BreachPrice>();
        foreach (var price in breach.Prices)
        {
            if (!_instruments.TryGetValue(price.Symbol, out var instrument)
                || await journal.FindQuoteAsync(price.Symbol, breach.Timestamp, null, cancellationToken) is not { } recorded)
            {
                prices.Add(new BreachPrice(price.Symbol, price.Bid, price.Ask, price.Timestamp, null, null, null));
                continue;
            }

            var quote = recorded.Quote;
            var bidPoints = (quote.Bid - price.Bid) / instrument.Point;
            var askPoints = (price.Ask - quote.Ask) / instrument.Point;
            var known = bidPoints >= 0m && askPoints >= 0m && bidPoints == decimal.Truncate(bidPoints) && askPoints == decimal.Truncate(askPoints);
            prices.Add(known
                ? new BreachPrice(price.Symbol, price.Bid, price.Ask, price.Timestamp, new FeedPrice(recorded.Sequence, recorded.Feed, quote.Bid, quote.Ask, quote.Timestamp), (int)bidPoints, (int)askPoints)
                : new BreachPrice(price.Symbol, price.Bid, price.Ask, price.Timestamp, null, null, null));
        }

        return prices;
    }

    // The group's conditions per symbol, with the markup at the breach where it is known.
    private static Dictionary<string, SymbolConditions> ConditionsOf(TradingGroup group, IReadOnlyList<BreachPrice> prices)
    {
        var conditions = group.Symbols.ToDictionary(s => s.Symbol, StringComparer.Ordinal);
        foreach (var price in prices)
        {
            if (price.BidMarkupPoints is { } bid && price.AskMarkupPoints is { } ask && conditions.TryGetValue(price.Symbol, out var current))
            {
                var markup = current with { SpreadMarkupPoints = bid + ask };
                if (markup.BidMarkupPoints == bid)
                {
                    conditions[price.Symbol] = markup;
                }
            }
        }

        return conditions;
    }

    private async Task<(IReadOnlyList<EquityPoint> Curve, IReadOnlyList<PriceGap> Gaps)> CurveAsync(
        List<EventEnvelope> events,
        int breachIndex,
        DateTimeOffset start,
        EquityFloorBreached breach,
        string currency,
        Dictionary<string, SymbolConditions> conditions,
        CancellationToken cancellationToken)
    {
        var positions = new Dictionary<string, RevaluedPosition>(StringComparer.Ordinal);
        var balance = 0m;
        var next = 0;
        while (next < breachIndex && events[next].Event.Timestamp <= start)
        {
            Apply(events[next++].Event);
        }

        var symbols = SymbolsFor(events.Take(breachIndex).Select(e => e.Event).OfType<PositionOpened>().Select(p => p.Symbol), currency);
        var revaluation = new Revaluation(configuration);
        foreach (var symbol in symbols)
        {
            if (await journal.FindQuoteAsync(symbol, start, null, cancellationToken) is { } seed)
            {
                revaluation.Update(seed.Quote);
            }
        }

        var points = new List<EquityPoint>();
        var gaps = new List<PriceGap>();
        var last = start;
        if (revaluation.Equity(balance, positions.Values, currency) is { } first)
        {
            points.Add(new EquityPoint(start, first));
        }

        await foreach (var recorded in journal.ReadQuotesBetweenAsync(symbols, start, breach.Timestamp, cancellationToken))
        {
            var quote = recorded.Quote;
            if (quote.Timestamp <= start)
            {
                continue;
            }

            // Events from commands before the price, then the price, then what the price caused.
            while (next < breachIndex && events[next].Event.Timestamp < quote.Timestamp)
            {
                Apply(events[next++].Event);
            }

            if (quote.Timestamp - last >= GapAfter)
            {
                gaps.Add(new PriceGap(last, quote.Timestamp));
            }

            last = quote.Timestamp;
            revaluation.Update(quote);
            if (revaluation.Equity(balance, positions.Values, currency) is { } equity)
            {
                points.Add(new EquityPoint(quote.Timestamp, equity));
            }

            while (next < breachIndex && events[next].Event.Timestamp == quote.Timestamp)
            {
                Apply(events[next++].Event);
            }
        }

        if (breach.Timestamp - last >= GapAfter)
        {
            gaps.Add(new PriceGap(last, breach.Timestamp));
        }

        // The breach as the engine measured it.
        points.RemoveAll(p => p.Time >= breach.Timestamp);
        points.Add(new EquityPoint(breach.Timestamp, breach.Equity));
        return (Reduce(points, MaxCurvePoints), gaps);

        void Apply(EngineEvent engineEvent)
        {
            balance = BalanceAfter(engineEvent) ?? balance;
            switch (engineEvent)
            {
                case PositionOpened opened when _instruments.TryGetValue(opened.Symbol, out var instrument):
                    positions[opened.PositionId] = new RevaluedPosition(
                        instrument,
                        conditions.GetValueOrDefault(opened.Symbol) ?? new SymbolConditions(opened.Symbol, 1, 0, 0m),
                        opened.Side,
                        opened.Volume,
                        opened.OpenPrice);
                    break;
                case PositionPartiallyClosed part when positions.TryGetValue(part.PositionId, out var position):
                    positions[part.PositionId] = position with { Volume = part.RemainingVolume };
                    break;
                case PositionClosed closed:
                    positions.Remove(closed.PositionId);
                    break;
            }
        }
    }

    /// <summary>
    /// The symbols whose prices value positions in <paramref name="symbols"/> in <paramref name="currency"/>: the symbols
    /// themselves, and every instrument that has the account currency, USD or one of their quote currencies, since a rate
    /// may go through any of them.
    /// </summary>
    public string[] SymbolsFor(IEnumerable<string> symbols, string currency)
    {
        var traded = symbols.Where(_instruments.ContainsKey).ToHashSet(StringComparer.Ordinal);
        var currencies = traded.Select(s => _instruments[s].QuoteCurrency).Append(currency).Append("USD").ToHashSet(StringComparer.Ordinal);
        return [.. _instruments.Values
            .Where(i => traded.Contains(i.Symbol) || currencies.Contains(i.BaseCurrency) || currencies.Contains(i.QuoteCurrency))
            .Select(i => i.Symbol)
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>The balance after the event, for events that change or state it.</summary>
    public static decimal? BalanceAfter(EngineEvent engineEvent) => engineEvent switch
    {
        AccountCreated created => created.Balance,
        PositionOpened opened => opened.BalanceAfter,
        PositionPartiallyClosed part => part.BalanceAfter,
        PositionClosed closed => closed.BalanceAfter,
        BalanceAdjusted adjusted => adjusted.BalanceAfter,
        _ => null,
    };

    /// <summary>
    /// At most <paramref name="max"/> points: the lowest and the highest of each period of equal length, in time order,
    /// with the first and the last, so a dip never disappears.
    /// </summary>
    public static IReadOnlyList<EquityPoint> Reduce(IReadOnlyList<EquityPoint> points, int max)
    {
        if (points.Count <= max)
        {
            return points;
        }

        var buckets = Math.Max(1, (max - 2) / 2);
        var from = points[0].Time;
        var span = (points[^1].Time - from).Ticks + 1;
        var reduced = new List<EquityPoint> { points[0] };
        foreach (var bucket in points.Skip(1).SkipLast(1).GroupBy(p => (int)((p.Time - from).Ticks * buckets / span)))
        {
            var low = bucket.MinBy(p => p.Equity)!;
            var high = bucket.MaxBy(p => p.Equity)!;
            reduced.AddRange(low == high ? [low] : low.Time <= high.Time ? [low, high] : [high, low]);
        }

        reduced.Add(points[^1]);
        return reduced;
    }
}
