using Microsoft.AspNetCore.Http.HttpResults;

using Trading.Engine;
using Trading.Service.Engine;
using Trading.Service.Feeds;
using Trading.Service.Reports;
using Trading.Service.Tenancy;

namespace Trading.Service.Api;

/// <summary>
/// For partners that create firms on the platform, such as our prop platform when a firm signs up (ADR 0016).
/// Every request carries the partner's API key and reaches only the firms the partner created. Versioned, like
/// the admin API.
/// </summary>
internal static class PartnerEndpoints
{
    public static IEndpointRouteBuilder MapPartnerApi(this IEndpointRouteBuilder app)
    {
        var partner = app.MapGroup("/api/partner/v1").WithTags("Partner").AddEndpointFilter<PartnerApiKeyFilter>();
        partner.MapGet("/server-names/{id}", GetServerName);
        partner.MapPost("/tenants", CreateTenantAsync);
        partner.MapGet("/tenants/{id}", GetTenantAsync);
        partner.MapPatch("/tenants/{id}", UpdateTenantAsync);
        partner.MapPost("/tenants/{id}/admin-key", ReplaceAdminKeyAsync);
        partner.MapGet("/price-feed", GetPriceFeedAsync);
        return app;
    }

    /// <summary>
    /// How the platform's price feed is doing (ADR 0053): when the last price came, for every symbol, and whether its
    /// market is open, so a partner can tell an outage from a closed market.
    /// </summary>
    private static async Task<Ok<PriceFeedStatus>> GetPriceFeedAsync(
        EngineHost engine,
        EngineConfiguration configuration,
        IPriceFeed feed,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var latest = (await engine.QueryAsync(e => e.GetLatestQuotes(), cancellationToken)).ToDictionary(q => q.Symbol, q => q.Timestamp, StringComparer.Ordinal);
        var symbols = configuration.Instruments
            .OrderBy(i => i.Symbol, StringComparer.Ordinal)
            .Select(i => new SymbolFeedStatus(i.Symbol, latest.TryGetValue(i.Symbol, out var at) ? at : null, i.TradingHours?.IsOpen(now) ?? true))
            .ToList();
        return TypedResults.Ok(new PriceFeedStatus(feed.Name, now, latest.Count == 0 ? null : latest.Values.Max(), symbols));
    }

    /// <summary>Whether a firm can be created with the server name now.</summary>
    private static Ok<ServerNameResponse> GetServerName(string id, TenantProvisioner provisioner) =>
        TypedResults.Ok(new ServerNameResponse(id, provisioner.IsAvailable(id)));

    /// <summary>Creates a firm with its own copy of the template groups. The admin API key is shown only now.</summary>
    private static async Task<Results<Created<CreatedTenantResponse>, ProblemHttpResult>> CreateTenantAsync(
        CreateTenantRequest request,
        HttpContext context,
        TenantProvisioner provisioner,
        CancellationToken cancellationToken)
    {
        var result = await provisioner.CreateAsync(TenantMaker.Of(PartnerApiKeyFilter.PartnerOf(context)), request.Id, request.Name, request.Currency, cancellationToken);
        return result switch
        {
            ProvisioningResult.Created created => TypedResults.Created(
                $"/api/partner/v1/tenants/{created.Tenant.Id}",
                new CreatedTenantResponse(
                    created.Tenant.Id,
                    created.Tenant.Name,
                    created.Tenant.Listed,
                    created.Tenant.LoginUrl,
                    [.. created.Groups.Select(g => new TenantGroupResponse(g.Id, g.Currency))],
                    created.AdminApiKey)),
            ProvisioningResult.Taken => Problem(StatusCodes.Status409Conflict, "The server name is taken.", RejectReason.DuplicateId.ToString()),
            ProvisioningResult.Invalid invalid => Problem(StatusCodes.Status422UnprocessableEntity, invalid.Problem, RejectReason.InvalidId.ToString()),
            ProvisioningResult.InvalidCurrency invalid => Problem(StatusCodes.Status422UnprocessableEntity, invalid.Problem, InvalidCurrencyReason),
            _ => throw new InvalidOperationException($"Unknown result {result.GetType().Name}."),
        };
    }

    private static async Task<Results<Ok<TenantResponse>, ProblemHttpResult>> GetTenantAsync(
        string id,
        HttpContext context,
        TenantCatalog tenants,
        EngineHost engine,
        CancellationToken cancellationToken) =>
        PartnerTenant(context, tenants, id) is { } tenant
            ? TypedResults.Ok(await ToResponseAsync(tenant, engine, cancellationToken))
            : UnknownTenant();

    /// <summary>
    /// Lists the firm's server or takes it off the list, for example when the firm goes live, and sets where its
    /// traders log in and its logo. Fields that are left out stay as they are; an empty address removes it.
    /// </summary>
    private static async Task<Results<Ok<TenantResponse>, ProblemHttpResult>> UpdateTenantAsync(
        string id,
        UpdateTenantRequest request,
        HttpContext context,
        TenantCatalog tenants,
        TenantProvisioner provisioner,
        EngineHost engine,
        CancellationToken cancellationToken)
    {
        if (PartnerTenant(context, tenants, id) is not { } tenant)
        {
            return UnknownTenant();
        }

        var loginUrl = tenant.LoginUrl;
        if (request.LoginUrl is { } requested)
        {
            if (requested.Length == 0)
            {
                loginUrl = null;
            }
            else if (Uri.TryCreate(requested, UriKind.Absolute, out var parsed) && TenantCatalog.IsValidLoginUrl(parsed))
            {
                loginUrl = parsed;
            }
            else
            {
                return Problem(StatusCodes.Status422UnprocessableEntity, "The login address must be an absolute http or https address.");
            }
        }

        var logoUrl = tenant.LogoUrl;
        if (request.LogoUrl is { } requestedLogo)
        {
            if (requestedLogo.Length == 0)
            {
                logoUrl = null;
            }
            else if (Uri.TryCreate(requestedLogo, UriKind.Absolute, out var parsed) && TenantCatalog.IsValidLoginUrl(parsed))
            {
                logoUrl = parsed;
            }
            else
            {
                return Problem(StatusCodes.Status422UnprocessableEntity, "The logo address must be an absolute http or https address.");
            }
        }

        var changed = await provisioner.SetListingAsync(
            tenant, request.Listed ?? tenant.Listed, loginUrl, logoUrl, TenantMaker.Of(PartnerApiKeyFilter.PartnerOf(context)), cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(changed, engine, cancellationToken));
    }

    /// <summary>A new admin API key for the firm, for example when the partner lost the first one. The old key stops working.</summary>
    private static async Task<Results<Ok<AdminApiKeyResponse>, ProblemHttpResult>> ReplaceAdminKeyAsync(
        string id,
        HttpContext context,
        TenantCatalog tenants,
        TenantProvisioner provisioner,
        CancellationToken cancellationToken) =>
        PartnerTenant(context, tenants, id) is { } tenant
            ? TypedResults.Ok(new AdminApiKeyResponse(
                await provisioner.ReplaceAdminApiKeyAsync(tenant, TenantMaker.Of(PartnerApiKeyFilter.PartnerOf(context)), null, cancellationToken)))
            : UnknownTenant();

    // Firms of other partners, and configured firms, look like they do not exist.
    private static Tenant? PartnerTenant(HttpContext context, TenantCatalog tenants, string id) =>
        tenants.ById(id) is { } tenant && tenant.PartnerId == PartnerApiKeyFilter.PartnerOf(context).Id ? tenant : null;

    private static async Task<TenantResponse> ToResponseAsync(Tenant tenant, EngineHost engine, CancellationToken cancellationToken)
    {
        var groups = await engine.QueryAsync(e => tenant.Groups.Select(e.GetGroup).OfType<TradingGroup>().ToList(), cancellationToken);
        return new TenantResponse(tenant.Id, tenant.Name, tenant.Listed, tenant.LoginUrl, [.. groups.Select(g => new TenantGroupResponse(g.Id, g.Currency))], tenant.LogoUrl);
    }

    /// <summary>The reason for an account currency the platform does not offer to new firms.</summary>
    public const string InvalidCurrencyReason = "InvalidCurrency";

    private static ProblemHttpResult UnknownTenant() => Problem(StatusCodes.Status404NotFound, "The partner has no such firm.");

    private static ProblemHttpResult Problem(int statusCode, string title, string? reason = null) =>
        TypedResults.Problem(
            statusCode: statusCode,
            title: title,
            extensions: reason is null ? null : new Dictionary<string, object?> { [CommandResults.ReasonExtension] = reason });
}

/// <summary>
/// A firm with its server <paramref name="Id"/>, which traders log in to, and its name. <paramref name="Currency"/> is its
/// accounts' currency, one of <c>Tenancy:Currencies</c>, or the template groups' own when left out.
/// </summary>
public sealed record CreateTenantRequest(string? Id, string? Name, string? Currency = null);

/// <summary>A firm's trading group and its account currency.</summary>
public sealed record TenantGroupResponse(string Id, string Currency);

/// <summary>
/// A firm. Only <paramref name="Listed"/> servers are on the list traders choose from. <paramref name="LoginUrl"/> is
/// where the terminal sends the firm's traders to log in, or null when they log in with a password.
/// <paramref name="LogoUrl"/> is the firm's logo, which the terminal shows with its name.
/// </summary>
public sealed record TenantResponse(string Id, string Name, bool Listed, Uri? LoginUrl, IReadOnlyList<TenantGroupResponse> Groups, Uri? LogoUrl = null);

/// <summary>A new firm, with the key to its admin API. The key is shown only once.</summary>
public sealed record CreatedTenantResponse(string Id, string Name, bool Listed, Uri? LoginUrl, IReadOnlyList<TenantGroupResponse> Groups, string AdminApiKey);

/// <summary>What to change about a firm. Null leaves it as it is. An empty <paramref name="LoginUrl"/> or <paramref name="LogoUrl"/> removes it.</summary>
public sealed record UpdateTenantRequest(bool? Listed, string? LoginUrl, string? LogoUrl = null);

public sealed record AdminApiKeyResponse(string AdminApiKey);

public sealed record ServerNameResponse(string Id, bool Available);
