using System.Globalization;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

using Trading.Service.Identity;
using Trading.Service.Tenancy;

namespace Trading.Service.Api;

/// <summary>
/// How much each caller may ask of the service a minute (ADR 0059): a trader from all their devices together, a firm's
/// system with its key, a partner, and anyone looking up a server by name. Each has an allowance of its own, so a busy
/// or broken client slows only itself. Up to a quarter of a minute's allowance can be used at once, and the rest comes
/// back evenly over the minute. 0 turns an allowance off.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    /// <summary>
    /// Requests a trader may make to the trading API, the settings and the realtime hub. A terminal asks for about sixty
    /// things as it opens, so a few reloads in a row stay well within it.
    /// </summary>
    public int TraderPerMinute { get; init; } = 1_200;

    /// <summary>Requests a firm's system may make to the admin API with its key.</summary>
    public int FirmPerMinute { get; init; } = 1_200;

    /// <summary>Requests a partner may make to the partner API.</summary>
    public int PartnerPerMinute { get; init; } = 600;

    /// <summary>Requests from one address to look up servers, which needs no login.</summary>
    public int PublicPerMinute { get; init; } = 60;
}

/// <summary>The policies for <see cref="RateLimitOptions"/>, and the answer a caller gets once it has used its allowance.</summary>
internal static class RateLimits
{
    public const string Trader = "trader";
    public const string Firm = "firm";
    public const string Partner = "partner";
    public const string Public = "public";

    /// <summary>The reason in a refusal's problem response, like an engine's reject reason.</summary>
    public const string TooManyRequests = "TooManyRequests";

    public static void AddApiPolicies(this RateLimiterOptions options)
    {
        options.AddPolicy(Trader, context =>
            Allowance(CurrentUser.IdOf(context.User) is { } userId ? $"user:{userId}" : AddressOf(context), Limits(context).TraderPerMinute));
        options.AddPolicy(Firm, context =>
        {
            var tenant = context.RequestServices.GetRequiredService<TenantCatalog>().ByAdminApiKey(context.Request.Headers[AdminApiKeyFilter.HeaderName]);
            return Allowance(tenant is not null ? $"firm:{tenant.Id}" : AddressOf(context), Limits(context).FirmPerMinute);
        });
        options.AddPolicy(Partner, context =>
        {
            var partner = context.RequestServices.GetRequiredService<PartnerCatalog>().ByApiKey(context.Request.Headers[AdminApiKeyFilter.HeaderName]);
            return Allowance(partner is not null ? $"partner:{partner.Id}" : AddressOf(context), Limits(context).PartnerPerMinute);
        });
        options.AddPolicy(Public, context => Allowance(AddressOf(context), Limits(context).PublicPerMinute));
        options.OnRejected = RejectAsync;
    }

    /// <summary>A refusal says when to try again and why, in the same shape as a rejected command.</summary>
    public static async ValueTask RejectAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        await TypedResults.Problem(
            title: "Too many requests. Wait a moment and try again.",
            statusCode: StatusCodes.Status429TooManyRequests,
            extensions: new Dictionary<string, object?> { [CommandResults.ReasonExtension] = TooManyRequests })
            .ExecuteAsync(context.HttpContext);
    }

    private static RateLimitPartition<string> Allowance(string key, int perMinute) =>
        perMinute <= 0
            ? RateLimitPartition.GetNoLimiter(key)
            : RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = Math.Max(1, perMinute / 4),
                TokensPerPeriod = Math.Max(1, (perMinute + 59) / 60),
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            });

    private static RateLimitOptions Limits(HttpContext context) => context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;

    private static string AddressOf(HttpContext context) => $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
}
