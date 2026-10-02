namespace Trading.Engine;

/// <summary>Static configuration of the engine.</summary>
/// <param name="MaxQuoteAge">Orders are rejected when the latest price is older than this.</param>
public sealed record EngineConfiguration(
    IReadOnlyList<Instrument> Instruments,
    IReadOnlyList<TradingGroup> Groups,
    TimeSpan MaxQuoteAge)
{
    /// <summary>Decimals per currency used when rounding amounts. Currencies not listed use 2.</summary>
    public IReadOnlyDictionary<string, int> CurrencyDecimals { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal) { ["JPY"] = 0 };
}
