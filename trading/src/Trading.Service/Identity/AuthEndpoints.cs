using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

using Trading.Service.Tenancy;

namespace Trading.Service.Identity;

/// <summary>Login for traders with a session cookie. Traders choose their firm's server, as in MetaTrader.</summary>
internal static class AuthEndpoints
{
    public const string LoginRateLimit = "login";

    // Verified when the email is unknown, so a wrong email takes as long as a wrong password.
    private static readonly Lazy<string> UnknownUserHash = new(() => new PasswordHasher<User>().HashPassword(null!, Guid.NewGuid().ToString()));

    public static IEndpointRouteBuilder MapAuthApi(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Auth");
        auth.MapPost("/login", LoginAsync).RequireRateLimiting(LoginRateLimit);
        auth.MapPost("/link", LinkLoginAsync).RequireRateLimiting(LoginRateLimit);
        auth.MapPost("/logout", (Func<HttpContext, Task<NoContent>>)LogoutAsync);
        auth.MapGet("/me", MeAsync).RequireAuthorization();
        app.MapGet("/api/servers", GetServers).WithTags("Auth");
        return app;
    }

    private static async Task<Results<Ok<MeResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request,
        HttpContext context,
        TenantCatalog tenants,
        IUserStore users,
        IPasswordHasher<User> hasher,
        CancellationToken cancellationToken)
    {
        var tenant = tenants.ById(request.Server ?? "");
        var user = tenant is null ? null : await users.FindByEmailAsync(tenant.Id, request.Email ?? "", cancellationToken);
        var verified = hasher.VerifyHashedPassword(user!, user?.PasswordHash ?? UnknownUserHash.Value, request.Password ?? "");
        if (tenant is null || user is null || verified == PasswordVerificationResult.Failed)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Wrong server, email or password.");
        }

        await SignInAsync(context, user);
        return TypedResults.Ok(await ToMeResponseAsync(user, tenant, users, cancellationToken));
    }

    /// <summary>Logs in with a one-time link from the firm's portal, created through the admin API.</summary>
    private static async Task<Results<Ok<MeResponse>, ProblemHttpResult>> LinkLoginAsync(
        LinkLoginRequest request,
        HttpContext context,
        TenantCatalog tenants,
        IUserStore users,
        ILoginLinkStore links,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var link = string.IsNullOrEmpty(request.Token)
            ? null
            : await links.UseAsync(LoginLinkTokens.Hash(request.Token), time.GetUtcNow(), cancellationToken);
        if (link is null
            || await users.FindByIdAsync(link.UserId, cancellationToken) is not { } user
            || tenants.ById(user.TenantId) is not { } tenant)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "The link has expired or was already used.");
        }

        await SignInAsync(context, user);
        return TypedResults.Ok(await ToMeResponseAsync(user, tenant, users, cancellationToken));
    }

    private static Task SignInAsync(HttpContext context, User user)
    {
        Claim[] claims =
        [
            new(CurrentUser.UserIdClaim, user.Id.ToString()),
            new(CurrentUser.TenantIdClaim, user.TenantId),
            new(ClaimTypes.Email, user.Email),
        ];
        return context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
    }

    private static async Task<NoContent> LogoutAsync(HttpContext context)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return TypedResults.NoContent();
    }

    // A firm that is no longer configured ends the sessions of its traders.
    private static async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult>> MeAsync(
        ClaimsPrincipal principal,
        TenantCatalog tenants,
        IUserStore users,
        CancellationToken cancellationToken) =>
        CurrentUser.IdOf(principal) is { } userId
        && await users.FindByIdAsync(userId, cancellationToken) is { } user
        && tenants.ById(user.TenantId) is { } tenant
            ? TypedResults.Ok(await ToMeResponseAsync(user, tenant, users, cancellationToken))
            : TypedResults.Unauthorized();

    /// <summary>The servers traders can log in to, ordered by name.</summary>
    private static Ok<IReadOnlyList<ServerInfo>> GetServers(TenantCatalog tenants) =>
        TypedResults.Ok<IReadOnlyList<ServerInfo>>([.. tenants.All.Select(ToServerInfo).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)]);

    private static async Task<MeResponse> ToMeResponseAsync(User user, Tenant tenant, IUserStore users, CancellationToken cancellationToken) =>
        new(user.Id, user.Email, ToServerInfo(tenant), await users.AccountsOfAsync(user.Id, cancellationToken));

    private static ServerInfo ToServerInfo(Tenant tenant) => new(tenant.Id, tenant.Name);
}

/// <summary>Server is the id of the firm's server, as in MetaTrader.</summary>
public sealed record LoginRequest(string? Server, string? Email, string? Password);

/// <summary>The token from a login link.</summary>
public sealed record LinkLoginRequest(string? Token);

/// <summary>A firm's server: the id traders log in with and the firm's name.</summary>
public sealed record ServerInfo(string Id, string Name);

/// <summary>The logged in trader, their firm's server and the accounts they own.</summary>
public sealed record MeResponse(Guid UserId, string Email, ServerInfo Server, IReadOnlyList<string> Accounts);

internal static class CurrentUser
{
    public const string UserIdClaim = "user_id";
    public const string TenantIdClaim = "tenant_id";

    public static Guid? IdOf(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(UserIdClaim), out var userId) ? userId : null;
}
