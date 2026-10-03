namespace Trading.Service.Feeds;

public sealed class PriceFeedOptions
{
    public const string SectionName = "PriceFeed";

    public const string SyntheticProvider = "Synthetic";
    public const string TiingoProvider = "Tiingo";

    /// <summary>Synthetic (made-up prices, the default) or Tiingo.</summary>
    public string Provider { get; init; } = SyntheticProvider;

    public TiingoOptions Tiingo { get; init; } = new();
}
