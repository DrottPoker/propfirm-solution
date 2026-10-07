using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Engine.Internal;

namespace Trading.Engine;

// Prices, triggers, equity floors and stop out.
public sealed partial class TradingEngine
{
    private RejectReason? ApplyQuote(Quote quote, List<EngineEvent> events)
    {
        if (!TryLookup(_instruments, quote.Symbol, out var instrument))
        {
            return RejectReason.UnknownSymbol;
        }

        if (quote.Bid <= 0m || quote.Ask < quote.Bid || !instrument.IsOnGrid(quote.Bid) || !instrument.IsOnGrid(quote.Ask))
        {
            return RejectReason.InvalidQuote;
        }

        _prices.Update(quote);

        // A price can change equity through conversion too, so every account with exposure is evaluated. A new trading day
        // starts on the first price after it began, also on an account without exposure, so its lock ends.
        foreach (var account in _accounts)
        {
            if (account.Status == AccountStatus.Disabled)
            {
                continue;
            }

            StartTradingDayIfDue(account, quote.Timestamp, events);
            if (!account.HasExposure)
            {
                continue;
            }

            ProcessTriggers(account, instrument, quote.Timestamp, events);
            EvaluateRisk(account, quote.Timestamp, events);
        }

        return null;
    }

    // Trailing stops move first, then stop loss and take profit, then pending orders. A position opened here is checked
    // on the next price.
    private void ProcessTriggers(AccountState account, Instrument instrument, DateTimeOffset now, List<EngineEvent> events)
    {
        foreach (var position in account.Positions.Where(p => IsSymbol(p.Instrument, instrument)).ToList())
        {
            var closePrice = _valuation.CurrentPrice(position.Instrument, position.Conditions).ClosePrice(position.Side);
            Trail(position, closePrice);
            if (TriggeredCloseReason(position, closePrice) is { } reason)
            {
                ClosePositionAt(account, position, closePrice, reason, now, events);
            }
        }

        foreach (var order in account.Orders.Where(o => IsSymbol(o.Instrument, instrument)).ToList())
        {
            var price = _valuation.CurrentPrice(order.Instrument, order.Conditions);
            if (!IsTriggered(order, price))
            {
                continue;
            }

            account.Orders.Remove(order);
            if (account.OwnLimits.MaxTrades is { } maxTrades && account.TradesToday >= maxTrades)
            {
                events.Add(new OrderCancelled(now, account.Id, order.Id, CancelReason.TradeLimit));
                continue;
            }

            var request = new OpenRequest(order.Id, order.Instrument, order.Conditions, order.Side, order.Volume, order.StopLoss, order.TakeProfit, order.TrailingDistance);
            if (!TryOpenPosition(account, request, price.OpenPrice(order.Side), now, events))
            {
                events.Add(new OrderCancelled(now, account.Id, order.Id, CancelReason.InsufficientMargin));
            }
        }
    }

    private void EvaluateRisk(AccountState account, DateTimeOffset now, List<EngineEvent> events)
    {
        if (account.Status == AccountStatus.Disabled || CheckFloors(account, now, events))
        {
            return;
        }

        // Commission on stop out closes can push equity below a floor.
        if (StopOutIfNeeded(account, now, events) && CheckFloors(account, now, events))
        {
            return;
        }

        // The firm's limits come first: a price that breaks both breaks the firm's.
        CheckOwnLimits(account, now, events);
    }

    // Returns true if a floor was breached and the account was disabled.
    private bool CheckFloors(AccountState account, DateTimeOffset now, List<EngineEvent> events)
    {
        if (account.Floors.Count == 0)
        {
            return false;
        }

        var equity = _valuation.Measure(account).Equity;
        foreach (var floor in account.Floors.Values)
        {
            floor.Observe(equity);
        }

        foreach (var floor in account.Floors.Values)
        {
            if (equity >= floor.Level)
            {
                continue;
            }

            events.Add(new EquityFloorBreached(now, account.Id, floor.Id, floor.Level, equity, PricesOf(account), PositionsOf(account)));
            Liquidate(account, CloseReason.EquityFloor, CancelReason.EquityFloor, now, events);
            Disable(account, DisableReason.EquityFloor, now, events);
            return true;
        }

        return false;
    }

    // Closes the position with the largest loss until the margin level is back at or above the stop out level.
    private bool StopOutIfNeeded(AccountState account, DateTimeOffset now, List<EngineEvent> events)
    {
        var stopOutLevel = account.Group.StopOutLevelPercent;
        if (stopOutLevel <= 0m || account.Positions.Count == 0)
        {
            return false;
        }

        var figures = _valuation.Measure(account);
        if (figures.MarginLevelPercent is not { } marginLevel || marginLevel >= stopOutLevel)
        {
            return false;
        }

        events.Add(new StopOutTriggered(now, account.Id, figures.Equity, figures.UsedMargin, marginLevel));
        while (account.Positions.Count > 0 && figures.MarginLevelPercent < stopOutLevel)
        {
            var (worst, closePrice) = LargestLoss(account);
            ClosePositionAt(account, worst, closePrice, CloseReason.StopOut, now, events);
            figures = _valuation.Measure(account);
        }

        return true;
    }

    // Ties go to the oldest position.
    private (PositionState Position, decimal ClosePrice) LargestLoss(AccountState account)
    {
        var currency = account.Group.Currency;
        (PositionState Position, decimal ClosePrice, decimal Profit)? worst = null;
        foreach (var position in account.Positions)
        {
            var closePrice = _valuation.CurrentPrice(position.Instrument, position.Conditions).ClosePrice(position.Side);
            var profit = _valuation.Profit(position, closePrice, currency);
            if (worst is null || profit < worst.Value.Profit)
            {
                worst = (position, closePrice, profit);
            }
        }

        return worst is { } result
            ? (result.Position, result.ClosePrice)
            : throw new InvalidOperationException("The account has no positions.");
    }

    private bool TryOpenPosition(AccountState account, OpenRequest request, decimal openPrice, DateTimeOffset now, List<EngineEvent> events)
    {
        var currency = account.Group.Currency;
        var margin = _valuation.Margin(request.Instrument, request.Conditions, request.Volume, currency);
        var commission = _valuation.Commission(request.Conditions, request.Volume, currency);
        if (margin + commission > _valuation.Measure(account).FreeMargin)
        {
            return false;
        }

        account.Balance -= commission;
        account.TradesToday++;
        account.Positions.Add(new PositionState(
            request.Id,
            request.Instrument,
            request.Conditions,
            request.Side,
            request.Volume,
            openPrice,
            request.StopLoss,
            request.TakeProfit,
            now,
            request.TrailingDistance));
        events.Add(new PositionOpened(
            now,
            account.Id,
            request.Id,
            request.Instrument.Symbol,
            request.Side,
            request.Volume,
            openPrice,
            request.StopLoss,
            request.TakeProfit,
            commission,
            account.Balance,
            request.TrailingDistance));
        return true;
    }

    private void ClosePositionAt(AccountState account, PositionState position, decimal closePrice, CloseReason reason, DateTimeOffset now, List<EngineEvent> events)
    {
        var currency = account.Group.Currency;
        var profit = _valuation.Profit(position, closePrice, currency);
        var commission = _valuation.Commission(position.Conditions, position.Volume, currency);
        account.Balance += profit - commission;
        account.Positions.Remove(position);
        events.Add(new PositionClosed(
            now,
            account.Id,
            position.Id,
            position.Instrument.Symbol,
            position.Side,
            position.Volume,
            position.OpenPrice,
            closePrice,
            profit,
            commission,
            reason,
            account.Balance));
    }

    // Closes part of a position. The rest stays open with the same id, its stops and its trailing stop.
    private void ClosePartAt(AccountState account, PositionState position, decimal volume, decimal closePrice, CloseReason reason, DateTimeOffset now, List<EngineEvent> events)
    {
        var currency = account.Group.Currency;
        var profit = _valuation.Profit(position, closePrice, currency, volume);
        var commission = _valuation.Commission(position.Conditions, volume, currency);
        account.Balance += profit - commission;
        position.Volume -= volume;
        events.Add(new PositionPartiallyClosed(
            now,
            account.Id,
            position.Id,
            position.Instrument.Symbol,
            position.Side,
            volume,
            position.Volume,
            position.OpenPrice,
            closePrice,
            profit,
            commission,
            reason,
            account.Balance));
    }

    // Closes positions at the latest prices, even if they are old, and cancels all orders.
    private void Liquidate(AccountState account, CloseReason closeReason, CancelReason cancelReason, DateTimeOffset now, List<EngineEvent> events)
    {
        foreach (var position in account.Positions.ToList())
        {
            var closePrice = _valuation.CurrentPrice(position.Instrument, position.Conditions).ClosePrice(position.Side);
            ClosePositionAt(account, position, closePrice, closeReason, now, events);
        }

        foreach (var order in account.Orders)
        {
            events.Add(new OrderCancelled(now, account.Id, order.Id, cancelReason));
        }

        account.Orders.Clear();
    }

    private static void Disable(AccountState account, DisableReason reason, DateTimeOffset now, List<EngineEvent> events)
    {
        account.Status = AccountStatus.Disabled;
        events.Add(new AccountDisabled(now, account.Id, reason));
    }

    // A trailing stop loss moves with the price the position closes at, never back. Like a trailing floor, the move
    // gives no event.
    private static void Trail(PositionState position, decimal closePrice)
    {
        if (position.TrailingDistance is not { } distance)
        {
            return;
        }

        var isBuy = position.Side == Side.Buy;
        var level = isBuy ? closePrice - distance : closePrice + distance;
        if (position.StopLoss is not { } stopLoss || (isBuy ? level > stopLoss : level < stopLoss))
        {
            position.StopLoss = level;
        }
    }

    private static bool IsSymbol(Instrument candidate, Instrument instrument) =>
        string.Equals(candidate.Symbol, instrument.Symbol, StringComparison.Ordinal);

    private static CloseReason? TriggeredCloseReason(PositionState position, decimal closePrice)
    {
        var isBuy = position.Side == Side.Buy;
        if (position.StopLoss is { } stopLoss && (isBuy ? closePrice <= stopLoss : closePrice >= stopLoss))
        {
            return CloseReason.StopLoss;
        }

        if (position.TakeProfit is { } takeProfit && (isBuy ? closePrice >= takeProfit : closePrice <= takeProfit))
        {
            return CloseReason.TakeProfit;
        }

        return null;
    }

    // Limit orders fill at the order price or better, stop orders at the current price even if it gapped past.
    private static bool IsTriggered(OrderState order, ClientPrice price) =>
        (order.Type, order.Side) switch
        {
            (OrderType.Limit, Side.Buy) => price.Ask <= order.Price,
            (OrderType.Limit, Side.Sell) => price.Bid >= order.Price,
            (OrderType.Stop, Side.Buy) => price.Ask >= order.Price,
            (OrderType.Stop, Side.Sell) => price.Bid <= order.Price,
            _ => false,
        };

    private readonly record struct OpenRequest(
        string Id,
        Instrument Instrument,
        SymbolConditions Conditions,
        Side Side,
        decimal Volume,
        decimal? StopLoss,
        decimal? TakeProfit,
        decimal? TrailingDistance);
}
