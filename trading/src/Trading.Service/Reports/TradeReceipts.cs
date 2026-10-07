using Trading.Engine;
using Trading.Engine.Events;
using Trading.Service.Engine;
using Trading.Service.Persistence;

namespace Trading.Service.Reports;

/// <summary>
/// Receipts for trades, read from the journal (ADR 0053). The price behind a fill is the last raw price of the symbol
/// the engine had when the input that caused the fill was applied, so the markup is what the account paid on top.
/// </summary>
internal sealed class TradeReceipts(IEngineJournal journal, EngineHost engine, EngineConfiguration configuration)
{
    /// <summary>How far before and after a fill its prices are shown.</summary>
    public static readonly TimeSpan PricesAround = TimeSpan.FromMinutes(1);

    /// <summary>The most prices shown around a fill. More are thinned out evenly.</summary>
    public const int MaxPrices = 240;

    private readonly Dictionary<string, Instrument> _instruments = configuration.Instruments.ToDictionary(i => i.Symbol, StringComparer.Ordinal);

    /// <summary>The position's receipt, or null when the account has no such position.</summary>
    public async Task<TradeReceipt?> BuildAsync(string accountId, string positionId, CancellationToken cancellationToken)
    {
        var events = await journal.ReadPositionEventsAsync(accountId, positionId, cancellationToken);
        if (events.FirstOrDefault(e => e.Envelope.Event is PositionOpened) is not { Envelope.Event: PositionOpened opened } openedEvent
            || !_instruments.TryGetValue(opened.Symbol, out var instrument)
            || await engine.QueryAsync(e => e.GetAccount(accountId), cancellationToken) is not { } account)
        {
            return null;
        }

        var order = events.Select(e => e.Envelope.Event).OfType<OrderPlaced>().FirstOrDefault(o => o.Type != OrderType.Market);
        var openFill = await FillAsync(
            instrument,
            opened.Timestamp,
            openedEvent.InputSequence,
            opened.Volume,
            opened.OpenPrice,
            opened.Commission,
            atAsk: opened.Side == Side.Buy,
            cancellationToken);

        // The stops as last set before each close, to tell which level a stop loss or take profit closed at.
        decimal? stopLoss = opened.StopLoss;
        decimal? takeProfit = opened.TakeProfit;
        var trailing = opened.TrailingDistance is not null;
        var closes = new List<ReceiptClose>();
        var closed = false;
        foreach (var recorded in events.SkipWhile(e => e != openedEvent).Skip(1))
        {
            switch (recorded.Envelope.Event)
            {
                case PositionModified modified:
                    (stopLoss, takeProfit, trailing) = (modified.StopLoss, modified.TakeProfit, modified.TrailingDistance is not null);
                    break;
                case PositionPartiallyClosed part:
                    closes.Add(await CloseAsync(recorded, part.Timestamp, part.Volume, part.ClosePrice, part.Commission, part.Profit, part.Reason));
                    break;
                case PositionClosed close:
                    closes.Add(await CloseAsync(recorded, close.Timestamp, close.Volume, close.ClosePrice, close.Commission, close.Profit, close.Reason));
                    closed = true;
                    break;
            }
        }

        var commission = opened.Commission + closes.Sum(c => c.Fill.Commission);
        decimal? profit = closes.Count > 0 ? closes.Sum(c => c.Profit) : null;
        return new TradeReceipt(
            accountId,
            positionId,
            opened.Symbol,
            opened.Side,
            instrument.Digits,
            account.Currency,
            order is null ? null : new ReceiptOrder(order.Timestamp, order.Type, order.Price),
            openFill,
            closes,
            commission,
            profit,
            closed ? profit - commission : null);

        // A trailing stop loss moves without events, so the level it closed at is not known.
        async Task<ReceiptClose> CloseAsync(RecordedEvent recorded, DateTimeOffset at, decimal volume, decimal price, decimal fee, decimal gain, CloseReason reason) =>
            new(
                await FillAsync(instrument, at, recorded.InputSequence, volume, price, fee, atAsk: opened.Side == Side.Sell, cancellationToken),
                gain,
                reason,
                reason switch
                {
                    CloseReason.StopLoss when !trailing => stopLoss,
                    CloseReason.TakeProfit => takeProfit,
                    _ => null,
                });
    }

    private async Task<ReceiptFill> FillAsync(
        Instrument instrument,
        DateTimeOffset at,
        long? input,
        decimal volume,
        decimal price,
        decimal commission,
        bool atAsk,
        CancellationToken cancellationToken)
    {
        var (feed, markup) = await FeedPriceAsync(journal, instrument, at, input, price, atAsk, cancellationToken);
        var prices = new List<PricePoint>();
        await foreach (var quote in journal.ReadQuotesBetweenAsync([instrument.Symbol], at - PricesAround, at + PricesAround, cancellationToken))
        {
            prices.Add(new PricePoint(quote.Quote.Timestamp, quote.Quote.Bid, quote.Quote.Ask));
        }

        return new ReceiptFill(at, volume, price, commission, feed, markup, Thin(prices, MaxPrices));
    }

    /// <summary>
    /// The feed's price behind a price the account got on one side, and the markup between them in points. Null for
    /// both when the journal has no price there, or the one it has does not lead to the account's price, as for a trade
    /// from before events were kept with their input.
    /// </summary>
    public static async Task<(FeedPrice? Feed, int? MarkupPoints)> FeedPriceAsync(
        IEngineJournal journal,
        Instrument instrument,
        DateTimeOffset at,
        long? input,
        decimal price,
        bool atAsk,
        CancellationToken cancellationToken)
    {
        if (await journal.FindQuoteAsync(instrument.Symbol, at, input, cancellationToken) is not { } recorded)
        {
            return (null, null);
        }

        var quote = recorded.Quote;
        var points = (atAsk ? price - quote.Ask : quote.Bid - price) / instrument.Point;
        if (points < 0m || points != decimal.Truncate(points))
        {
            return (null, null);
        }

        return (new FeedPrice(recorded.Sequence, recorded.Feed, quote.Bid, quote.Ask, quote.Timestamp), (int)points);
    }

    /// <summary>At most <paramref name="max"/> of the items, evenly spread, always with the first and the last.</summary>
    public static IReadOnlyList<T> Thin<T>(IReadOnlyList<T> items, int max)
    {
        if (items.Count <= max)
        {
            return items;
        }

        return [.. Enumerable.Range(0, max).Select(i => items[(int)Math.Round(i * (items.Count - 1) / (double)(max - 1))])];
    }
}
