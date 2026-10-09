using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Engine.Internal;

namespace Trading.Engine;

// Which accounts a price evaluates (ADR 0059). An account's value depends only on the latest prices of the symbols its
// positions are priced and converted with, so a price evaluates the accounts it can change and those a command or a new
// trading day changed since their last evaluation. Every other account is valued as it was when last evaluated, which
// then gave nothing, so evaluating it again would give nothing either. The events are the same as when every account is
// evaluated on every price, in the same order, while a price costs only as much as the accounts it concerns.
public sealed partial class TradingEngine
{
    // symbol -> the accounts whose value its price can change, kept as their positions and orders change.
    private readonly Dictionary<string, HashSet<AccountState>> _accountsPricedBy = new(StringComparer.Ordinal);

    // (symbol, account currency) -> the symbols whose prices value a position in it. Depends only on the configuration.
    private readonly Dictionary<(string Symbol, string Currency), HashSet<string>> _pricedBy = [];

    // Accounts a command or a new trading day changed since their last evaluation on a price.
    private readonly HashSet<AccountState> _changed = [];

    // No account's trading day starts before this, so a price before it starts none. Lowered when a command sets an
    // earlier start, and found again after every price that may have started a day.
    private DateTimeOffset _nextDayStart = DateTimeOffset.MinValue;

    // A trading day starts on the first price after it began, also on an account without exposure so its lock ends, so a
    // price that may start one looks at every account in turn. Any other price looks only at the accounts it concerns.
    private void EvaluateOnPrice(Instrument instrument, Quote quote, List<EngineEvent> events)
    {
        if (quote.Timestamp >= _nextDayStart)
        {
            foreach (var account in _accounts)
            {
                EvaluateOnPrice(account, instrument, quote, startDay: true, events);
            }

            _nextDayStart = EarliestDayStart();
            return;
        }

        foreach (var account in AccountsConcerned(quote.Symbol))
        {
            EvaluateOnPrice(account, instrument, quote, startDay: false, events);
        }
    }

    private void EvaluateOnPrice(AccountState account, Instrument instrument, Quote quote, bool startDay, List<EngineEvent> events)
    {
        // A disabled account and one without exposure are never evaluated on a price. A command changes them first.
        if (account.Status == AccountStatus.Disabled)
        {
            _changed.Remove(account);
            return;
        }

        if (startDay && StartTradingDayIfDue(account, quote.Timestamp, events))
        {
            _changed.Add(account);
        }

        if (!account.HasExposure)
        {
            _changed.Remove(account);
            return;
        }

        if (!_changed.Contains(account) && !account.PricedBy.Contains(quote.Symbol))
        {
            return;
        }

        ProcessTriggers(account, instrument, quote.Timestamp, events);
        EvaluateRisk(account, quote.Timestamp, events);
        _changed.Remove(account);
        Register(account);
    }

    // The accounts the price can change and those changed since their last evaluation, in creation order like every
    // evaluation, so their events come in the same order as when every account is looked at.
    private List<AccountState> AccountsConcerned(string symbol)
    {
        var accounts = new List<AccountState>(_changed);
        if (_accountsPricedBy.TryGetValue(symbol, out var priced))
        {
            foreach (var account in priced)
            {
                if (!_changed.Contains(account))
                {
                    accounts.Add(account);
                }
            }
        }

        accounts.Sort(static (a, b) => a.Order.CompareTo(b.Order));
        return accounts;
    }

    // After a command on the account: the next price evaluates it, whatever that price is for.
    private void AccountChanged(AccountState account)
    {
        _changed.Add(account);
        Register(account);
        if (account.Status != AccountStatus.Disabled && account.NextDayStart < _nextDayStart)
        {
            _nextDayStart = account.NextDayStart;
        }
    }

    // Files the account under every symbol whose price can change it now. As many positions and orders as before, all of
    // them filed already, leave it as it is: an evaluation seldom changes them. A symbol left over from a closed position
    // only makes a price evaluate the account once more than needed, which gives nothing, until it is filed again.
    private void Register(AccountState account)
    {
        if (account.Positions.Count + account.Orders.Count == account.FiledItems && IsFiled(account))
        {
            return;
        }

        account.FiledItems = account.Positions.Count + account.Orders.Count;
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var position in account.Positions)
        {
            symbols.UnionWith(PricedBy(position.Instrument, account.Group.Currency));
        }

        foreach (var order in account.Orders)
        {
            symbols.Add(order.Instrument.Symbol);
        }

        if (symbols.SetEquals(account.PricedBy))
        {
            return;
        }

        foreach (var symbol in account.PricedBy)
        {
            if (!symbols.Contains(symbol))
            {
                _accountsPricedBy[symbol].Remove(account);
            }
        }

        foreach (var symbol in symbols)
        {
            if (account.PricedBy.Add(symbol))
            {
                if (!_accountsPricedBy.TryGetValue(symbol, out var accounts))
                {
                    accounts = [];
                    _accountsPricedBy[symbol] = accounts;
                }

                accounts.Add(account);
            }
        }

        account.PricedBy.RemoveWhere(s => !symbols.Contains(s));
    }

    private bool IsFiled(AccountState account)
    {
        foreach (var position in account.Positions)
        {
            if (!account.PricedBy.IsSupersetOf(PricedBy(position.Instrument, account.Group.Currency)))
            {
                return false;
            }
        }

        foreach (var order in account.Orders)
        {
            if (!account.PricedBy.Contains(order.Instrument.Symbol))
            {
                return false;
            }
        }

        return true;
    }

    // The symbols whose prices value a position in the instrument for an account in the currency: its own, and those its
    // profit and margin are converted with.
    private HashSet<string> PricedBy(Instrument instrument, string currency)
    {
        if (_pricedBy.TryGetValue((instrument.Symbol, currency), out var known))
        {
            return known;
        }

        var symbols = new HashSet<string>(StringComparer.Ordinal) { instrument.Symbol };
        symbols.UnionWith(_prices.SymbolsOfRate(instrument.QuoteCurrency, currency));
        symbols.UnionWith(_prices.SymbolsOfRate(instrument.BaseCurrency, currency));
        _pricedBy[(instrument.Symbol, currency)] = symbols;
        return symbols;
    }

    private DateTimeOffset EarliestDayStart()
    {
        var earliest = DateTimeOffset.MaxValue;
        foreach (var account in _accounts)
        {
            if (account.Status != AccountStatus.Disabled && account.NextDayStart < earliest)
            {
                earliest = account.NextDayStart;
            }
        }

        return earliest;
    }
}
