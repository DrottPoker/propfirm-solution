using System.Security.Claims;
using System.Text.Json;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Prop.Api.Configuration;
using Prop.Api.Firms;
using Prop.Api.Portal;

namespace Prop.Api.Identity;

/// <summary>
/// ID checks (ADR 0042): the trader's own check in the portal, the firm's choice in its admin panel, the firm's own
/// service reporting through the firm API, and Didit telling us about checks.
/// </summary>
internal static class IdentityEndpoints
{
    /// <summary>Didit's messages are small. A larger body is not Didit's.</summary>
    private const int MaxWebhookBytes = 1024 * 1024;

    public static RouteGroupBuilder MapTraderIdentity(this RouteGroupBuilder portal)
    {
        var identity = portal.MapGroup("/identity").RequireAuthorization(PortalAuth.TraderPolicy);
        identity.MapGet("", GetMineAsync);
        identity.MapPost("/start", StartAsync);
        identity.MapPost("/test/{sessionId:guid}", DecideTestAsync);
        return portal;
    }

    public static RouteGroupBuilder MapAdminIdentity(this RouteGroupBuilder admin)
    {
        admin.MapGet("/identity", GetSettingsAsync);
        admin.MapPut("/identity", SaveSettingsAsync);
        return admin;
    }

    public static RouteGroupBuilder MapFirmIdentity(this RouteGroupBuilder firm)
    {
        firm.MapGet("/traders/identity", GetTraderIdentityAsync);
        firm.MapPut("/traders/identity", ReportAsync);
        return firm;
    }

    public static IEndpointRouteBuilder MapIdentityWebhooks(this IEndpointRouteBuilder app)
    {
        // Not part of the API anyone calls: Didit does, signed with our webhook secret.
        app.MapPost("/api/identity/v1/didit", DiditWebhookAsync).ExcludeFromDescription();
        return app;
    }

    /// <summary>The trader's own ID check: how the firm checks, what waits for it, and where the check is.</summary>
    private static async Task<Ok<MyIdentityResponse>> GetMineAsync(ClaimsPrincipal principal, HttpContext context, IdentityService identity, CancellationToken cancellationToken) =>
        TypedResults.Ok(await identity.MineAsync(PortalFirmFilter.FirmOf(context), PortalAuth.UserIdOf(principal), cancellationToken));

    /// <summary>
    /// Where the trader does the check: the provider's page, or the firm's own. 409 when the firm checks by hand, the
    /// trader is verified or a check is in review, 503 when the check cannot be started now.
    /// </summary>
    private static async Task<Results<Ok<IdentityStartResponse>, ProblemHttpResult>> StartAsync(
        ClaimsPrincipal principal,
        HttpContext context,
        IdentityService identity,
        PortalUsers users,
        CancellationToken cancellationToken)
    {
        if (await users.FindByIdAsync(PortalAuth.UserIdOf(principal), PortalRoles.Trader, cancellationToken) is not { } trader)
        {
            return Problem(StatusCodes.Status401Unauthorized, "Log in again.");
        }

        var (result, url) = await identity.StartAsync(PortalFirmFilter.FirmOf(context), trader, cancellationToken);
        return result is IdentityResult.Refused refused ? ProblemOf(refused) : TypedResults.Ok(new IdentityStartResponse(url!));
    }

    /// <summary>The test page approves or declines the trader's own test check. 404 for another trader's, or a real one.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> DecideTestAsync(
        Guid sessionId,
        TestIdentityRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        IdentityService identity,
        PortalUsers users,
        CancellationToken cancellationToken)
    {
        if (await users.FindByIdAsync(PortalAuth.UserIdOf(principal), PortalRoles.Trader, cancellationToken) is not { } trader)
        {
            return Problem(StatusCodes.Status401Unauthorized, "Log in again.");
        }

        return await identity.DecideTestAsync(PortalFirmFilter.FirmOf(context), trader, sessionId, request.Approve, cancellationToken) is IdentityResult.Refused refused
            ? ProblemOf(refused)
            : TypedResults.NoContent();
    }

    /// <summary>How the firm checks its traders, our prices for the built-in checks and the checks since the firm was last charged.</summary>
    private static async Task<Ok<IdentitySettingsResponse>> GetSettingsAsync(HttpContext context, IdentityService identity, CancellationToken cancellationToken) =>
        TypedResults.Ok(await identity.SettingsViewAsync(PortalFirmFilter.FirmOf(context), cancellationToken));

    /// <summary>Saves how the firm checks its traders. 422 without a valid address for the firm's own service.</summary>
    private static async Task<Results<Ok<IdentitySettingsResponse>, ProblemHttpResult>> SaveSettingsAsync(
        IdentitySettingsRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        IdentityService identity,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        return await identity.SaveSettingsAsync(firm, request, principal.FindFirstValue(ClaimTypes.Email) ?? "", cancellationToken) is IdentityResult.Refused refused
            ? ProblemOf(refused)
            : TypedResults.Ok(await identity.SettingsViewAsync(firm, cancellationToken));
    }

    /// <summary>How the trader with the email was checked, by any provider or the firm's own service. 404 for no trader, or no check.</summary>
    private static async Task<Results<Ok<TraderIdentityResponse>, ProblemHttpResult>> GetTraderIdentityAsync(
        string email,
        HttpContext context,
        IdentityService identity,
        PortalUsers users,
        CancellationToken cancellationToken)
    {
        if (await users.FindTraderAsync(FirmApiKeyFilter.FirmOf(context).Id, email, cancellationToken) is not { } trader)
        {
            return Problem(StatusCodes.Status404NotFound, "No trader at your firm has this email.");
        }

        return TraderIdentityResponse.From(await identity.TraderAsync(trader.Id, cancellationToken)) is { } found
            ? TypedResults.Ok(found)
            : Problem(StatusCodes.Status404NotFound, "The trader has not been checked.");
    }

    /// <summary>
    /// The firm's own ID service tells how the trader with the email was checked: <c>Pending</c>, <c>InReview</c>,
    /// <c>Approved</c> with the name, date of birth and country from the document, or <c>Declined</c> with the reason.
    /// Approval ticks "ID checked". 409 unless the firm chose its own service.
    /// </summary>
    private static async Task<Results<Ok<TraderIdentityResponse>, ProblemHttpResult>> ReportAsync(
        ExternalIdentityRequest request,
        HttpContext context,
        IdentityService identity,
        CancellationToken cancellationToken)
    {
        var (result, saved) = await identity.ReportAsync(FirmApiKeyFilter.FirmOf(context), request, cancellationToken);
        return result is IdentityResult.Refused refused ? ProblemOf(refused) : TypedResults.Ok(TraderIdentityResponse.From(saved)!);
    }

    /// <summary>
    /// Didit tells that a check changed. Only a valid signature is accepted, and the decision is read from Didit's API. 503
    /// when Didit's API cannot be reached, so Didit sends it again.
    /// </summary>
    private static async Task<IResult> DiditWebhookAsync(HttpContext context, IdentityService identity, IOptions<IdentityCheckOptions> options, CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength > MaxWebhookBytes)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "The message is too large.");
        }

        using var buffer = new MemoryStream();
        await context.Request.Body.CopyToAsync(buffer, cancellationToken);
        var body = buffer.ToArray();
        if (!DiditSignature.IsValid(options.Value.Didit.WebhookSecret, context.Request.Headers[DiditSignature.Header], body))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "The signature is not valid.");
        }

        string? sessionId;
        try
        {
            using var message = JsonDocument.Parse(body);
            sessionId = message.RootElement.TryGetProperty("session_id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
        }
        catch (JsonException)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The message is not JSON.");
        }

        // Messages about other things, such as users or transactions, are answered so Didit does not send them again.
        return sessionId is null || await identity.DiditChangedAsync(sessionId, cancellationToken)
            ? TypedResults.Ok()
            : TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Didit could not be asked about the check. Send it again.");
    }

    private static ProblemHttpResult Problem(int statusCode, string title) => TypedResults.Problem(statusCode: statusCode, title: title);

    private static ProblemHttpResult ProblemOf(IdentityResult.Refused refused) =>
        TypedResults.Problem(
            statusCode: refused.StatusCode,
            title: refused.Problem,
            extensions: refused.Field is null ? null : new Dictionary<string, object?> { ["field"] = refused.Field });
}
