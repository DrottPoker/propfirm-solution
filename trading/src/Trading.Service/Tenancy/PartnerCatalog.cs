using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Trading.Service.Tenancy;

/// <summary>The partners that may create firms, from the configuration. Validated at startup.</summary>
internal sealed partial class PartnerCatalog
{
    private readonly List<Partner> _partners;

    public PartnerCatalog(IEnumerable<PartnerOptions> partners)
    {
        _partners = partners.Select(Validate).ToList();
        Require(_partners.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() == _partners.Count, "Partner ids must be unique.");
    }

    public IReadOnlyList<Partner> All => _partners;

    /// <summary>The partner the key belongs to. Compared in constant time.</summary>
    public Partner? ByApiKey(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return _partners.Find(p => CryptographicOperations.FixedTimeEquals(hash, p.ApiKeyHash));
    }

    private static Partner Validate(PartnerOptions options)
    {
        Require(options.Id.Trim().Length > 0 && options.Name.Trim().Length > 0, "A partner needs an id and a name.");
        Require(Sha256Hex().IsMatch(options.ApiKeySha256), $"Partner {options.Id} needs ApiKeySha256 as 64 lowercase hex characters.");
        return new Partner(options.Id, options.Name.Trim(), Convert.FromHexString(options.ApiKeySha256));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Invalid partner configuration: {message}");
        }
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Hex();
}

internal sealed record Partner(string Id, string Name, byte[] ApiKeyHash);

/// <summary>Partner requests carry the partner's API key, and reach only the firms the partner created.</summary>
internal sealed class PartnerApiKeyFilter(PartnerCatalog partners, TenantCatalog tenants, PartnerActivity activity) : IEndpointFilter
{
    private static readonly object PartnerKey = new();

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (partners.ByApiKey(http.Request.Headers[AdminApiKeyFilter.HeaderName]) is not { } partner)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: $"A valid {AdminApiKeyFilter.HeaderName} header is required.");
        }

        await tenants.Ready.WaitAsync(http.RequestAborted);
        activity.Called(partner.Id);
        http.Items[PartnerKey] = partner;
        return await next(context);
    }

    public static Partner PartnerOf(HttpContext context) =>
        context.Items[PartnerKey] as Partner ?? throw new InvalidOperationException("The partner API key filter did not run.");
}

/// <summary>When each partner and each firm's system last called, for the staff panel (ADR 0057). Kept in memory.</summary>
internal sealed class PartnerActivity(TimeProvider time)
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> _calls = new(StringComparer.Ordinal);

    public void Called(string partnerId) => _calls[partnerId] = time.GetUtcNow();

    /// <summary>When the partner last called since the service started, or null.</summary>
    public DateTimeOffset? LastCall(string partnerId) => _calls.TryGetValue(partnerId, out var at) ? at : null;
}
