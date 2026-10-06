namespace Trading.Service.Feeds;

public sealed class PriceFeedOptions
{
    public const string SectionName = "PriceFeed";

    public const string SyntheticProvider = "Synthetic";
    public const string TiingoProvider = "Tiingo";
    public const string CapitalComProvider = "CapitalCom";

    /// <summary>Every provider, by the name it is stored with.</summary>
    public static readonly IReadOnlyList<string> Providers = [SyntheticProvider, TiingoProvider, CapitalComProvider];

    /// <summary>Synthetic (made-up prices, the default), Tiingo or CapitalCom.</summary>
    public string Provider { get; init; } = SyntheticProvider;

    public TiingoOptions Tiingo { get; init; } = new();

    public CapitalComOptions CapitalCom { get; init; } = new();
}
