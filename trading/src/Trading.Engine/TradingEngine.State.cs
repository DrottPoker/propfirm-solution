using Trading.Engine.Inputs;
using Trading.Engine.Internal;

namespace Trading.Engine;

// Export and restore of the complete state, for snapshots.
public sealed partial class TradingEngine
{
    /// <summary>Exports the complete state. Lists keep the order the engine processes them in.</summary>
    public EngineState ExportState() =>
        new(
            _clock,
            GetLatestQuotes(),
            _accounts
                .Select(a => new AccountRecord(
                    a.Id,
                    a.Group.Id,
                    a.Balance,
                    a.Status,
                    a.Positions
                        .Select(p => new PositionRecord(p.Id, p.Instrument.Symbol, p.Side, p.Volume, p.OpenPrice, p.StopLoss, p.TakeProfit, p.OpenTime))
                        .ToList(),
                    a.Orders
                        .Select(o => new OrderRecord(o.Id, o.Instrument.Symbol, o.Side, o.Type, o.Volume, o.Price, o.StopLoss, o.TakeProfit, o.PlacedTime))
                        .ToList(),
                    a.Floors.Values.Select(f => new FloorRecord(f.Id, f.Rule, f.HighWaterMark, f.Anchor)).ToList(),
                    a.UsedOrderIds.Order(StringComparer.Ordinal).ToList(),
                    a.UsedOperationIds.Order(StringComparer.Ordinal).ToList()))
                .ToList(),
            _createdGroups.Select(g => g.ToDefinition()).ToList());

    /// <summary>
    /// Creates an engine from exported state. Throws if the state does not fit the configuration,
    /// for example an account in a group that no longer exists, or a created group that is now configured.
    /// </summary>
    public static TradingEngine FromState(EngineConfiguration configuration, EngineState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var engine = new TradingEngine(configuration);
        engine.Restore(state);
        return engine;
    }

    /// <summary>The latest raw price per symbol, ordered by symbol.</summary>
    public IReadOnlyList<Quote> GetLatestQuotes() =>
        _prices.LatestQuotes.OrderBy(q => q.Symbol, StringComparer.Ordinal).ToList();

    private void Restore(EngineState state)
    {
        _clock = state.Clock;

        foreach (var quote in state.LatestQuotes)
        {
            Require(_instruments.ContainsKey(quote.Symbol), $"Price for unknown symbol {quote.Symbol}.");
            _prices.Update(quote);
        }

        // Accounts refer to groups, so the created groups come first.
        var symbols = _instruments.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var definition in state.Groups ?? [])
        {
            Require(!_groups.ContainsKey(definition.Id), $"Created group {definition.Id} is also configured, or appears twice.");
            Require(ConfigurationValidator.GroupProblem(definition, symbols) is null, $"Created group {definition.Id} does not fit the configuration.");
            var group = new GroupState(definition);
            _groups.Add(group.Id, group);
            _createdGroups.Add(group);
        }

        foreach (var record in state.Accounts)
        {
            Require(!_accountsById.ContainsKey(record.AccountId), $"Account {record.AccountId} appears twice.");
            Require(_groups.TryGetValue(record.GroupId, out var group), $"Account {record.AccountId} is in unknown group {record.GroupId}.");

            var account = new AccountState(record.AccountId, group, record.Balance) { Status = record.Status };
            foreach (var p in record.Positions)
            {
                var (instrument, conditions) = Tradable(account, p.Symbol);
                Require(_valuation.CanValue(instrument, group.Currency), $"Position {p.PositionId} cannot be valued without prices.");
                account.Positions.Add(new PositionState(p.PositionId, instrument, conditions, p.Side, p.Volume, p.OpenPrice, p.StopLoss, p.TakeProfit, p.OpenTime));
            }

            foreach (var o in record.Orders)
            {
                var (instrument, conditions) = Tradable(account, o.Symbol);
                account.Orders.Add(new OrderState(o.OrderId, instrument, conditions, o.Side, o.Type, o.Volume, o.Price, o.StopLoss, o.TakeProfit, o.PlacedTime));
            }

            foreach (var f in record.Floors)
            {
                account.Floors[f.FloorId] = new FloorState(f.FloorId, f.Rule, f.HighWaterMark, f.Anchor);
            }

            foreach (var orderId in record.UsedOrderIds)
            {
                account.UsedOrderIds.Add(orderId);
            }

            foreach (var operationId in record.UsedOperationIds ?? [])
            {
                account.UsedOperationIds.Add(operationId);
            }

            _accountsById.Add(account.Id, account);
            _accounts.Add(account);
        }
    }

    private (Instrument Instrument, SymbolConditions Conditions) Tradable(AccountState account, string symbol)
    {
        Require(_instruments.TryGetValue(symbol, out var instrument), $"Account {account.Id} has unknown symbol {symbol}.");
        Require(account.Group.TryGetConditions(symbol, out var conditions), $"Account {account.Id} has {symbol}, which group {account.Group.Id} cannot trade.");
        return (instrument, conditions);
    }

    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Cannot restore engine state: {message}");
        }
    }
}
