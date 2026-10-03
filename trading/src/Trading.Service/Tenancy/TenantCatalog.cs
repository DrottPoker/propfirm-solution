using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Trading.Engine;

namespace Trading.Service.Tenancy;

/// <summary>Looks up firms by server, admin API key and trading group. Validates the configuration at startup.</summary>
internal sealed partial class TenantCatalog
{
    private readonly List<Tenant> _tenants;

    public TenantCatalog(IEnumerable<TenantOptions> tenants, EngineConfiguration engine)
    {
        _tenants = tenants.Select(t => Validate(t, engine)).ToList();
        Require(_tenants.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() == _tenants.Count, "Tenant ids must be unique.");
        Require(_tenants.SelectMany(t => t.Groups).Distinct(StringComparer.Ordinal).Count() == _tenants.Sum(t => t.Groups.Count), "A group can belong to one tenant only.");
    }

    public IReadOnlyList<Tenant> All => _tenants;

    public Tenant? ById(string tenantId) => _tenants.Find(t => t.Id == tenantId);

    public Tenant? ByGroup(string groupId) => _tenants.Find(t => t.Groups.Contains(groupId, StringComparer.Ordinal));

    /// <summary>The firm the key belongs to. Compared in constant time.</summary>
    public Tenant? ByAdminApiKey(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return _tenants.Find(t => CryptographicOperations.FixedTimeEquals(hash, t.AdminApiKeyHash));
    }

    public static string HashApiKey(string apiKey) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));

    private static Tenant Validate(TenantOptions options, EngineConfiguration engine)
    {
        // The id is the server name, typed by traders and used in links, so it is kept simple.
        Require(ServerName().IsMatch(options.Id), $"Tenant id '{options.Id}' must be 2 to 63 lowercase letters, digits or dashes.");
        Require(options.Name.Trim().Length > 0, $"Tenant {options.Id} needs a name.");
        Require(options.Groups.All(g => engine.Groups.Any(eg => eg.Id == g)), $"Tenant {options.Id} has a group that is not configured.");
        Require(Sha256Hex().IsMatch(options.AdminApiKeySha256), $"Tenant {options.Id} needs AdminApiKeySha256 as 64 lowercase hex characters.");

        return new Tenant(options.Id, options.Name.Trim(), options.Groups.ToList(), Convert.FromHexString(options.AdminApiKeySha256));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Invalid tenant configuration: {message}");
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,62}$")]
    private static partial Regex ServerName();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Hex();
}

internal sealed record Tenant(string Id, string Name, IReadOnlyList<string> Groups, byte[] AdminApiKeyHash);
