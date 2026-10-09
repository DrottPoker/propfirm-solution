using System.Security.Claims;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using Trading.Engine;
using Trading.Service.Candles;
using Trading.Service.Identity;
using Trading.Service.Staff;
using Trading.Service.Tenancy;

namespace Trading.Service.Api;

/// <summary>
/// For our own staff, in the staff panel (ADR 0057): the whole platform, every server, the price feed, the instruments,
/// what traders hold and the engine. Staff log in with their own session, which reaches nothing else. Staff see account
/// numbers and amounts, never who the traders are. Versioned like the other APIs outside the terminal.
/// </summary>
internal static class StaffEndpoints
{
    public const int MaxEventsPerRequest = 200;
    public const int MaxReasonLength = 500;

    // Verified when the email is unknown, so a wrong email takes as long as a wrong password.
    private static readonly Lazy<string> UnknownStaffHash = new(() => new PasswordHasher<StaffUser>().HashPassword(null!, Guid.NewGuid().ToString()));

    public static IEndpointRouteBuilder MapStaffApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/staff/v1").WithTags("Staff");
        api.MapPost("/login", LoginAsync).RequireRateLimiting(AuthEndpoints.LoginRateLimit);
        api.MapPost("/logout", (Func<HttpContext, Task<NoContent>>)LogoutAsync);

        var staff = api.MapGroup("").RequireAuthorization(StaffAuth.Policy);
        staff.MapGet("/me", (ClaimsPrincipal principal) => TypedResults.Ok(new StaffMeResponse(StaffAuth.EmailOf(principal))));
        staff.MapGet("/overview", (StaffFigures figures, CancellationToken cancellationToken) => OkAsync(figures.OverviewAsync(cancellationToken)));
        staff.MapGet("/servers", (StaffFigures figures, CancellationToken cancellationToken, string? group = null, string? search = null) =>
            OkAsync(figures.ServersAsync(group, search, cancellationToken)));
        staff.MapPost("/servers", CreateServerAsync);
        staff.MapGet("/server-names/{id}", (string id, TenantProvisioner provisioner) => TypedResults.Ok(new ServerNameResponse(id, provisioner.IsAvailable(id))));
        staff.MapGet("/currencies", (IOptions<TenancyOptions> tenancy) => TypedResults.Ok<IReadOnlyList<string>>(tenancy.Value.Currencies));
        staff.MapGet("/servers/{id}", GetServerAsync);
        staff.MapPatch("/servers/{id}", UpdateServerAsync);
        staff.MapPost("/servers/{id}/admin-key", ReplaceAdminKeyAsync);
        staff.MapGet("/servers/{id}/events", GetServerEventsAsync);
        staff.MapGet("/accounts/{accountId}", GetAccountAsync);
        staff.MapGet("/search", (StaffFigures figures, CancellationToken cancellationToken, string? q = null) => OkAsync(figures.SearchAsync(q, cancellationToken)));
        staff.MapGet("/price-feed", (StaffFigures figures, CancellationToken cancellationToken) => OkAsync(figures.PriceFeedAsync(cancellationToken)));
        staff.MapPost("/price-feed/gaps/{gapId:guid}/retry", RetryGapAsync);
        staff.MapPost("/price-feed/history/reload", ReloadHistory);
        staff.MapGet("/instruments", (StaffFigures figures, CancellationToken cancellationToken) => OkAsync(figures.InstrumentsAsync(cancellationToken)));
        staff.MapGet("/exposure", (StaffFigures figures, CancellationToken cancellationToken, string? server = null) =>
            OkAsync(figures.ExposureAsync(server, cancellationToken)));
        staff.MapGet("/engine", (StaffFigures figures, CancellationToken cancellationToken) => OkAsync(figures.EngineAsync(cancellationToken)));
        return app;
    }

    private static async Task<Results<Ok<StaffMeResponse>, ProblemHttpResult>> LoginAsync(
        StaffLoginRequest request,
        HttpContext context,
        IStaffStore staff,
        IPasswordHasher<StaffUser> hasher,
        CancellationToken cancellationToken)
    {
        var user = string.IsNullOrWhiteSpace(request.Email) ? null : await staff.FindByEmailAsync(request.Email, cancellationToken);
        var verified = hasher.VerifyHashedPassword(user!, user?.PasswordHash ?? UnknownStaffHash.Value, request.Password ?? "");
        if (user is null || verified == PasswordVerificationResult.Failed)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Wrong email or password.");
        }

        await StaffAuth.SignInAsync(context, user);
        return TypedResults.Ok(new StaffMeResponse(user.Email));
    }

    private static async Task<NoContent> LogoutAsync(HttpContext context)
    {
        await StaffAuth.SignOutAsync(context);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<T>> OkAsync<T>(Task<T> result) => TypedResults.Ok(await result);

    private static async Task<Results<Ok<StaffServerResponse>, ProblemHttpResult>> GetServerAsync(string id, StaffFigures figures, CancellationToken cancellationToken) =>
        await figures.ServerAsync(id, cancellationToken) is { } server ? TypedResults.Ok(server) : UnknownServer();

    private static async Task<Results<Ok<StaffServerEventsResponse>, ProblemHttpResult>> GetServerEventsAsync(
        string id,
        StaffFigures figures,
        CancellationToken cancellationToken,
        string? account = null,
        int limit = 50)
    {
        if (limit is < 1 or > MaxEventsPerRequest)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, $"limit must be 1 to {MaxEventsPerRequest}.");
        }

        return await figures.ServerEventsAsync(id, string.IsNullOrWhiteSpace(account) ? null : account.Trim(), limit, cancellationToken) is { } events
            ? TypedResults.Ok(events)
            : UnknownServer();
    }

    private static async Task<Results<Ok<StaffAccountResponse>, ProblemHttpResult>> GetAccountAsync(string accountId, StaffFigures figures, CancellationToken cancellationToken) =>
        await figures.AccountAsync(accountId, cancellationToken) is { } account
            ? TypedResults.Ok(account)
            : Problem(StatusCodes.Status404NotFound, "There is no such account.");

    /// <summary>Makes a server for a firm that uses the platform on its own. Its admin key is shown only now.</summary>
    private static async Task<Results<Created<StaffCreatedServerResponse>, ProblemHttpResult>> CreateServerAsync(
        StaffCreateServerRequest request,
        ClaimsPrincipal principal,
        TenantProvisioner provisioner,
        CancellationToken cancellationToken)
    {
        var result = await provisioner.CreateAsync(TenantMaker.OfStaff(StaffAuth.EmailOf(principal)), request.Id, request.Name, request.Currency, cancellationToken);
        return result switch
        {
            ProvisioningResult.Created created => TypedResults.Created(
                $"/api/staff/v1/servers/{created.Tenant.Id}",
                new StaffCreatedServerResponse(created.Tenant.Id, created.Tenant.Name, created.AdminApiKey)),
            ProvisioningResult.Taken => Problem(StatusCodes.Status409Conflict, "The server name is taken.", RejectReason.DuplicateId.ToString()),
            ProvisioningResult.Invalid invalid => Problem(StatusCodes.Status422UnprocessableEntity, invalid.Problem, RejectReason.InvalidId.ToString()),
            ProvisioningResult.InvalidCurrency invalid => Problem(StatusCodes.Status422UnprocessableEntity, invalid.Problem, PartnerEndpoints.InvalidCurrencyReason),
            _ => throw new InvalidOperationException($"Unknown result {result.GetType().Name}."),
        };
    }

    /// <summary>
    /// Puts the server on the list traders choose from or takes it off. A partner that lists its servers itself, such as
    /// Kronant Prop when a firm goes live, can list it again.
    /// </summary>
    private static async Task<Results<Ok<StaffServerResponse>, ProblemHttpResult>> UpdateServerAsync(
        string id,
        StaffUpdateServerRequest request,
        ClaimsPrincipal principal,
        TenantCatalog tenants,
        TenantProvisioner provisioner,
        StaffFigures figures,
        CancellationToken cancellationToken)
    {
        if (tenants.ById(id) is not { } tenant)
        {
            return UnknownServer();
        }

        if (request.Listed is { } listed && listed != tenant.Listed)
        {
            await provisioner.SetListingAsync(tenant, listed, tenant.LoginUrl, tenant.LogoUrl, TenantMaker.OfStaff(StaffAuth.EmailOf(principal)), cancellationToken);
        }

        return TypedResults.Ok((await figures.ServerAsync(id, cancellationToken))!);
    }

    /// <summary>
    /// Stops the server's admin key, for example when it may have leaked. A key a partner holds is replaced by one that
    /// nobody sees, and the partner asks for a new one when the old one is turned away. Otherwise the new key is shown
    /// only now.
    /// </summary>
    private static async Task<Results<Ok<StaffReplacedKeyResponse>, ProblemHttpResult>> ReplaceAdminKeyAsync(
        string id,
        StaffReplaceKeyRequest request,
        ClaimsPrincipal principal,
        TenantCatalog tenants,
        PartnerCatalog partners,
        TenantProvisioner provisioner,
        CancellationToken cancellationToken)
    {
        if (tenants.ById(id) is not { } tenant)
        {
            return UnknownServer();
        }

        var reason = request.Reason?.Trim() ?? "";
        if (reason.Length is 0 or > MaxReasonLength)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, $"Say why, in 1 to {MaxReasonLength} characters.");
        }

        if (tenant.IsConfigured)
        {
            return Problem(StatusCodes.Status409Conflict, "A configured server's key is set in the configuration, which saves it again at every start.");
        }

        var key = await provisioner.ReplaceAdminApiKeyAsync(tenant, TenantMaker.OfStaff(StaffAuth.EmailOf(principal)), reason, cancellationToken);
        return tenant.PartnerId is { } partnerId
            ? TypedResults.Ok(new StaffReplacedKeyResponse(null, partners.All.FirstOrDefault(p => p.Id == partnerId)?.Name ?? partnerId))
            : TypedResults.Ok(new StaffReplacedKeyResponse(key, null));
    }

    /// <summary>Fills a gap in the charts again from the feed's history, in the background.</summary>
    private static async Task<Results<Accepted, ProblemHttpResult>> RetryGapAsync(Guid gapId, ChartHistory history, CancellationToken cancellationToken)
    {
        if (!history.HasHistory)
        {
            return NoHistory();
        }

        return await history.RetryGapAsync(gapId, cancellationToken)
            ? TypedResults.Accepted((string?)null)
            : Problem(StatusCodes.Status404NotFound, "There is no such gap that could not be filled.");
    }

    /// <summary>Loads the feed's whole history again, in the background, replacing the charts' bars with the feed's.</summary>
    private static Results<Accepted, ProblemHttpResult> ReloadHistory(ClaimsPrincipal principal, ChartHistory history) =>
        history.RequestReload(StaffAuth.EmailOf(principal)) ? TypedResults.Accepted((string?)null) : NoHistory();

    private static ProblemHttpResult NoHistory() => Problem(StatusCodes.Status409Conflict, "Made-up prices have no history to load.");

    private static ProblemHttpResult UnknownServer() => Problem(StatusCodes.Status404NotFound, "There is no such server.");

    private static ProblemHttpResult Problem(int statusCode, string title, string? reason = null) =>
        TypedResults.Problem(
            statusCode: statusCode,
            title: title,
            extensions: reason is null ? null : new Dictionary<string, object?> { [CommandResults.ReasonExtension] = reason });
}
