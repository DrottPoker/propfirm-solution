using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

using Trading.Service.Tenancy;

namespace Trading.Service.Identity;

/// <summary>Login for traders with a session cookie, and the firm's branding for the terminal.</summary>
internal static class AuthEndpoints
{
    public const string LoginRateLimit = "login";

    // Verified when the email is unknown, so a wrong email takes as long as a wrong password.
    private static readonly Lazy<string> UnknownUserHash = new(() => new PasswordHasher<User>().HashPassword(null!, Guid.NewGuid().ToString()));

    public static IEndpointRouteBuilder MapAuthApi(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Auth");
        auth.MapPost("/login", LoginAsync).RequireRateLimiting(LoginRateLimit);
        auth.MapPost("/logout", (Func<HttpContext, Task<NoContent>>)LogoutAsync);
        auth.MapGet("/me", MeAsync).RequireAuthorization();
        app.MapGet("/api/branding", GetBranding).WithTags("Auth");
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
        if (tenants.ByHost(context.Request.Host.Host) is not { } tenant)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "No firm uses this address.");
        }

        var user = await users.FindByEmailAsync(tenant.Id, request.Email ?? "", cancellationToken);
        var verified = hasher.VerifyHashedPassword(user!, user?.PasswordHash ?? UnknownUserHash.Value, request.Password ?? "");
        if (user is null || verified == PasswordVerificationResult.Failed)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Wrong email or password.");
        }

        Claim[] claims =
        [
            new(CurrentUser.UserIdClaim, user.Id.ToString()),
            new(CurrentUser.TenantIdClaim, user.TenantId),
            new(ClaimTypes.Email, user.Email),
        ];
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
        return TypedResults.Ok(await ToMeResponseAsync(user, users, cancellationToken));
    }

    private static async Task<NoContent> LogoutAsync(HttpContext context)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult>> MeAsync(ClaimsPrincipal principal, IUserStore users, CancellationToken cancellationToken) =>
        CurrentUser.IdOf(principal) is { } userId && await users.FindByIdAsync(userId, cancellationToken) is { } user
            ? TypedResults.Ok(await ToMeResponseAsync(user, users, cancellationToken))
            : TypedResults.Unauthorized();

    /// <summary>Branding for the firm that owns the host. The terminal passes the host it was opened on.</summary>
    private static Results<Ok<Branding>, NotFound> GetBranding(string? host, HttpContext context, TenantCatalog tenants) =>
        tenants.ByHost(host ?? context.Request.Host.Host) is { } tenant ? TypedResults.Ok(tenant.Branding) : TypedResults.NotFound();

    private static async Task<MeResponse> ToMeResponseAsync(User user, IUserStore users, CancellationToken cancellationToken) =>
        new(user.Id, user.Email, user.TenantId, await users.AccountsOfAsync(user.Id, cancellationToken));
}

public sealed record LoginRequest(string? Email, string? Password);

/// <summary>The logged in trader and the accounts they own.</summary>
public sealed record MeResponse(Guid UserId, string Email, string TenantId, IReadOnlyList<string> Accounts);

internal static class CurrentUser
{
    public const string UserIdClaim = "user_id";
    public const string TenantIdClaim = "tenant_id";

    public static Guid? IdOf(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(UserIdClaim), out var userId) ? userId : null;
}
