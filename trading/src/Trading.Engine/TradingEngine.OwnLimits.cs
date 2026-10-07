using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Engine.Internal;

namespace Trading.Engine;

// The trader's own limits, the lock until the next trading day, and the trading day they count in (ADR 0054).
public sealed partial class TradingEngine
{
    private RejectReason? ApplySetTradingDay(SetTradingDay command, List<EngineEvent> events)
    {
        if (!TryGetOpenAccount(command.AccountId, out var account, out var accountRejection))
        {
            return accountRejection;
        }

        if (command.Day is null || command.Day.FindProblem() is not null)
        {
            return RejectReason.InvalidTradingDay;
        }

        account.TradingDay = command.Day;
        account.NextDayStart = command.Day.NextStart(command.Timestamp);
        if (account.Lock is { } held)
        {
            account.Lock = held with { Until = account.NextDayStart };
        }

        events.Add(new TradingDaySet(command.Timestamp, account.Id, command.Day, account.NextDayStart));
        return null;
    }

    private RejectReason? ApplySetOwnLimits(SetOwnLimits command, List<EngineEvent> events)
    {
        if (!TryGetOpenAccount(command.AccountId, out var account, out var accountRejection))
        {
            return accountRejection;
        }

        var wanted = command.Limits;
        var currency = account.Group.Currency;
        if (wanted is null
            || !IsValidLimit(wanted.DailyLoss, currency)
            || !IsValidLimit(wanted.DailyTarget, currency)
            || wanted.MaxTrades is < 1 or > OwnLimits.MostTrades)
        {
            return RejectReason.InvalidLimits;
        }

        var limits = account.OwnLimits.StricterOf(wanted);
        account.OwnLimits = limits;
        account.PendingOwnLimits = wanted == limits ? null : wanted;
        events.Add(new OwnLimitsSet(command.Timestamp, account.Id, limits, account.PendingOwnLimits));

        // A limit the day has already passed is reached at once.
        CheckOwnLimits(account, command.Timestamp, events);
        return null;
    }

    private RejectReason? ApplyLockTrading(LockTrading command, List<EngineEvent> events)
    {
        if (!TryGetOpenAccount(command.AccountId, out var account, out var accountRejection))
        {
            return accountRejection;
        }

        if (account.Lock is not null)
        {
            return RejectReason.AccountLocked;
        }

        // Only at fresh prices, as when the trader closes them, so nothing closes at an old price.
        var before = events.Count;
        if (command.ClosePositions)
        {
            foreach (var position in account.Positions.ToList())
            {
                if (GetTradingPrice(position.Instrument, position.Conditions, command.Timestamp, out var price) is null)
                {
                    ClosePositionAt(account, position, price.ClosePrice(position.Side), CloseReason.OwnLimit, command.Timestamp, events);
                }
            }
        }

        Lock(account, LockReason.Trader, null, ClosesSince(events, before), command.Timestamp, events);
        EvaluateRisk(account, command.Timestamp, events);
        return null;
    }

    // The first input on or after the start of the account's next trading day starts it: the day counts from the balance
    // now, which no trade in between has changed, the trades start again, the limits loosened yesterday apply, and the
    // lock ends. An account without own limits or a lock starts its day without an event.
    private static void StartTradingDayIfDue(AccountState account, DateTimeOffset now, List<EngineEvent> events)
    {
        if (account.Status == AccountStatus.Disabled || now < account.NextDayStart)
        {
            return;
        }

        var told = !account.OwnLimits.IsEmpty || account.PendingOwnLimits is not null || account.Lock is not null;
        account.DayStartBalance = account.Balance;
        account.TradesToday = 0;
        account.NextDayStart = account.TradingDay.NextStart(now);
        if (account.PendingOwnLimits is { } pending)
        {
            account.OwnLimits = pending;
            account.PendingOwnLimits = null;
        }

        if (told)
        {
            events.Add(new TradingDayStarted(now, account.Id, account.DayStartBalance, account.OwnLimits, account.NextDayStart));
        }

        Unlock(account, now, events);
    }

    // Reaching the trader's own daily loss limit or profit target closes every position and locks new orders, like a
    // floor but without ending the account.
    private void CheckOwnLimits(AccountState account, DateTimeOffset now, List<EngineEvent> events)
    {
        if (account.Lock is not null || account.Status == AccountStatus.Disabled)
        {
            return;
        }

        var equity = _valuation.Measure(account).Equity;
        var (reason, level) = account switch
        {
            { LossLevel: { } loss } when equity <= loss => (LockReason.DailyLoss, loss),
            { TargetLevel: { } target } when equity >= target => (LockReason.DailyTarget, target),
            _ => ((LockReason?)null, 0m),
        };
        if (reason is not { } reached)
        {
            return;
        }

        events.Add(new OwnLimitReached(now, account.Id, reached, level, equity));
        var before = events.Count;
        Liquidate(account, CloseReason.OwnLimit, CancelReason.TradingLocked, now, events);
        var limit = reached == LockReason.DailyLoss ? account.OwnLimits.DailyLoss : account.OwnLimits.DailyTarget;
        Lock(account, reached, limit, ClosesSince(events, before), now, events);
    }

    // Pending orders would open positions, so they go when new orders lock.
    private void Lock(AccountState account, LockReason reason, decimal? limit, int positionsClosed, DateTimeOffset now, List<EngineEvent> events)
    {
        foreach (var order in account.Orders)
        {
            events.Add(new OrderCancelled(now, account.Id, order.Id, CancelReason.TradingLocked));
        }

        account.Orders.Clear();
        account.Lock = new OwnLock(account.NextDayStart, reason);
        var dayResult = _valuation.Measure(account).Equity - account.DayStartBalance;
        events.Add(new TradingLocked(now, account.Id, reason, account.NextDayStart, limit, dayResult, positionsClosed));
    }

    private static void Unlock(AccountState account, DateTimeOffset now, List<EngineEvent> events)
    {
        if (account.Lock is null)
        {
            return;
        }

        account.Lock = null;
        events.Add(new TradingUnlocked(now, account.Id));
    }

    private static int ClosesSince(List<EngineEvent> events, int index) => events.Skip(index).OfType<PositionClosed>().Count();

    private bool IsValidLimit(decimal? amount, string currency) =>
        amount is not { } value || (value > 0m && _valuation.IsRounded(value, currency));
}
