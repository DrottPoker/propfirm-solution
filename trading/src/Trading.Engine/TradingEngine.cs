using System.Diagnostics.CodeAnalysis;

using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Engine.Internal;

namespace Trading.Engine;

/// <summary>
/// Deterministic core of the trading engine: the same inputs always produce the same events.
/// Simulated execution only. Not thread-safe: apply inputs one at a time, in order.
/// </summary>
public sealed partial class TradingEngine
{
    private readonly Dictionary<string, Instrument> _instruments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GroupState> _groups = new(StringComparer.Ordinal);

    // Groups created by input, in creation order. Configured groups come from the configuration instead.
    private readonly List<GroupState> _createdGroups = [];
    private readonly Dictionary<string, AccountState> _accountsById = new(StringComparer.Ordinal);

    // Creation order, for deterministic iteration.
    private readonly List<AccountState> _accounts = [];

    private readonly PriceBook _prices;
    private readonly Valuation _valuation;
    private readonly TimeSpan _maxQuoteAge;
    private DateTimeOffset _clock = DateTimeOffset.MinValue;

    public TradingEngine(EngineConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ConfigurationValidator.Validate(configuration);

        foreach (var instrument in configuration.Instruments)
        {
            _instruments.Add(instrument.Symbol, instrument);
        }

        foreach (var group in configuration.Groups)
        {
            _groups.Add(group.Id, new GroupState(group));
        }

        _prices = new PriceBook(configuration.Instruments);
        _valuation = new Valuation(_prices, configuration.CurrencyDecimals);
        _maxQuoteAge = configuration.MaxQuoteAge;
    }

    /// <summary>Applies one input and returns the events it caused, in order. Invalid inputs give an <see cref="InputRejected"/> event.</summary>
    public IReadOnlyList<EngineEvent> Apply(EngineInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var events = new List<EngineEvent>();
        if (input.Timestamp < _clock)
        {
            events.Add(new InputRejected(input.Timestamp, input, RejectReason.OutOfOrder));
            return events;
        }

        _clock = input.Timestamp;

        var rejection = input switch
        {
            Quote quote => ApplyQuote(quote, events),
            CreateGroup command => ApplyCreateGroup(command, events),
            ChangeGroupSymbols command => ApplyChangeGroupSymbols(command, events),
            CreateAccount command => ApplyCreateAccount(command, events),
            PlaceOrder command => ApplyPlaceOrder(command, events),
            ModifyOrder command => ApplyModifyOrder(command, events),
            CancelOrder command => ApplyCancelOrder(command, events),
            ClosePosition command => ApplyClosePosition(command, events),
            CloseAllPositions command => ApplyCloseAllPositions(command, events),
            ModifyPosition command => ApplyModifyPosition(command, events),
            SetEquityFloor command => ApplySetEquityFloor(command, events),
            RemoveEquityFloor command => ApplyRemoveEquityFloor(command, events),
            CloseAccount command => ApplyCloseAccount(command, events),
            SuspendAccount command => ApplySuspendAccount(command, events),
            ResumeAccount command => ApplyResumeAccount(command, events),
            AdjustBalance command => ApplyAdjustBalance(command, events),
            _ => throw new ArgumentException($"Unknown input type {input.GetType().Name}.", nameof(input)),
        };

        if (rejection is { } reason)
        {
            events.Add(new InputRejected(input.Timestamp, input, reason));
        }

        return events;
    }

    /// <summary>Returns the account valued at the latest prices, or null if it does not exist.</summary>
    public AccountSnapshot? GetAccount(string accountId)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        return _accountsById.TryGetValue(accountId, out var account) ? Snapshot(account) : null;
    }

    /// <summary>The account's trading group, or null if the account does not exist. Cheap, unlike <see cref="GetAccount"/>.</summary>
    public string? GetGroupId(string accountId)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        return _accountsById.TryGetValue(accountId, out var account) ? account.Group.Id : null;
    }

    /// <summary>The group's conditions, with its symbols in symbol order, or null if the group does not exist.</summary>
    public TradingGroup? GetGroup(string groupId)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        return _groups.TryGetValue(groupId, out var group) ? group.ToDefinition() : null;
    }

    /// <summary>Whether the group was created with <see cref="CreateGroup"/>, so its symbols can be changed. False for configured and unknown groups.</summary>
    public bool IsCreatedGroup(string groupId)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        return _groups.TryGetValue(groupId, out var group) && _createdGroups.Contains(group);
    }

    /// <summary>
    /// What a point of the symbol is worth on the account, for turning an amount of money into a stop loss or take profit.
    /// Null if the account does not exist, its group cannot trade the symbol, or there is no conversion rate yet.
    /// </summary>
    public PointValue? GetPointValue(string accountId, string symbol)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        ArgumentNullException.ThrowIfNull(symbol);
        if (!_accountsById.TryGetValue(accountId, out var account)
            || !_instruments.TryGetValue(symbol, out var instrument)
            || !account.Group.TryGetConditions(symbol, out _))
        {
            return null;
        }

        var currency = account.Group.Currency;
        return _valuation.TryGetPointValue(instrument, currency, out var perLot) ? new PointValue(symbol, currency, perLot) : null;
    }

    /// <summary>Latest prices after the group's markup, for each of the group's symbols that has a price. Null if the group does not exist.</summary>
    public IReadOnlyList<SymbolPrice>? GetPrices(string groupId)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        if (!_groups.TryGetValue(groupId, out var group))
        {
            return null;
        }

        var prices = new List<SymbolPrice>();
        foreach (var conditions in group.Symbols)
        {
            if (_prices.TryGetLatest(conditions.Symbol, out var quote))
            {
                var price = PriceBook.ToClientPrice(_instruments[conditions.Symbol], conditions, quote);
                prices.Add(new SymbolPrice(conditions.Symbol, price.Bid, price.Ask, price.Timestamp));
            }
        }

        return prices;
    }

    private AccountSnapshot Snapshot(AccountState account)
    {
        var figures = _valuation.Measure(account);
        return new AccountSnapshot(
            account.Id,
            account.Group.Id,
            account.Group.Currency,
            account.Status,
            figures.Balance,
            figures.Equity,
            figures.UsedMargin,
            figures.FreeMargin,
            figures.MarginLevelPercent,
            PositionsOf(account),
            account.Orders
                .Select(o => new OrderSnapshot(o.Id, o.Instrument.Symbol, o.Side, o.Type, o.Volume, o.Price, o.StopLoss, o.TakeProfit, o.PlacedTime, o.TrailingDistance))
                .ToList(),
            account.Floors.Values
                .Select(f => new FloorSnapshot(f.Id, f.Rule, f.Level, f.HighWaterMark, figures.Equity - f.Level))
                .ToList());
    }

    private List<PositionSnapshot> PositionsOf(AccountState account)
    {
        var currency = account.Group.Currency;
        return account.Positions
            .Select(p =>
            {
                var closePrice = _valuation.CurrentPrice(p.Instrument, p.Conditions).ClosePrice(p.Side);
                return new PositionSnapshot(
                    p.Id,
                    p.Instrument.Symbol,
                    p.Side,
                    p.Volume,
                    p.OpenPrice,
                    p.StopLoss,
                    p.TakeProfit,
                    p.OpenTime,
                    closePrice,
                    _valuation.Profit(p, closePrice, currency),
                    _valuation.Margin(p.Instrument, p.Conditions, p.Volume, currency),
                    p.TrailingDistance);
            })
            .ToList();
    }

    // Prices of all symbols the account has positions in, as seen by the account.
    private List<SymbolPrice> PricesOf(AccountState account) =>
        account.Positions
            .GroupBy(p => p.Instrument.Symbol, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var position = g.First();
                var price = _valuation.CurrentPrice(position.Instrument, position.Conditions);
                return new SymbolPrice(g.Key, price.Bid, price.Ask, price.Timestamp);
            })
            .ToList();

    // An account that is not disabled. A suspended one can still close positions, change stops, move money and be closed.
    private bool TryGetOpenAccount(string? accountId, [NotNullWhen(true)] out AccountState? account, out RejectReason rejection)
    {
        if (!TryLookup(_accountsById, accountId, out account))
        {
            rejection = RejectReason.UnknownAccount;
            return false;
        }

        if (account.Status == AccountStatus.Disabled)
        {
            account = null;
            rejection = RejectReason.AccountDisabled;
            return false;
        }

        rejection = default;
        return true;
    }

    private static bool TryLookup<T>(Dictionary<string, T> map, string? key, [MaybeNullWhen(false)] out T value)
    {
        if (key is null)
        {
            value = default;
            return false;
        }

        return map.TryGetValue(key, out value);
    }
}
