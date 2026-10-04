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

    /// <summary>Creates a firm for a partner. False if the id or one of the groups is taken.</summary>
    Task<bool> CreateAsync(Tenant tenant, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Replaces the hash of the firm's admin API key.</summary>
    Task SetAdminApiKeyAsync(string tenantId, byte[] adminApiKeyHash, CancellationToken cancellationToken);

    /// <summary>Whether the firm's server is listed, and where its traders log in.</summary>
    Task SetListingAsync(string tenantId, bool listed, Uri? loginUrl, Uri? logoUrl, CancellationToken cancellationToken);
}

/// <summary>
/// A firm on the platform. <paramref name="Id"/> is the server traders log in to. <paramref name="PartnerId"/> is
/// the partner that created it, or null for a configured firm. Only listed servers are shown to traders.
/// <paramref name="LoginUrl"/> is where the firm's traders log in when they have no password for the terminal,
/// for example the firm's portal.
/// </summary>
public sealed record Tenant(string Id, string Name, IReadOnlyList<string> Groups, byte[] AdminApiKeyHash, string? PartnerId, bool Listed, Uri? LoginUrl = null, Uri? LogoUrl = null);
