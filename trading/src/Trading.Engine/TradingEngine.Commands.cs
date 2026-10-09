using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Engine.Internal;

namespace Trading.Engine;

// Commands. Each handler validates everything before it changes state, and returns a reason when it rejects.
public sealed partial class TradingEngine
{
    private RejectReason? ApplyCreateGroup(CreateGroup command, List<EngineEvent> events)
    {
        if (command.Group is null || string.IsNullOrEmpty(command.Group.Id))
        {
            return RejectReason.InvalidId;
        }

        if (_groups.ContainsKey(command.Group.Id))
        {
            return RejectReason.DuplicateId;
        }

        if (ConfigurationValidator.GroupProblem(command.Group, _instruments.Keys.ToHashSet(StringComparer.Ordinal)) is not null)
        {
            return RejectReason.InvalidGroup;
        }

        var group = new GroupState(command.Group);
        _groups.Add(group.Id, group);
        _createdGroups.Add(group);
        events.Add(new GroupCreated(command.Timestamp, group.ToDefinition()));
        return null;
    }

    private RejectReason? ApplyChangeGroupSymbols(ChangeGroupSymbols command, List<EngineEvent> events)
    {
        if (!TryLookup(_groups, command.GroupId, out var group))
        {
            return RejectReason.UnknownGroup;
        }

        if (!_createdGroups.Contains(group))
        {
            return RejectReason.GroupNotChangeable;
        }

        var changed = new TradingGroup(group.Id, group.Currency, group.StopOutLevelPercent, command.Symbols);
        if (ConfigurationValidator.GroupProblem(changed, _instruments.Keys.ToHashSet(StringComparer.Ordinal)) is not null)
        {
            return RejectReason.InvalidGroup;
        }

        var kept = command.Symbols.Select(s => s.Symbol).ToHashSet(StringComparer.Ordinal);
        var accounts = _accounts.Where(a => a.Group == group).ToList();
        if (accounts.Any(a => a.Positions.Any(p => !kept.Contains(p.Instrument.Symbol)) || a.Orders.Any(o => !kept.Contains(o.Instrument.Symbol))))
        {
            return RejectReason.SymbolInUse;
        }

        group.ReplaceSymbols(command.Symbols);
        foreach (var account in accounts)
        {
            foreach (var position in account.Positions)
            {
                group.TryGetConditions(position.Instrument.Symbol, out var conditions);
                position.Conditions = conditions!;
            }

            foreach (var order in account.Orders)
            {
                group.TryGetConditions(order.Instrument.Symbol, out var conditions);
                order.Conditions = conditions!;
            }
        }

        events.Add(new GroupSymbolsChanged(command.Timestamp, group.ToDefinition()));

        // New leverage and markup change margin and equity at once, as a new price would.
        foreach (var account in accounts.Where(a => a.Status != AccountStatus.Disabled && a.HasExposure))
        {
            EvaluateRisk(account, command.Timestamp, events);
        }

        foreach (var account in accounts)
        {
            AccountChanged(account);
        }

        return null;
    }

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

        var account = new AccountState(command.AccountId, group, command.InitialBalance, TradingDay.Utc.NextStart(command.Timestamp)) { Order = _accounts.Count };
        _accountsById.Add(account.Id, account);
        _accounts.Add(account);
        events.Add(new AccountCreated(command.Timestamp, account.Id, group.Id, group.Currency, account.Balance));
        return null;
    }

    private RejectReason? ApplyPlaceOrder(PlaceOrder command, List<EngineEvent> events)
    {
        if (!TryGetOpenAccount(command.AccountId, out var account, out var accountRejection))
        {
            return accountRejection;
        }

        if (account.Status == AccountStatus.Suspended)
        {
            return RejectReason.AccountSuspended;
        }

        if (account.Lock is not null)
        {
            return RejectReason.AccountLocked;
        }

        if (account.OwnLimits.MaxTrades is { } maxTrades && account.TradesToday >= maxTrades)
        {
            return RejectReason.TradeLimitReached;
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

        if (GetTradingPrice(instrument, conditions, command.Timestamp, out var price) is { } priceRejection)
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

            if (!TryGetTrailingDistance(command.TrailingStop, price.ClosePrice(command.Side), command.StopLoss, out var marketTrailing))
            {
                return RejectReason.NoStopLoss;
            }

            var request = new OpenRequest(command.OrderId, instrument, conditions, command.Side, command.Volume, command.StopLoss, command.TakeProfit, marketTrailing);
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

        if (!TryGetTrailingDistance(command.TrailingStop, orderPrice, command.StopLoss, out var pendingTrailing))
        {
            return RejectReason.NoStopLoss;
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
            command.Timestamp,
            pendingTrailing));
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
            command.TakeProfit,
            pendingTrailing));
        return null;
    }

    private RejectReason? ApplyModifyOrder(ModifyOrder command, List<EngineEvent> events)
    {
        if (!TryGetOpenAccount(command.AccountId, out var account, out var accountRejection))
        {
            return accountRejection;
        }

        var order = account.Orders.Find(o => string.Equals(o.Id, command.OrderId, StringComparison.Ordinal));
        if (order is null)
        {
            return RejectReason.UnknownOrder;
        }

        if (!IsValidOptionalPrice(order.Instrument, command.StopLoss))
        {
            return RejectReason.InvalidStopLoss;
        }

        if (!IsValidOptionalPrice(order.Instrument, command.TakeProfit))
        {
            return RejectReason.InvalidTakeProfit;
        }

        if (GetTradingPrice(order.Instrument, order.Conditions, command.Timestamp, out var price) is { } priceRejection)
        {
            return priceRejection;
        }

        if (command.Price <= 0m || !order.Instrument.IsOnGrid(command.Price) || !IsValidPendingPrice(order.Type, order.Side, command.Price, price))
        {
            return RejectReason.InvalidPrice;
        }

        if (ValidateStops(order.Side, command.Price, command.StopLoss, command.TakeProfit) is { } stopsRejection)
        {
            return stopsRejection;
        }

        if (!TryGetTrailingDistance(command.TrailingStop, command.Price, command.StopLoss, out var trailing))
        {
            return RejectReason.NoStopLoss;
        }

        order.Price = command.Price;
        order.StopLoss = command.StopLoss;
        order.TakeProfit = command.TakeProfit;
        order.TrailingDistance = trailing;
        events.Add(new OrderModified(command.Timestamp, account.Id, order.Id, order.Instrument.Symbol, order.Price, order.StopLoss, order.TakeProfit, order.TrailingDistance));
        return null;
    }

    private RejectReason? ApplyCancelOrder(CancelOrder command, List<EngineEvent> events)
    {
        if (!TryGetOpenAccount(command.AccountId, out var account, out var accountRejection))
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
        if (!TryGetOpenAccount(command.AccountId, out var account, out var accountRejection))
        {
            return accountRejection;
        }

        var position = FindPosition(account, command.PositionId);
        if (position is null)
        {
            return RejectReason.UnknownPosition;
        }

        // A part must be a volume the instrument allows, and leave one.
        var part = command.Volume is { } volume && volume != position.Volume ? volume : (decimal?)null;
        if (part is { } partVolume && (!IsValidVolume(position.Instrument, partVolume) || position.Volume - partVolume < position.Instrument.VolumeMin))
        {
            return RejectReason.InvalidVolume;
        }

        if (GetTradingPrice(position.Instrument, position.Conditions, command.Timestamp, out var price) is { } priceRejection)
        {
            return priceRejection;
        }

        if (part is { } closed)
        {
            ClosePartAt(account, position, closed, price.ClosePrice(position.Side), CloseReason.Manual, command.Timestamp, events);
        }
        else
        {
            ClosePositionAt(account, position, price.ClosePrice(position.Side), CloseReason.Manual, command.Timestamp, events);
        }

        EvaluateRisk(account, command.Timestamp, events);
        return null;
    }

    private RejectReason? ApplyCloseAllPositions(CloseAllPositions command, List<EngineEvent> events)
    {
        if (!TryGetOpenAccount(command.AccountId, out var account, out var accountRejection))
        {
            return accountRejection;
        }

        var positions = account.Positions
            .Where(p => command.Symbol is null || string.Equals(p.Instrument.Symbol, command.Symbol, StringComparison.Ordinal))
            .ToList();
        if (positions.Count == 0)
        {
            return RejectReason.UnknownPosition;
        }

        // Each position closes at its own price, in the order they opened. One that cannot close now stays open.
        RejectReason? firstRejection = null;
        var closedAny = false;
        foreach (var position in positions)
        {
            if (GetTradingPrice(position.Instrument, position.Conditions, command.Timestamp, out var price) is { } rejection)
            {
                firstRejection ??= rejection;
                continue;
            }

            ClosePositionAt(account, position, price.ClosePrice(position.Side), CloseReason.Manual, command.Timestamp, events);
            closedAny = true;
        }

        if (!closedAny)
        {
            return firstRejection;
        }

        EvaluateRisk(account, command.Timestamp, events);
        return null;
    }

    private RejectReason? ApplyModifyPosition(ModifyPosition command, List<EngineEvent> events)
    {
        if (!TryGetOpenAccount(command.AccountId, out var account, out var accountRejection))
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

        if (GetTradingPrice(position.Instrument, position.Conditions, command.Timestamp, out var price) is { } priceRejection)
        {
            return priceRejection;
        }

        if (ValidateStops(position.Side, price.ClosePrice(position.Side), command.StopLoss, command.TakeProfit) is { } stopsRejection)
        {
            return stopsRejection;
        }

        if (!TryGetTrailingDistance(command.TrailingStop, price.ClosePrice(position.Side), command.StopLoss, out var trailing))
        {
            return RejectReason.NoStopLoss;
        }

        position.StopLoss = command.StopLoss;
        position.TakeProfit = command.TakeProfit;
        position.TrailingDistance = trailing;
        events.Add(new PositionModified(command.Timestamp, account.Id, position.Id, position.StopLoss, position.TakeProfit, position.TrailingDistance));
        return null;
    }

    private RejectReason? ApplySetEquityFloor(SetEquityFloor command, List<EngineEvent> events)
    {
        if (!TryGetOpenAccount(command.AccountId, out var account, out var accountRejection))
        {
            return accountRejection;
        }

        if (string.IsNullOrEmpty(command.FloorId) || !IsValidFloorRule(command.Rule))
        {
            return RejectReason.InvalidFloor;
        }

        // A new or replaced floor starts trailing from the current equity, and an anchored one is measured now.
        var equity = _valuation.Measure(account).Equity;
        decimal? anchor = command.Rule is AnchoredFloor anchored
            ? anchored.Anchor == FloorAnchor.Balance ? account.Balance : Math.Max(account.Balance, equity)
            : null;
        var floor = new FloorState(command.FloorId, command.Rule, equity, anchor);
        account.Floors[floor.Id] = floor;
        events.Add(new EquityFloorSet(command.Timestamp, account.Id, floor.Id, floor.Rule, floor.Level));
        EvaluateRisk(account, command.Timestamp, events);
        return null;
    }

    private RejectReason? ApplyRemoveEquityFloor(RemoveEquityFloor command, List<EngineEvent> events)
    {
        if (!TryGetOpenAccount(command.AccountId, out var account, out var accountRejection))
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
        if (!TryGetOpenAccount(command.AccountId, out var account, out var accountRejection))
        {
            return accountRejection;
        }

        Liquidate(account, CloseReason.AccountClosed, CancelReason.AccountClosed, command.Timestamp, events);
        Disable(account, DisableReason.Closed, command.Timestamp, events);
        return null;
    }

    private RejectReason? ApplySuspendAccount(SuspendAccount command, List<EngineEvent> events)
    {
        if (!TryGetOpenAccount(command.AccountId, out var account, out var accountRejection))
        {
            return accountRejection;
        }

        if (account.Status == AccountStatus.Suspended)
        {
            return RejectReason.AccountSuspended;
        }

        // A pending order would open a position, which a suspended account may not.
        foreach (var order in account.Orders)
        {
            events.Add(new OrderCancelled(command.Timestamp, account.Id, order.Id, CancelReason.AccountSuspended));
        }

        account.Orders.Clear();
        account.Status = AccountStatus.Suspended;
        events.Add(new AccountSuspended(command.Timestamp, account.Id));
        return null;
    }

    private RejectReason? ApplyResumeAccount(ResumeAccount command, List<EngineEvent> events)
    {
        if (!TryGetOpenAccount(command.AccountId, out var account, out var accountRejection))
        {
            return accountRejection;
        }

        if (account.Status != AccountStatus.Suspended)
        {
            return RejectReason.AccountNotSuspended;
        }

        account.Status = AccountStatus.Active;
        events.Add(new AccountResumed(command.Timestamp, account.Id));
        return null;
    }

    private RejectReason? ApplyReopenAccount(ReopenAccount command, List<EngineEvent> events)
    {
        if (!TryLookup(_accountsById, command.AccountId, out var account))
        {
            return RejectReason.UnknownAccount;
        }

        if (account.Status != AccountStatus.Disabled)
        {
            return RejectReason.AccountNotDisabled;
        }

        if (command.Balance <= 0m || !_valuation.IsRounded(command.Balance, account.Group.Currency))
        {
            return RejectReason.InvalidAmount;
        }

        account.Balance = command.Balance;
        account.Status = AccountStatus.Active;
        events.Add(new AccountReopened(command.Timestamp, account.Id, command.Balance));

        // The day counts from the new balance, and the trader's own lock no longer holds.
        account.DayStartBalance = command.Balance;
        account.NextDayStart = account.TradingDay.NextStart(command.Timestamp);
        Unlock(account, command.Timestamp, events);

        // The limits that ended the account would end it again, so the firm sets new ones.
        foreach (var floorId in account.Floors.Keys.ToList())
        {
            account.Floors.Remove(floorId);
            events.Add(new EquityFloorRemoved(command.Timestamp, account.Id, floorId));
        }

        return null;
    }

    private RejectReason? ApplyAdjustBalance(AdjustBalance command, List<EngineEvent> events)
    {
        if (!TryGetOpenAccount(command.AccountId, out var account, out var accountRejection))
        {
            return accountRejection;
        }

        if (string.IsNullOrEmpty(command.OperationId))
        {
            return RejectReason.InvalidId;
        }

        if (account.UsedOperationIds.Contains(command.OperationId))
        {
            return RejectReason.DuplicateId;
        }

        var currency = account.Group.Currency;
        if (command.Amount == 0m || !_valuation.IsRounded(command.Amount, currency)
            || (command.MinBalance is { } min && (min < 0m || !_valuation.IsRounded(min, currency))))
        {
            return RejectReason.InvalidAmount;
        }

        // A withdrawal may not leave too little, cause a stop out or breach a floor.
        var figures = _valuation.Measure(account);
        var balanceAfter = account.Balance + command.Amount;
        if (command.Amount < 0m
            && (balanceAfter < (command.MinBalance ?? 0m)
                || -command.Amount > figures.FreeMargin
                || account.Floors.Values.Any(f => figures.Equity + command.Amount < f.LevelShiftedBy(command.Amount))))
        {
            return RejectReason.InsufficientFunds;
        }

        account.Balance = balanceAfter;
        account.DayStartBalance += command.Amount;
        account.UsedOperationIds.Add(command.OperationId);
        foreach (var floor in account.Floors.Values)
        {
            floor.Shift(command.Amount);
        }

        events.Add(new BalanceAdjusted(command.Timestamp, account.Id, command.OperationId, command.Amount, account.Balance));
        EvaluateRisk(account, command.Timestamp, events);
        return null;
    }

    // The price a trader's order, close or stop change is checked against. None while the market is closed, even if a
    // price came in after it closed, and none that is too old.
    private RejectReason? GetTradingPrice(Instrument instrument, SymbolConditions conditions, DateTimeOffset now, out ClientPrice price)
    {
        if (!instrument.IsOpen(now))
        {
            price = default;
            return RejectReason.MarketClosed;
        }

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

    // A trailing stop keeps the distance from the reference price to the stop loss, so it needs a stop loss. Without
    // a trailing stop the distance is null.
    private static bool TryGetTrailingDistance(bool trailingStop, decimal reference, decimal? stopLoss, out decimal? distance)
    {
        distance = trailingStop && stopLoss is { } level ? Math.Abs(reference - level) : null;
        return !trailingStop || stopLoss is not null;
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
        AnchoredFloor anchored => anchored.Distance > 0m && Enum.IsDefined(anchored.Anchor),
        _ => false,
    };
}
