using Microsoft.AspNetCore.Http.HttpResults;

using Trading.Engine;
using Trading.Service.Engine;
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
        partner.MapPost("/tenants/{id}/admin-key", ReplaceAdminKeyAsync);
        return app;
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
        var result = await provisioner.CreateAsync(PartnerApiKeyFilter.PartnerOf(context), request.Id, request.Name, cancellationToken);
        return result switch
        {
            ProvisioningResult.Created created => TypedResults.Created(
                $"/api/partner/v1/tenants/{created.Tenant.Id}",
                new CreatedTenantResponse(
                    created.Tenant.Id,
                    created.Tenant.Name,
                    created.Tenant.Listed,
                    [.. created.Groups.Select(g => new TenantGroupResponse(g.Id, g.Currency))],
                    created.AdminApiKey)),
            ProvisioningResult.Taken => Problem(StatusCodes.Status409Conflict, "The server name is taken.", RejectReason.DuplicateId.ToString()),
            ProvisioningResult.Invalid invalid => Problem(StatusCodes.Status422UnprocessableEntity, invalid.Problem, RejectReason.InvalidId.ToString()),
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

    /// <summary>A new admin API key for the firm, for example when the partner lost the first one. The old key stops working.</summary>
    private static async Task<Results<Ok<AdminApiKeyResponse>, ProblemHttpResult>> ReplaceAdminKeyAsync(
        string id,
        HttpContext context,
        TenantCatalog tenants,
        TenantProvisioner provisioner,
        CancellationToken cancellationToken) =>
        PartnerTenant(context, tenants, id) is { } tenant
            ? TypedResults.Ok(new AdminApiKeyResponse(await provisioner.ReplaceAdminApiKeyAsync(tenant, cancellationToken)))
            : UnknownTenant();

    // Firms of other partners, and configured firms, look like they do not exist.
    private static Tenant? PartnerTenant(HttpContext context, TenantCatalog tenants, string id) =>
        tenants.ById(id) is { } tenant && tenant.PartnerId == PartnerApiKeyFilter.PartnerOf(context).Id ? tenant : null;

    private static async Task<TenantResponse> ToResponseAsync(Tenant tenant, EngineHost engine, CancellationToken cancellationToken)
    {
        var groups = await engine.QueryAsync(e => tenant.Groups.Select(e.GetGroup).OfType<TradingGroup>().ToList(), cancellationToken);
        return new TenantResponse(tenant.Id, tenant.Name, tenant.Listed, [.. groups.Select(g => new TenantGroupResponse(g.Id, g.Currency))]);
    }

    private static ProblemHttpResult UnknownTenant() => Problem(StatusCodes.Status404NotFound, "The partner has no such firm.");

    private static ProblemHttpResult Problem(int statusCode, string title, string? reason = null) =>
        TypedResults.Problem(
            statusCode: statusCode,
            title: title,
            extensions: reason is null ? null : new Dictionary<string, object?> { [CommandResults.ReasonExtension] = reason });
}

/// <summary>A firm with its server <paramref name="Id"/>, which traders log in to, and its name.</summary>
public sealed record CreateTenantRequest(string? Id, string? Name);

/// <summary>A firm's trading group and its account currency.</summary>
public sealed record TenantGroupResponse(string Id, string Currency);

/// <summary>A firm. Only <paramref name="Listed"/> servers are on the list traders choose from.</summary>
public sealed record TenantResponse(string Id, string Name, bool Listed, IReadOnlyList<TenantGroupResponse> Groups);

/// <summary>A new firm, with the key to its admin API. The key is shown only once.</summary>
public sealed record CreatedTenantResponse(string Id, string Name, bool Listed, IReadOnlyList<TenantGroupResponse> Groups, string AdminApiKey);

public sealed record AdminApiKeyResponse(string AdminApiKey);

public sealed record ServerNameResponse(string Id, bool Available);
