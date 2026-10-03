namespace Prop.Api.Configuration;

/// <summary>Where the trading platform's admin API is, and how the event stream is read.</summary>
public sealed class TradingPlatformOptions
{
    public const string SectionName = "TradingPlatform";

    /// <summary>The trading service, for example http://localhost:5101/.</summary>
    public Uri? Url { get; init; }

    /// <summary>How long a request for events waits for new ones. The trading platform allows up to 30 seconds.</summary>
    public int EventWaitSeconds { get; init; } = 30;

    public int EventsPerRequest { get; init; } = 500;
}

/// <summary>A firm on the prop platform. Configured for now; managed through an API when firms sign up themselves.</summary>
public sealed class FirmOptions
{
    public const string SectionName = "Firms";

    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    /// <summary>SHA-256 of the key the firm's own systems use for the firm API, as lowercase hex.</summary>
    public string ApiKeySha256 { get; init; } = "";

    public FirmTradingOptions Trading { get; init; } = new();

    public FirmWebhookOptions Webhook { get; init; } = new();

    /// <summary>Challenges created at startup if the firm does not have them yet.</summary>
    public IReadOnlyList<SeedChallengeOptions> SeedChallenges { get; init; } = [];
}

/// <summary>The firm's server on the trading platform. The API key is a secret: keep it out of files outside development.</summary>
public sealed class FirmTradingOptions
{
    public string Server { get; init; } = "";

    public string ApiKey { get; init; } = "";

    /// <summary>The trading group new accounts are opened in.</summary>
    public string Group { get; init; } = "";
}

public sealed class FirmWebhookOptions
{
    /// <summary>Where the firm receives webhooks. Empty for none.</summary>
    public Uri? Url { get; init; }

    /// <summary>Signs every webhook. A secret: keep it out of files outside development.</summary>
    public string Secret { get; init; } = "";
}

public sealed class SeedChallengeOptions
{
    public const string TwoStepTemplate = "TwoStep";

    public string Template { get; init; } = TwoStepTemplate;

    public string Id { get; init; } = "";

    public decimal InitialBalance { get; init; }

    public string Currency { get; init; } = "USD";
}
