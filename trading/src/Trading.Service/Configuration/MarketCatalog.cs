using System.Diagnostics.CodeAnalysis;

using Trading.Engine;

namespace Trading.Service.Configuration;

/// <summary>Instrument and group lookups from the engine configuration.</summary>
internal sealed class MarketCatalog(EngineConfiguration configuration)
{
    private readonly Dictionary<string, Instrument> _instruments =
        configuration.Instruments.ToDictionary(i => i.Symbol, StringComparer.Ordinal);

    private readonly Dictionary<string, TradingGroup> _groups =
        configuration.Groups.ToDictionary(g => g.Id, StringComparer.Ordinal);

    public bool TryGet(string groupId, string symbol, [NotNullWhen(true)] out Instrument? instrument, [NotNullWhen(true)] out SymbolConditions? conditions)
    {
        conditions = _groups.TryGetValue(groupId, out var group)
            ? group.Symbols.FirstOrDefault(s => string.Equals(s.Symbol, symbol, StringComparison.Ordinal))
            : null;
        instrument = conditions is null ? null : _instruments[conditions.Symbol];
        return conditions is not null;
    }

    /// <summary>The group's instruments with their trading conditions. Shown to traders for transparency.</summary>
    public IReadOnlyList<InstrumentInfo> InstrumentsFor(string groupId) =>
        _groups.TryGetValue(groupId, out var group)
            ? group.Symbols
                .OrderBy(s => s.Symbol, StringComparer.Ordinal)
                .Select(s => InstrumentInfo.From(_instruments[s.Symbol], s))
                .ToList()
            : [];
}

/// <summary>An instrument as one group trades it.</summary>
public sealed record InstrumentInfo(
    string Symbol,
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
    public static InstrumentInfo From(Instrument instrument, SymbolConditions conditions) =>
        new(
            instrument.Symbol,
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
