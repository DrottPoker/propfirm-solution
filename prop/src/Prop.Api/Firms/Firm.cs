using System.Text.RegularExpressions;

using Prop.Api.Payments;

namespace Prop.Api.Firms;

/// <summary>Where a firm is on its way from sign-up to live (ADR 0017).</summary>
public enum FirmStatus
{
    /// <summary>The firm exists, but its server on the trading platform is still being created.</summary>
    Provisioning,

    /// <summary>Everything works, with a limit on open challenge accounts, until the firm goes live.</summary>
    Sandbox,

    /// <summary>No limits. Configured firms are live.</summary>
    Live,
}

/// <summary>
/// A firm on the prop platform. <paramref name="ApiKeyHash"/> is empty until the firm makes a key for its own
/// systems, and <paramref name="Trading"/> until its server on the trading platform exists.
/// <paramref name="Suspension"/> is set while we have suspended the firm. <paramref name="EmailSettings"/> are the
/// notification emails the firm turned on or off by kind; a kind that is missing is on. <paramref name="AccountCurrency"/>
/// is the currency of the accounts the firm chose, which its server on the trading platform is created with.
/// <paramref name="SupportEmail"/> is where replies to the emails to its traders go.
/// </summary>
internal sealed record Firm(
    string Id,
    string Name,
    FirmStatus Status,
    byte[]? ApiKeyHash,
    FirmTrading? Trading,
    FirmWebhook? Webhook,
    FirmPortal Portal,
    FirmPayments Payments,
    FirmSuspension? Suspension,
    IReadOnlyDictionary<string, bool>? EmailSettings = null,
    string AccountCurrency = "USD",
    string? SupportEmail = null);

/// <summary>We suspended the firm, for a reason its administrators see (ADR 0021).</summary>
internal sealed record FirmSuspension(DateTimeOffset At, string Reason);

/// <summary>The firm's server on the trading platform, the key the prop platform uses there, and the group new accounts open in.</summary>
internal sealed record FirmTrading(string Server, string ApiKey, string Group, string Currency);

internal sealed record FirmWebhook(Uri Url, string Secret);

internal sealed record FirmPortal(Uri Url, IReadOnlyList<string> Hosts, Branding Branding);

/// <summary>What the portal needs to look like the firm's own.</summary>
public sealed record Branding(string Name, string? LogoUrl, IReadOnlyDictionary<string, string> Colors);

/// <summary>The rules for what a firm may be called and how it may look, shared by configured firms and those that sign up.</summary>
internal static partial class FirmRules
{
    public const int MaxNameLength = 100;

    /// <summary>The portal's colors a firm may override.</summary>
    public static readonly IReadOnlySet<string> ThemeColors = new HashSet<string>(StringComparer.Ordinal)
    {
        "background", "panel", "border", "foreground", "muted", "accent", "accent-foreground", "profit", "loss", "warning",
    };

    /// <summary>Short names that would be confused with our own addresses.</summary>
    public static readonly IReadOnlySet<string> ReservedIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "account", "accounts", "admin", "api", "app", "assets", "auth", "billing", "blog", "cdn", "dashboard", "demo", "dev",
        "docs", "email", "help", "login", "mail", "ns1", "ns2", "ops", "platform", "portal", "secure", "signup", "smtp", "staging",
        "static", "status", "support", "terminal", "test", "trade", "trading", "www",
    };

    /// <summary>2 to 40 lowercase letters, digits and dashes, not first or last. Short enough to be part of trading account ids.</summary>
    public static bool IsValidId(string? id) => id is not null && FirmId().IsMatch(id);

    public static bool IsValidName(string? name) => name?.Trim().Length is > 0 and <= MaxNameLength;

    /// <summary>An absolute https address, or empty for none.</summary>
    public static bool IsValidLogoUrl(string? logoUrl) =>
        string.IsNullOrEmpty(logoUrl) || (Uri.TryCreate(logoUrl, UriKind.Absolute, out var logo) && logo.Scheme == Uri.UriSchemeHttps);

    /// <summary>The colors end up in CSS, so only the theme's colors as plain hex colors are allowed.</summary>
    public static string? ColorProblem(IReadOnlyDictionary<string, string> colors) =>
        colors.FirstOrDefault(c => !ThemeColors.Contains(c.Key) || !HexColor().IsMatch(c.Value)) is { Key: not null } bad
            ? $"Color {bad.Key} must be one of the portal's colors, as #rrggbb."
            : null;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,38}[a-z0-9]$")]
    private static partial Regex FirmId();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}
