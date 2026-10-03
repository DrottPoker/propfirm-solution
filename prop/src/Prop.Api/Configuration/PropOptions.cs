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

/// <summary>Rules for portal passwords, logins and sessions. Development turns them off; elsewhere the defaults hold.</summary>
public sealed class LoginOptions
{
    public const string SectionName = "Login";

    /// <summary>The shortest password a trader may choose.</summary>
    public int MinimumPasswordLength { get; init; } = 10;

    /// <summary>Login attempts per minute from one address, or 0 for no limit.</summary>
    public int AttemptsPerMinute { get; init; } = 10;

    /// <summary>How long a session lasts without being used.</summary>
    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromHours(12);
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

    public FirmPortalOptions Portal { get; init; } = new();

    /// <summary>Challenges created at startup if the firm does not have them yet.</summary>
    public IReadOnlyList<SeedChallengeOptions> SeedChallenges { get; init; } = [];

    /// <summary>Administrators created at startup, or given the configured password. For development, until firms sign up themselves.</summary>
    public IReadOnlyList<SeedLoginOptions> SeedAdmins { get; init; } = [];

    /// <summary>Traders created at startup, or given the configured password. For development, so testing needs no invitation.</summary>
    public IReadOnlyList<SeedLoginOptions> SeedTraders { get; init; } = [];
}

/// <summary>The firm's white label portal: where it is reached and how it looks.</summary>
public sealed class FirmPortalOptions
{
    /// <summary>The portal's main address, ending with /, used in invitation links. For example https://portal.firm.com/.</summary>
    public Uri? Url { get; init; }

    /// <summary>Host names the portal is reached on, without port. The firm is known from them.</summary>
    public IReadOnlyList<string> Hosts { get; init; } = [];

    /// <summary>An absolute https address, or empty for none.</summary>
    public string LogoUrl { get; init; } = "";

    /// <summary>Overrides of the portal's theme colors, as #rrggbb.</summary>
    public IReadOnlyDictionary<string, string> Colors { get; init; } = new Dictionary<string, string>();
}

public sealed class SeedLoginOptions
{
    public string Email { get; init; } = "";

    public string Password { get; init; } = "";
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
