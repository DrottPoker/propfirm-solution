namespace Trading.Service.Configuration;

public sealed class TerminalOptions
{
    public const string SectionName = "Terminal";

    /// <summary>Where traders open the terminal, for example https://trade.example.com/. Used in login links.</summary>
    public Uri? Url { get; init; }
}
