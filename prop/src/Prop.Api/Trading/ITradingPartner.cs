namespace Prop.Api.Trading;

/// <summary>
/// The trading platform's partner API, which creates the servers of firms that sign up (ADR 0016). Acts as the
/// prop platform, not as one firm.
/// </summary>
internal interface ITradingPartner
{
    /// <summary>Whether a server with the name can be created now.</summary>
    Task<bool> IsServerAvailableAsync(string server, CancellationToken cancellationToken);

    /// <summary>Creates the firm's server. Null if the name is taken, for example by an earlier attempt whose answer was lost.</summary>
    Task<PartnerTenant?> CreateTenantAsync(string server, string name, string currency, CancellationToken cancellationToken);

    /// <summary>A server the prop platform created. Null if there is none, or if someone else created it.</summary>
    Task<PartnerTenant?> GetTenantAsync(string server, CancellationToken cancellationToken);

    /// <summary>A new key to the server's admin API. The old one stops working.</summary>
    Task<string> ReplaceAdminKeyAsync(string server, CancellationToken cancellationToken);

    /// <summary>Lists the server for traders or takes it off the list, and sets where its traders log in.</summary>
    Task SetListingAsync(string server, bool listed, Uri loginUrl, Uri? logoUrl, CancellationToken cancellationToken);

    /// <summary>When the platform's last price came, per symbol, and whether each market is open (ADR 0053).</summary>
    Task<PriceFeedStatus> GetPriceFeedAsync(CancellationToken cancellationToken);
}

/// <summary>A firm's server and its groups. The admin API key is only known when the server was just created.</summary>
internal sealed record PartnerTenant(string Server, IReadOnlyList<PartnerGroup> Groups, string? AdminApiKey);

internal sealed record PartnerGroup(string Id, string Currency);
