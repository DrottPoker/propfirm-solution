using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Options;

using Trading.Engine;

namespace Trading.Service.Configuration;

/// <summary>
/// The configured instruments, and what a group trades of them. The group comes from the engine, since groups can
/// also be created while it runs (ADR 0016).
/// </summary>
internal sealed class MarketCatalog(EngineConfiguration configuration, IOptions<TradingOptions> trading)
{
    private readonly Dictionary<string, Instrument> _instruments =
        configuration.Instruments.ToDictionary(i => i.Symbol, StringComparer.Ordinal);

    private readonly Dictionary<string, InstrumentCategory> _categories =
        trading.Value.Instruments.ToDictionary(i => i.Symbol, i => i.Category ?? InstrumentCategory.Forex, StringComparer.Ordinal);

    public bool TryGet(TradingGroup group, string symbol, [NotNullWhen(true)] out Instrument? instrument, [NotNullWhen(true)] out SymbolConditions? conditions)
    {
        conditions = group.Symbols.FirstOrDefault(s => string.Equals(s.Symbol, symbol, StringComparison.Ordinal));
        instrument = conditions is null ? null : _instruments[conditions.Symbol];
        return conditions is not null;
    }

    /// <summary>Every instrument on the platform, by symbol.</summary>
    public IEnumerable<Instrument> All => _instruments.Values.OrderBy(i => i.Symbol, StringComparer.Ordinal);

    public InstrumentCategory CategoryOf(string symbol) => _categories[symbol];

    /// <summary>The group's instruments with their trading conditions. Shown to traders for transparency.</summary>
    public IReadOnlyList<InstrumentInfo> InstrumentsFor(TradingGroup group) =>
        group.Symbols
            .OrderBy(s => s.Symbol, StringComparer.Ordinal)
            .Select(s => InstrumentInfo.From(_instruments[s.Symbol], _categories[s.Symbol], s))
            .ToList();
}

/// <summary>An instrument as one group trades it.</summary>
public sealed record InstrumentInfo(
    string Symbol,
    InstrumentCategory Category,
    string BaseCurrency,
    string QuoteCurrency,
    decimal ContractSize,
    int Digits,
    decimal VolumeMin,
    decimal VolumeStep,
    decimal VolumeMax,
    int Leverage,
    int SpreadMarkupPoints,
    decimal CommissionPerLotPerSide)
{
    public static InstrumentInfo From(Instrument instrument, InstrumentCategory category, SymbolConditions conditions) =>
        new(
            instrument.Symbol,
            category,
            instrument.BaseCurrency,
            instrument.QuoteCurrency,
            instrument.ContractSize,
            instrument.Digits,
            instrument.VolumeMin,
            instrument.VolumeStep,
            instrument.VolumeMax,
            conditions.Leverage,
            conditions.SpreadMarkupPoints,
            conditions.CommissionPerLotPerSide);
}
