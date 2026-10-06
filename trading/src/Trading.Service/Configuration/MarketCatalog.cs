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
    // How far ahead the sessions of a market are listed for the terminal.
    private static readonly TimeSpan SessionsAhead = TimeSpan.FromDays(7);

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

    /// <summary>When each of the group's symbols can be traded, seen from <paramref name="now"/>, by symbol (ADR 0050).</summary>
    public IReadOnlyList<MarketHours> MarketHoursFor(TradingGroup group, DateTimeOffset now) =>
        group.Symbols
            .OrderBy(s => s.Symbol, StringComparer.Ordinal)
            .Select(s => _instruments[s.Symbol].TradingHours is { } hours
                ? new MarketHours(s.Symbol, hours.IsOpen(now), hours.NextChange(now), hours.PeriodsBetween(now, now + SessionsAhead))
                : new MarketHours(s.Symbol, true, null, null))
            .ToList();
}

/// <summary>When a symbol's market is open, seen from the moment it was asked. A market that never closes has no next change and no sessions.</summary>
/// <param name="NextChange">When the market closes if it is open, or opens if it is closed. Null if it never closes, or does not open within a month.</param>
/// <param name="Sessions">The periods the market is open from now and a week on, in UTC, the current one first. Null if it never closes.</param>
public sealed record MarketHours(string Symbol, bool IsOpen, DateTimeOffset? NextChange, IReadOnlyList<MarketPeriod>? Sessions);

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
