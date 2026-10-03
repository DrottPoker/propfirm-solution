using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Prop.Api.Configuration;

namespace Prop.Api.Firms;

/// <summary>The firms on the prop platform, validated at startup.</summary>
internal sealed partial class FirmCatalog
{
    /// <summary>The portal's colors a firm may override.</summary>
    public static readonly IReadOnlySet<string> ThemeColors = new HashSet<string>(StringComparer.Ordinal)
    {
        "background", "panel", "border", "foreground", "muted", "accent", "profit", "loss", "warning",
    };

    private readonly List<Firm> _firms;

    public FirmCatalog(IEnumerable<FirmOptions> firms)
    {
        _firms = firms.Select(Validate).ToList();
        Require(_firms.Select(f => f.Id).Distinct(StringComparer.Ordinal).Count() == _firms.Count, "Firm ids must be unique.");
        Require(
            _firms.SelectMany(f => f.Portal.Hosts).Distinct(StringComparer.OrdinalIgnoreCase).Count() == _firms.Sum(f => f.Portal.Hosts.Count),
            "A portal host can belong to one firm only.");
    }

    public IReadOnlyList<Firm> All => _firms;

    public Firm? ById(string firmId) => _firms.Find(f => f.Id == firmId);

    /// <summary>The firm whose portal is reached on the host name, without port.</summary>
    public Firm? ByHost(string? host) =>
        host is null ? null : _firms.Find(f => f.Portal.Hosts.Contains(host, StringComparer.OrdinalIgnoreCase));

    /// <summary>The firm the key belongs to. Compared in constant time.</summary>
    public Firm? ByApiKey(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return _firms.Find(f => CryptographicOperations.FixedTimeEquals(hash, f.ApiKeyHash));
    }

    public static string HashApiKey(string apiKey) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));

    private static Firm Validate(FirmOptions options)
    {
        Require(FirmId().IsMatch(options.Id), $"Firm id '{options.Id}' must be 2 to 40 lowercase letters, digits or dashes.");
        Require(options.Name.Trim().Length > 0, $"Firm {options.Id} needs a name.");
        Require(Sha256Hex().IsMatch(options.ApiKeySha256), $"Firm {options.Id} needs ApiKeySha256 as 64 lowercase hex characters.");
        Require(
            options.Trading.Server.Length > 0 && options.Trading.ApiKey.Length > 0 && options.Trading.Group.Length > 0,
            $"Firm {options.Id} needs a trading server, API key and group.");
        Require(
            options.Webhook.Url is null || (options.Webhook.Url.IsAbsoluteUri && options.Webhook.Secret.Length >= 32),
            $"Firm {options.Id}: a webhook needs an absolute address and a secret of at least 32 characters.");

        var portal = options.Portal;
        Require(
            portal.Url is { IsAbsoluteUri: true } url && url.AbsolutePath.EndsWith('/') && portal.Hosts.Count > 0,
            $"Firm {options.Id} needs a portal address ending with / and at least one portal host.");
        Require(
            portal.LogoUrl.Length == 0 || (Uri.TryCreate(portal.LogoUrl, UriKind.Absolute, out var logo) && logo.Scheme == Uri.UriSchemeHttps),
            $"Firm {options.Id}: the logo must be an absolute https address.");
        foreach (var (name, value) in portal.Colors)
        {
            // The colors end up in CSS, so only plain hex colors are allowed.
            Require(ThemeColors.Contains(name) && HexColor().IsMatch(value), $"Firm {options.Id}: color {name} must be one of the theme colors and #rrggbb.");
        }

        return new Firm(
            options.Id,
            options.Name.Trim(),
            Convert.FromHexString(options.ApiKeySha256),
            new FirmTrading(options.Trading.Server, options.Trading.ApiKey, options.Trading.Group),
            options.Webhook.Url is { } webhook ? new FirmWebhook(webhook, options.Webhook.Secret) : null,
            new FirmPortal(
                portal.Url!,
                [.. portal.Hosts],
                new Branding(options.Name.Trim(), portal.LogoUrl.Length > 0 ? portal.LogoUrl : null, portal.Colors.ToDictionary())));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Invalid firm configuration: {message}");
        }
    }

    // Short enough to be part of trading account ids.
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,39}$")]
    private static partial Regex FirmId();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Hex();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}

internal sealed record Firm(string Id, string Name, byte[] ApiKeyHash, FirmTrading Trading, FirmWebhook? Webhook, FirmPortal Portal);

/// <summary>The firm's server on the trading platform and the key the prop platform uses there.</summary>
internal sealed record FirmTrading(string Server, string ApiKey, string Group);

internal sealed record FirmWebhook(Uri Url, string Secret);

internal sealed record FirmPortal(Uri Url, IReadOnlyList<string> Hosts, Branding Branding);

/// <summary>What the portal needs to look like the firm's own.</summary>
public sealed record Branding(string Name, string? LogoUrl, IReadOnlyDictionary<string, string> Colors);
