namespace Trading.Service.Tenancy;

/// <summary>
/// A firm that uses the platform. Traders choose the firm's server when they log in, as in MetaTrader.
/// Configured for now; managed through an API when firms sign up themselves.
/// </summary>
public sealed class TenantOptions
{
    public const string SectionName = "Tenants";

    /// <summary>The server name traders log in with, for example nordic-prop. Lowercase letters, digits and dashes.</summary>
    public string Id { get; init; } = "";

    /// <summary>The firm's name, shown on the login page and next to its accounts.</summary>
    public string Name { get; init; } = "";

    /// <summary>Trading groups the firm's accounts may be in.</summary>
    public IReadOnlyList<string> Groups { get; init; } = [];

    /// <summary>SHA-256 of the firm's admin API key, as lowercase hex. The key itself is never stored.</summary>
    public string AdminApiKeySha256 { get; init; } = "";
}
