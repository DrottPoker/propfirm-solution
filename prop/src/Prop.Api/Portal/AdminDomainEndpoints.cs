using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Prop.Api.Api;
using Prop.Api.Configuration;
using Prop.Api.Firms;

namespace Prop.Api.Portal;

/// <summary>
/// The firm's own domain for its portal (ADR 0039): it is added in the admin panel, proved with a TXT record and pointed
/// to us with a CNAME record, and becomes the portal's address once both are there. Our proxy asks which hosts may get a
/// certificate.
/// </summary>
internal static class AdminDomainEndpoints
{
    public static RouteGroupBuilder MapAdminDomain(this RouteGroupBuilder admin)
    {
        admin.MapGet("/domain", GetAsync);
        admin.MapPut("/domain", SaveAsync);
        admin.MapPost("/domain/check", CheckAsync);
        admin.MapDelete("/domain", RemoveAsync);
        return admin;
    }

    /// <summary>
    /// Answers our proxy, which makes a certificate the first time a host is asked for: only a firm's portal may get one.
    /// Not reachable from the internet (ADR 0018).
    /// </summary>
    public static IEndpointRouteBuilder MapCertificateCheck(this IEndpointRouteBuilder app)
    {
        app.MapGet(
                "/api/tls/allowed",
                async (string? domain, CustomDomainStore domains, CancellationToken cancellationToken) =>
                    DomainRules.Normalize(domain) is { } host && await domains.IsFirmHostAsync(host, cancellationToken) ? Results.Ok() : Results.NotFound())
            .ExcludeFromDescription();
        return app;
    }

    private static async Task<Ok<DomainResponse>> GetAsync(
        HttpContext context,
        CustomDomainStore domains,
        IOptions<DomainOptions> options,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        return TypedResults.Ok(DomainResponse.From(await domains.GetAsync(firm.Id, cancellationToken), options.Value));
    }

    // A new domain is looked up at once, so records added beforehand take effect without waiting.
    private static async Task<Results<Ok<DomainResponse>, ProblemHttpResult>> SaveAsync(
        DomainRequest request,
        HttpContext context,
        CustomDomainStore domains,
        DomainVerifier verifier,
        IOptions<DomainOptions> options,
        IOptions<PlatformOptions> platform,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        if (!options.Value.Enabled)
        {
            return AccountActions.Problem(StatusCodes.Status409Conflict, "Own domains are not on here yet.");
        }

        var domain = DomainRules.Normalize(request.Domain);
        if (DomainRules.Problem(domain, platform.Value, options.Value) is { } problem)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, problem);
        }

        switch (await domains.SaveAsync(firm.Id, domain!, platform.Value.PortalUrlOf(firm.Id), time.GetUtcNow(), cancellationToken))
        {
            case DomainSaveOutcome.Taken:
                return AccountActions.Problem(StatusCodes.Status409Conflict, "Another firm has that domain.");
            case DomainSaveOutcome.Configured:
                return AccountActions.Problem(StatusCodes.Status409Conflict, "Your firm's addresses come from our configuration, so they are changed there.");
        }

        return TypedResults.Ok(DomainResponse.From(await verifier.CheckAsync(firm.Id, cancellationToken), options.Value));
    }

    private static async Task<Results<Ok<DomainResponse>, ProblemHttpResult>> CheckAsync(
        HttpContext context,
        DomainVerifier verifier,
        IOptions<DomainOptions> options,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        return await verifier.CheckAsync(firm.Id, cancellationToken) is { } domain
            ? TypedResults.Ok(DomainResponse.From(domain, options.Value))
            : AccountActions.Problem(StatusCodes.Status404NotFound, "Add your domain first.");
    }

    // The portal goes back to its address with us, which kept working all along.
    private static async Task<Ok<DomainResponse>> RemoveAsync(
        HttpContext context,
        CustomDomainStore domains,
        FirmStore store,
        FirmCatalog firms,
        Challenges.WorkSignals signals,
        IOptions<DomainOptions> options,
        IOptions<PlatformOptions> platform,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        await domains.RemoveAsync(firm.Id, platform.Value.PortalUrlOf(firm.Id), cancellationToken);
        await AdminSettingsEndpoints.ReloadAsync(firm, store, firms, cancellationToken);
        signals.Provisioning.Set();
        return TypedResults.Ok(DomainResponse.From(null, options.Value));
    }
}

/// <summary>The domain the firm wants its portal on, such as portal.yourfirm.com.</summary>
public sealed record DomainRequest(string? Domain);

/// <summary>
/// The firm's own domain and the DNS records it needs: a CNAME record to <paramref name="CnameTarget"/>, and a TXT record
/// named <paramref name="TxtName"/> with <paramref name="TxtValue"/>. <paramref name="Problem"/> says what the last lookup,
/// at <paramref name="CheckedAt"/>, missed. <paramref name="Available"/> is false while own domains are not on.
/// </summary>
public sealed record DomainResponse(
    bool Available,
    string? Domain,
    DomainStatus? Status,
    string CnameTarget,
    string? TxtName,
    string? TxtValue,
    DateTimeOffset? CheckedAt,
    DateTimeOffset? ActiveAt,
    string? Problem)
{
    internal static DomainResponse From(CustomDomain? domain, DomainOptions options) =>
        new(options.Enabled, domain?.Domain, domain?.Status, options.CnameTarget, domain?.TxtName, domain?.Token, domain?.CheckedAt, domain?.ActiveAt, domain?.Problem);
}
