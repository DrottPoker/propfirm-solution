namespace Trading.Service.Tenancy;

/// <summary>A firm that uses the platform. Configured for now; managed through an API when firms sign up themselves.</summary>
public sealed class TenantOptions
{
    public const string SectionName = "Tenants";

    public string Id { get; init; } = "";

    /// <summary>Host names that belong to the firm, without port. The terminal and API are reached through them.</summary>
    public IReadOnlyList<string> Hosts { get; init; } = [];

    /// <summary>Trading groups the firm's accounts may be in.</summary>
    public IReadOnlyList<string> Groups { get; init; } = [];

    /// <summary>SHA-256 of the firm's admin API key, as lowercase hex. The key itself is never stored.</summary>
    public string AdminApiKeySha256 { get; init; } = "";

    public BrandingOptions Branding { get; init; } = new();
}

/// <summary>White label settings for the firm's terminal. Colors are #rrggbb.</summary>
public sealed class BrandingOptions
{
    public string DisplayName { get; init; } = "";

    /// <summary>Absolute https address of the logo, or empty for none.</summary>
    public string LogoUrl { get; init; } = "";

    public IReadOnlyDictionary<string, string> Colors { get; init; } = new Dictionary<string, string>();
}
