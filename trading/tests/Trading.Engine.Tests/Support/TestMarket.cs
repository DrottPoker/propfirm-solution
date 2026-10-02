namespace Trading.Engine.Tests.Support;

internal static class TestMarket
{
    public static readonly DateTimeOffset Start = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    public static readonly Instrument EurUsd = new("EURUSD", "EUR", "USD", 100_000m, 5, 0.01m, 0.01m, 100m);
    public static readonly Instrument GbpUsd = new("GBPUSD", "GBP", "USD", 100_000m, 5, 0.01m, 0.01m, 100m);
    public static readonly Instrument EurGbp = new("EURGBP", "EUR", "GBP", 100_000m, 5, 0.01m, 0.01m, 100m);
    public static readonly Instrument UsdJpy = new("USDJPY", "USD", "JPY", 100_000m, 3, 0.01m, 0.01m, 100m);
    public static readonly Instrument XauUsd = new("XAUUSD", "XAU", "USD", 100m, 2, 0.01m, 0.01m, 50m);

    public static IReadOnlyList<Instrument> AllInstruments { get; } = [EurUsd, GbpUsd, EurGbp, UsdJpy, XauUsd];

    /// <summary>One USD group named "standard". Leverage 100, or 30 for gold.</summary>
    public static EngineConfiguration Configuration(
        decimal commission = 0m,
        int markupPoints = 0,
        decimal stopOutLevel = 50m,
        IReadOnlyList<Instrument>? instruments = null,
        IReadOnlyList<string>? tradableSymbols = null)
    {
        var allInstruments = instruments ?? AllInstruments;
        var symbols = allInstruments
            .Where(i => tradableSymbols is null || tradableSymbols.Contains(i.Symbol))
            .Select(i => new SymbolConditions(i.Symbol, i == XauUsd ? 30 : 100, markupPoints, commission))
            .ToList();
        return new EngineConfiguration(
            allInstruments,
            [new TradingGroup("standard", "USD", stopOutLevel, symbols)],
            TimeSpan.FromSeconds(5));
    }
}
