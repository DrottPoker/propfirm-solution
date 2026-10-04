using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Trading.Service.Tenancy;

/// <summary>
/// The firms on the platform, kept in memory and looked up by server, admin API key and trading group. Loaded
/// from the tenant store at startup and kept up to date as partners create firms. The service runs as one
/// instance, so nothing else changes the store.
/// </summary>
internal sealed partial class TenantCatalog
{
    private readonly Lock _lock = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IReadOnlyList<Tenant> _tenants = [];

    /// <summary>Completes when the firms are loaded. Lookups before that find nothing.</summary>
    public Task Ready => _ready.Task;

    public IReadOnlyList<Tenant> All => Volatile.Read(ref _tenants);

    public Tenant? ById(string tenantId) => All.FirstOrDefault(t => t.Id == tenantId);

    public Tenant? ByGroup(string groupId) => All.FirstOrDefault(t => t.Groups.Contains(groupId, StringComparer.Ordinal));

    /// <summary>The firm the key belongs to. Compared in constant time.</summary>
    public Tenant? ByAdminApiKey(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return All.FirstOrDefault(t => CryptographicOperations.FixedTimeEquals(hash, t.AdminApiKeyHash));
    }

    public void Load(IReadOnlyList<Tenant> tenants)
    {
        lock (_lock)
        {
            Volatile.Write(ref _tenants, tenants);
        }

        _ready.TrySetResult();
    }

    public void LoadFailed(Exception exception) => _ready.TrySetException(exception);

    /// <summary>Adds the firm, or replaces the one with the same id.</summary>
    public void Put(Tenant tenant)
    {
        lock (_lock)
        {
            Volatile.Write(ref _tenants, [.. _tenants.Where(t => t.Id != tenant.Id), tenant]);
        }
    }

    public static string HashApiKey(string apiKey) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));

    /// <summary>Traders are sent to the login address, so it must be a web address.</summary>
    public static bool IsValidLoginUrl(Uri url) => url.IsAbsoluteUri && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp);

    /// <summary>The id is the server name, typed by traders and used in links, so it is kept simple.</summary>
    public static bool IsValidId(string? id) => id is not null && ServerName().IsMatch(id);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,62}$")]
    private static partial Regex ServerName();
}
