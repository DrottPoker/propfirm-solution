using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Trading.Engine;

namespace Trading.Service.Tenancy;

/// <summary>Looks up firms by host, admin API key and trading group. Validates the configuration at startup.</summary>
internal sealed partial class TenantCatalog
{
    /// <summary>Colors a firm may override in its terminal.</summary>
    public static readonly IReadOnlySet<string> ThemeColors = new HashSet<string>(StringComparer.Ordinal)
    {
        "background", "panel", "border", "foreground", "muted", "accent", "buy", "sell", "profit", "loss", "warning",
    };

    private readonly List<Tenant> _tenants;

    public TenantCatalog(IEnumerable<TenantOptions> tenants, EngineConfiguration engine)
    {
        _tenants = tenants.Select(t => Validate(t, engine)).ToList();
        Require(_tenants.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() == _tenants.Count, "Tenant ids must be unique.");
        Require(_tenants.SelectMany(t => t.Hosts).Distinct(StringComparer.OrdinalIgnoreCase).Count() == _tenants.Sum(t => t.Hosts.Count), "A host can belong to one tenant only.");
        Require(_tenants.SelectMany(t => t.Groups).Distinct(StringComparer.Ordinal).Count() == _tenants.Sum(t => t.Groups.Count), "A group can belong to one tenant only.");
    }

    public IReadOnlyList<Tenant> All => _tenants;

    public Tenant? ByHost(string? host) =>
        host is null ? null : _tenants.Find(t => t.Hosts.Contains(host, StringComparer.OrdinalIgnoreCase));

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
        Require(options.Id.Length > 0, "Every tenant needs an id.");
        Require(options.Hosts.Count > 0, $"Tenant {options.Id} needs at least one host.");
        Require(options.Groups.All(g => engine.Groups.Any(eg => eg.Id == g)), $"Tenant {options.Id} has a group that is not configured.");
        Require(Sha256Hex().IsMatch(options.AdminApiKeySha256), $"Tenant {options.Id} needs AdminApiKeySha256 as 64 lowercase hex characters.");

        var branding = options.Branding;
        Require(
            branding.LogoUrl.Length == 0 || (Uri.TryCreate(branding.LogoUrl, UriKind.Absolute, out var logo) && logo.Scheme == Uri.UriSchemeHttps),
            $"Tenant {options.Id}: the logo must be an absolute https address.");
        foreach (var (name, value) in branding.Colors)
        {
            // The colors end up in CSS, so only plain hex colors are allowed.
            Require(ThemeColors.Contains(name) && HexColor().IsMatch(value), $"Tenant {options.Id}: color {name} must be one of the theme colors and #rrggbb.");
        }

        return new Tenant(
            options.Id,
            options.Hosts.ToList(),
            options.Groups.ToList(),
            Convert.FromHexString(options.AdminApiKeySha256),
            new Branding(branding.DisplayName.Length > 0 ? branding.DisplayName : options.Id, branding.LogoUrl.Length > 0 ? branding.LogoUrl : null, branding.Colors.ToDictionary()));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Invalid tenant configuration: {message}");
        }
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Hex();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}

internal sealed record Tenant(string Id, IReadOnlyList<string> Hosts, IReadOnlyList<string> Groups, byte[] AdminApiKeyHash, Branding Branding);

/// <summary>What the terminal needs to look like the firm's own.</summary>
public sealed record Branding(string DisplayName, string? LogoUrl, IReadOnlyDictionary<string, string> Colors);
