namespace Trading.Service.Tenancy;

/// <summary>
/// A configured firm, saved to the tenant store at every start. For development and tests. Firms that sign up
/// are created by a partner instead (ADR 0016). Traders choose the firm's server when they log in, as in MetaTrader.
/// </summary>
public sealed class TenantOptions
{
    public const string SectionName = "Tenants";

    /// <summary>The server name traders log in with, for example nordic-prop. Lowercase letters, digits and dashes.</summary>
    public string Id { get; init; } = "";

    /// <summary>The firm's name, shown on the login page and next to its accounts.</summary>
    public string Name { get; init; } = "";

    /// <summary>Trading groups the firm's accounts may be in. Configured groups only.</summary>
    public IReadOnlyList<string> Groups { get; init; } = [];

    /// <summary>SHA-256 of the firm's admin API key, as lowercase hex. The key itself is never stored.</summary>
    public string AdminApiKeySha256 { get; init; } = "";

    /// <summary>Where the firm's traders log in when they have no password for the terminal, for example its portal. Empty for none.</summary>
    public Uri? LoginUrl { get; init; }
}

/// <summary>A system that may create firms on the platform, such as our prop platform.</summary>
public sealed class PartnerOptions
{
    public const string SectionName = "Partners";

    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    /// <summary>SHA-256 of the partner's API key, as lowercase hex. The key itself is never stored.</summary>
    public string ApiKeySha256 { get; init; } = "";
}

/// <summary>What a firm created by a partner starts with.</summary>
public sealed class TenancyOptions
{
    public const string SectionName = "Tenancy";

    /// <summary>Configured groups that a new firm gets a copy of, named {server}-{group}.</summary>
    public IReadOnlyList<string> NewTenantGroups { get; init; } = [];

    /// <summary>
    /// The account currencies a new firm may choose instead of the templates' own. Each needs a pair with USD among the
    /// instruments, since the engine converts through USD.
    /// </summary>
    public IReadOnlyList<string> Currencies { get; init; } = [];
}
