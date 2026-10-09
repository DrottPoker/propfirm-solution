namespace Trading.Service.Tenancy;

/// <summary>Where the firms on the platform and their groups are kept.</summary>
public interface ITenantStore
{
    Task<IReadOnlyList<Tenant>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saves a configured firm: creates it, or gives it the configured name, key and groups. Throws if a partner
    /// created a firm with the same id, or if one of the groups belongs to another firm.
    /// </summary>
    Task SaveConfiguredAsync(Tenant tenant, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Creates a firm for a partner or our staff. False if the id or one of the groups is taken.</summary>
    Task<bool> CreateAsync(Tenant tenant, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Replaces the hash of the firm's admin API key.</summary>
    Task SetAdminApiKeyAsync(string tenantId, byte[] adminApiKeyHash, CancellationToken cancellationToken);

    /// <summary>Whether the firm's server is listed, and where its traders log in.</summary>
    Task SetListingAsync(string tenantId, bool listed, Uri? loginUrl, Uri? logoUrl, CancellationToken cancellationToken);

    /// <summary>How the firm's terminal works for its traders (ADR 0058).</summary>
    Task SetTerminalProfileAsync(string tenantId, TerminalProfile profile, CancellationToken cancellationToken);
}

/// <summary>
/// A firm on the platform. <paramref name="Id"/> is the server traders log in to. <paramref name="PartnerId"/> is
/// the partner that created it, or null for a configured firm and one our staff made, whose email is then
/// <paramref name="CreatedBy"/> (ADR 0057). Only listed servers can be found by name on the terminal's login (ADR
/// 0058). <paramref name="LoginUrl"/> is where the firm's traders log in when they have no password for the terminal,
/// for example the firm's portal. <paramref name="Terminal"/> is how its terminal works, null for its kind's default.
/// </summary>
public sealed record Tenant(
    string Id,
    string Name,
    IReadOnlyList<string> Groups,
    byte[] AdminApiKeyHash,
    string? PartnerId,
    bool Listed,
    Uri? LoginUrl = null,
    Uri? LogoUrl = null,
    DateTimeOffset? CreatedAt = null,
    string? CreatedBy = null,
    TerminalProfile? Terminal = null)
{
    /// <summary>A firm from the configuration, saved again at every start.</summary>
    public bool IsConfigured => PartnerId is null && CreatedBy is null;

    /// <summary>How the firm's terminal works: as the firm set it, or the default.</summary>
    public TerminalProfile Profile => Terminal ?? TerminalProfile.Standard;
}
