using System.Diagnostics.CodeAnalysis;

namespace Trading.Engine.Internal;

internal sealed class GroupState
{
    private Dictionary<string, SymbolConditions> _symbols;

    public GroupState(TradingGroup group)
    {
        Id = group.Id;
        Currency = group.Currency;
        StopOutLevelPercent = group.StopOutLevelPercent;
        _symbols = group.Symbols.ToDictionary(s => s.Symbol, StringComparer.Ordinal);
    }

    public string Id { get; }

    public string Currency { get; }

    public decimal StopOutLevelPercent { get; }

    public IEnumerable<SymbolConditions> Symbols => _symbols.Values.OrderBy(s => s.Symbol, StringComparer.Ordinal);

    public bool TryGetConditions(string symbol, [NotNullWhen(true)] out SymbolConditions? conditions) =>
        _symbols.TryGetValue(symbol, out conditions);

    /// <summary>Replaces the symbols. The caller has validated them.</summary>
    public void ReplaceSymbols(IEnumerable<SymbolConditions> symbols) =>
        _symbols = symbols.ToDictionary(s => s.Symbol, StringComparer.Ordinal);

    /// <summary>The group as a definition, with its symbols in symbol order.</summary>
    public TradingGroup ToDefinition() => new(Id, Currency, StopOutLevelPercent, Symbols.ToList());
}
