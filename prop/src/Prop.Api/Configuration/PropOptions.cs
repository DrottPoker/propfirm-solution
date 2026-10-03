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

    /// <summary>The prop platform's key to the partner API, which creates the servers of firms that sign up. A secret.</summary>
    public string PartnerApiKey { get; init; } = "";
}

/// <summary>Our own platform, where firms sign up, and where the portals of new firms are reached.</summary>
public sealed class PlatformOptions
{
    public const string SectionName = "Platform";

    /// <summary>A working name, shown when firms sign up and in emails, until the product is named.</summary>
    public string Name { get; init; } = "Prop platform";

    /// <summary>Where firms sign up, ending with /, for example https://app.example.com/.</summary>
    public Uri? Url { get; init; }

    /// <summary>The portal address of a new firm, with {firm} for its short name, for example https://{firm}.example.com/.</summary>
    public string FirmPortalUrl { get; init; } = "";

    /// <summary>The portal address of the firm with the short name.</summary>
    public Uri PortalUrlOf(string firmId) => new(FirmPortalUrl.Replace("{firm}", firmId, StringComparison.Ordinal));
}

/// <summary>How firms sign up.</summary>
public sealed class SignupOptions
{
    public const string SectionName = "Signup";

    /// <summary>Whether the email address must be confirmed before the firm is created. Development turns it off.</summary>
    public bool RequireEmailVerification { get; init; } = true;

    /// <summary>The version of the terms and the data processing agreement that firms accept, recorded with each firm.</summary>
    public string TermsVersion { get; init; } = "";

    public Uri? TermsUrl { get; init; }

    public Uri? DpaUrl { get; init; }

    /// <summary>Short names firms may not choose, besides the built-in ones.</summary>
    public IReadOnlyList<string> ReservedFirmIds { get; init; } = [];
}

/// <summary>What a firm in the sandbox may do before it goes live.</summary>
public sealed class SandboxOptions
{
    public const string SectionName = "Sandbox";

    /// <summary>The most challenge accounts a firm in the sandbox may have open at once.</summary>
    public int MaxOpenAccounts { get; init; } = 10;
}

/// <summary>Email from the platform, such as confirmations and invitations for administrators.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string From { get; init; } = "";

    public string FromName { get; init; } = "";

    public SmtpOptions Smtp { get; init; } = new();
}

public sealed class SmtpOptions
{
    public string Host { get; init; } = "";

    public int Port { get; init; } = 587;

    public string UserName { get; init; } = "";

    /// <summary>A secret: keep it out of files outside development.</summary>
    public string Password { get; init; } = "";

    /// <summary>None, StartTls or SslOnConnect. StartTls everywhere but a local test server.</summary>
    public string Security { get; init; } = "StartTls";
}

/// <summary>The key that encrypts the firms' secrets in the database. Never kept in the database itself.</summary>
public sealed class SecretsOptions
{
    public const string SectionName = "Secrets";

    /// <summary>32 random bytes in base64. A secret: keep it out of files outside development, and never change it without encrypting the secrets again.</summary>
    public string Key { get; init; } = "";
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

/// <summary>
/// A configured firm, saved to the database at every start and live from the beginning. For development and
/// tests. Other firms sign up themselves (ADR 0017).
/// </summary>
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

    /// <summary>Challenges created at startup, or replaced so they follow their template.</summary>
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

    /// <summary>The group's account currency. Challenges must be in it.</summary>
    public string Currency { get; init; } = "USD";
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

    /// <summary>Tiny targets and no minimum trading days, to try the whole flow quickly. For development only.</summary>
    public const string QuickTestTemplate = "QuickTest";

    public string Template { get; init; } = TwoStepTemplate;

    public string Id { get; init; } = "";

    public decimal InitialBalance { get; init; }

    public string Currency { get; init; } = "USD";
}
