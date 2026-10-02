using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Engine.Internal;

namespace Trading.Engine;

// Commands. Each handler validates everything before it changes state, and returns a reason when it rejects.
public sealed partial class TradingEngine
{
    private RejectReason? ApplyCreateAccount(CreateAccount command, List<EngineEvent> events)
    {
        if (string.IsNullOrEmpty(command.AccountId))
        {
            return RejectReason.InvalidId;
        }

        if (_accountsById.ContainsKey(command.AccountId))
        {
            return RejectReason.DuplicateId;
        }

        if (!TryLookup(_groups, command.GroupId, out var group))
        {
            return RejectReason.UnknownGroup;
        }

        if (command.InitialBalance < 0m || !_valuation.IsRounded(command.InitialBalance, group.Currency))
        {
            return RejectReason.InvalidAmount;
        }

        var account = new AccountState(command.AccountId, group, command.InitialBalance);
        _accountsById.Add(account.Id, account);
        _accounts.Add(account);
        events.Add(new AccountCreated(command.Timestamp, account.Id, group.Id, group.Currency, account.Balance));
        return null;
    }

    private RejectReason? ApplyPlaceOrder(PlaceOrder command, List<EngineEvent> events)
    {
        if (!TryGetActiveAccount(command.AccountId, out var account, out var accountRejection))
        {
            return accountRejection;
        }

        if (string.IsNullOrEmpty(command.OrderId))
        {
            return RejectReason.InvalidId;
        }

        if (account.UsedOrderIds.Contains(command.OrderId))
        {
            return RejectReason.DuplicateId;
        }

        if (!TryLookup(_instruments, command.Symbol, out var instrument))
        {
            return RejectReason.UnknownSymbol;
        }

        if (!account.Group.TryGetConditions(instrument.Symbol, out var conditions))
        {
            return RejectReason.SymbolNotTradable;
        }

        if (!Enum.IsDefined(command.Side) || !Enum.IsDefined(command.Type))
        {
            return RejectReason.InvalidOrder;
        }

        if (!IsValidVolume(instrument, command.Volume))
        {
            return RejectReason.InvalidVolume;
        }

        if (!IsValidOptionalPrice(instrument, command.StopLoss))
        {
            return RejectReason.InvalidStopLoss;
        }

        if (!IsValidOptionalPrice(instrument, command.TakeProfit))
        {
            return RejectReason.InvalidTakeProfit;
        }

        if (GetFreshPrice(instrument, conditions, command.Timestamp, out var price) is { } priceRejection)
        {
            return priceRejection;
        }

        if (!_valuation.CanValue(instrument, account.Group.Currency))
        {
            return RejectReason.NoConversionRate;
        }

        if (command.Type == OrderType.Market)
        {
            if (command.Price is not null)
            {
                return RejectReason.InvalidPrice;
            }

            if (ValidateStops(command.Side, price.ClosePrice(command.Side), command.StopLoss, command.TakeProfit) is { } stopsRejection)
            {
                return stopsRejection;
            }

            var request = new OpenRequest(command.OrderId, instrument, conditions, command.Side, command.Volume, command.StopLoss, command.TakeProfit);
            if (!TryOpenPosition(account, request, price.OpenPrice(command.Side), command.Timestamp, events))
            {
                return RejectReason.InsufficientMargin;
            }

            account.UsedOrderIds.Add(command.OrderId);
            EvaluateRisk(account, command.Timestamp, events);
            return null;
        }

        if (command.Price is not { } orderPrice || orderPrice <= 0m || !instrument.IsOnGrid(orderPrice)
            || !IsValidPendingPrice(command.Type, command.Side, orderPrice, price))
        {
            return RejectReason.InvalidPrice;
        }

        if (ValidateStops(command.Side, orderPrice, command.StopLoss, command.TakeProfit) is { } pendingStopsRejection)
        {
            return pendingStopsRejection;
        }

        account.Orders.Add(new OrderState(
            command.OrderId,
            instrument,
            conditions,
            command.Side,
            command.Type,
            command.Volume,
            orderPrice,
            command.StopLoss,
            command.TakeProfit,
            command.Timestamp));
        account.UsedOrderIds.Add(command.OrderId);
        events.Add(new OrderPlaced(
            command.Timestamp,
            account.Id,
            command.OrderId,
            instrument.Symbol,
            command.Side,
            command.Type,
            command.Volume,
            orderPrice,
            command.StopLoss,
            command.TakeProfit));
        return null;
    }

    private RejectReason? ApplyCancelOrder(CancelOrder command, List<EngineEvent> events)
    {
        if (!TryGetActiveAccount(command.AccountId, out var account, out var accountRejection))
        {
            return accountRejection;
        }

        var order = account.Orders.Find(o => string.Equals(o.Id, command.OrderId, StringComparison.Ordinal));
        if (order is null)
        {
            return RejectReason.UnknownOrder;
        }

        account.Orders.Remove(order);
        events.Add(new OrderCancelled(command.Timestamp, account.Id, order.Id, CancelReason.Manual));
        return null;
    }

    private RejectReason? ApplyClosePosition(ClosePosition command, List<EngineEvent> events)
    {
        if (!TryGetActiveAccount(command.AccountId, out var account, out var accountRejection))
        {
            return accountRejection;
        }

        var position = FindPosition(account, command.PositionId);
        if (position is null)
        {
            return RejectReason.UnknownPosition;
        }

        if (GetFreshPrice(position.Instrument, position.Conditions, command.Timestamp, out var price) is { } priceRejection)
        {
            return priceRejection;
        }

        ClosePositionAt(account, position, price.ClosePrice(position.Side), CloseReason.Manual, command.Timestamp, events);
        EvaluateRisk(account, command.Timestamp, events);
        return null;
    }

    private RejectReason? ApplyModifyPosition(ModifyPosition command, List<EngineEvent> events)
    {
        if (!TryGetActiveAccount(command.AccountId, out var account, out var accountRejection))
        {
            return accountRejection;
        }

        var position = FindPosition(account, command.PositionId);
        if (position is null)
        {
            return RejectReason.UnknownPosition;
        }

        if (!IsValidOptionalPrice(position.Instrument, command.StopLoss))
        {
            return RejectReason.InvalidStopLoss;
        }

        if (!IsValidOptionalPrice(position.Instrument, command.TakeProfit))
        {
            return RejectReason.InvalidTakeProfit;
        }

        if (GetFreshPrice(position.Instrument, position.Conditions, command.Timestamp, out var price) is { } priceRejection)
        {
            return priceRejection;
        }

        if (ValidateStops(position.Side, price.ClosePrice(position.Side), command.StopLoss, command.TakeProfit) is { } stopsRejection)
        {
            return stopsRejection;
        }

        position.StopLoss = command.StopLoss;
        position.TakeProfit = command.TakeProfit;
        events.Add(new PositionModified(command.Timestamp, account.Id, position.Id, position.StopLoss, position.TakeProfit));
        return null;
    }

    private RejectReason? ApplySetEquityFloor(SetEquityFloor command, List<EngineEvent> events)
    {
        if (!TryGetActiveAccount(command.AccountId, out var account, out var accountRejection))
        {
            return accountRejection;
        }

        if (string.IsNullOrEmpty(command.FloorId) || !IsValidFloorRule(command.Rule))
        {
            return RejectReason.InvalidFloor;
        }

        // A new or replaced floor starts trailing from the current equity.
        var floor = new FloorState(command.FloorId, command.Rule, _valuation.Measure(account).Equity);
        account.Floors[floor.Id] = floor;
        events.Add(new EquityFloorSet(command.Timestamp, account.Id, floor.Id, floor.Rule, floor.Level));
        EvaluateRisk(account, command.Timestamp, events);
        return null;
    }

    private RejectReason? ApplyRemoveEquityFloor(RemoveEquityFloor command, List<EngineEvent> events)
    {
        if (!TryGetActiveAccount(command.AccountId, out var account, out var accountRejection))
        {
            return accountRejection;
        }

        if (command.FloorId is null || !account.Floors.Remove(command.FloorId))
        {
            return RejectReason.UnknownFloor;
        }

        events.Add(new EquityFloorRemoved(command.Timestamp, account.Id, command.FloorId));
        return null;
    }

    private RejectReason? ApplyCloseAccount(CloseAccount command, List<EngineEvent> events)
    {
        if (!TryGetActiveAccount(command.AccountId, out var account, out var accountRejection))
        {
            return accountRejection;
        }

        Liquidate(account, CloseReason.AccountClosed, CancelReason.AccountClosed, command.Timestamp, events);
        Disable(account, DisableReason.Closed, command.Timestamp, events);
        return null;
    }

    private RejectReason? GetFreshPrice(Instrument instrument, SymbolConditions conditions, DateTimeOffset now, out ClientPrice price)
    {
        if (!_prices.TryGetLatest(instrument.Symbol, out var quote))
        {
            price = default;
            return RejectReason.NoPrice;
        }

        if (now - quote.Timestamp > _maxQuoteAge)
        {
            price = default;
            return RejectReason.StalePrice;
        }

        price = PriceBook.ToClientPrice(instrument, conditions, quote);
        return null;
    }

    private static PositionState? FindPosition(AccountState account, string? positionId) =>
        account.Positions.Find(p => string.Equals(p.Id, positionId, StringComparison.Ordinal));

    private static bool IsValidVolume(Instrument instrument, decimal volume) =>
        volume >= instrument.VolumeMin && volume <= instrument.VolumeMax && volume % instrument.VolumeStep == 0m;

    private static bool IsValidOptionalPrice(Instrument instrument, decimal? price) =>
        price is not { } value || (value > 0m && instrument.IsOnGrid(value));

    // Limit orders wait for a better price than now, stop orders for a worse one.
    private static bool IsValidPendingPrice(OrderType type, Side side, decimal orderPrice, ClientPrice current) =>
        (type, side) switch
        {
            (OrderType.Limit, Side.Buy) => orderPrice < current.Ask,
            (OrderType.Limit, Side.Sell) => orderPrice > current.Bid,
            (OrderType.Stop, Side.Buy) => orderPrice > current.Ask,
            (OrderType.Stop, Side.Sell) => orderPrice < current.Bid,
            _ => false,
        };

    // Stop loss must be on the losing side of the reference price and take profit on the winning side.
    private static RejectReason? ValidateStops(Side side, decimal reference, decimal? stopLoss, decimal? takeProfit)
    {
        if (stopLoss is { } sl && (side == Side.Buy ? sl >= reference : sl <= reference))
        {
            return RejectReason.InvalidStopLoss;
        }

        if (takeProfit is { } tp && (side == Side.Buy ? tp <= reference : tp >= reference))
        {
            return RejectReason.InvalidTakeProfit;
        }

        return null;
    }

    private static bool IsValidFloorRule(EquityFloorRule? rule) => rule switch
    {
        FixedFloor fixedFloor => fixedFloor.Level >= 0m,
        TrailingFloor trailing => trailing.Distance > 0m && trailing.LockLevel is null or >= 0m,
        _ => false,
    };
}
