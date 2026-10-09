using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

using Trading.Engine;
using Trading.Service.Tenancy;

namespace Trading.Service.Identity;

/// <summary>
/// Login for traders with a session cookie (ADR 0058). A trader logs in on the firm's own login page, which names its
/// server, or with only an email address and a password, which finds the firm. No list of every firm is shown: a listed
/// firm can be found by its name.
/// </summary>
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
        app.MapGet("/api/servers", FindServersAsync).WithTags("Auth");
        app.MapGet("/api/servers/{id}", GetServerAsync).WithTags("Auth");
        return app;
    }

    /// <summary>
    /// Logs in with the email address and the password, at the server named, or at the one firm where they fit. Where
    /// they fit at several firms, the trader is told which, and chooses. Only firms whose traders may log in to the
    /// terminal with a password are tried; the others log their traders in through their own portal.
    /// </summary>
    private static async Task<Results<Ok<MeResponse>, Conflict<ChooseServerResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request,
        HttpContext context,
        TenantCatalog tenants,
        IUserStore users,
        IPasswordHasher<User> hasher,
        EngineConfiguration configuration,
        CancellationToken cancellationToken)
    {
        await tenants.Ready.WaitAsync(cancellationToken);
        var email = request.Email ?? "";
        IReadOnlyList<User> candidates = string.IsNullOrEmpty(request.Server)
            ? await users.FindAllByEmailAsync(email, cancellationToken)
            : await users.FindByEmailAsync(request.Server, email, cancellationToken) is { } named ? [named] : [];
        var fitting = new List<(User User, Tenant Tenant)>();
        foreach (var candidate in candidates)
        {
            if (tenants.ById(candidate.TenantId) is { Profile.PasswordLogin: true } firm
                && hasher.VerifyHashedPassword(candidate, candidate.PasswordHash, request.Password ?? "") != PasswordVerificationResult.Failed)
            {
                fitting.Add((candidate, firm));
            }
        }

        if (candidates.Count == 0)
        {
            // A wrong email takes as long as a wrong password.
            hasher.VerifyHashedPassword(null!, UnknownUserHash.Value, request.Password ?? "");
        }

        if (fitting.Count == 0)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Wrong email or password.");
        }

        if (fitting.Count > 1)
        {
            return TypedResults.Conflict(
                new ChooseServerResponse([.. fitting.Select(f => ToServerInfo(f.Tenant)).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)]));
        }

        var (user, tenant) = fitting[0];
        await SignInAsync(context, user);
        return TypedResults.Ok(await ToMeResponseAsync(user, tenant, users, configuration, cancellationToken));
    }

    /// <summary>Logs in with a one-time link from the firm's portal, created through the admin API.</summary>
    private static async Task<Results<Ok<MeResponse>, ProblemHttpResult>> LinkLoginAsync(
        LinkLoginRequest request,
        HttpContext context,
        TenantCatalog tenants,
        IUserStore users,
        ILoginLinkStore links,
        TimeProvider time,
        EngineConfiguration configuration,
        CancellationToken cancellationToken)
    {
        await tenants.Ready.WaitAsync(cancellationToken);
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
        return TypedResults.Ok(await ToMeResponseAsync(user, tenant, users, configuration, cancellationToken));
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

    // A firm that no longer exists ends the sessions of its traders.
    private static async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult>> MeAsync(
        ClaimsPrincipal principal,
        TenantCatalog tenants,
        IUserStore users,
        EngineConfiguration configuration,
        CancellationToken cancellationToken)
    {
        await tenants.Ready.WaitAsync(cancellationToken);
        return CurrentUser.IdOf(principal) is { } userId
            && await users.FindByIdAsync(userId, cancellationToken) is { } user
            && tenants.ById(user.TenantId) is { } tenant
                ? TypedResults.Ok(await ToMeResponseAsync(user, tenant, users, configuration, cancellationToken))
                : TypedResults.Unauthorized();
    }

    /// <summary>
    /// The listed firms whose name holds the search, at most a few and ordered by name, so a trader finds their firm's
    /// login without a list of every firm on the platform (ADR 0058). Firms in the sandbox are not listed. A search
    /// shorter than two letters finds nothing.
    /// </summary>
    private static async Task<Ok<IReadOnlyList<ServerInfo>>> FindServersAsync(string? search, TenantCatalog tenants, CancellationToken cancellationToken)
    {
        await tenants.Ready.WaitAsync(cancellationToken);
        var text = search?.Trim() ?? "";
        if (text.Length < MinServerSearch)
        {
            return TypedResults.Ok<IReadOnlyList<ServerInfo>>([]);
        }

        return TypedResults.Ok<IReadOnlyList<ServerInfo>>(
            [
                .. tenants.All
                    .Where(t => t.Listed && t.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
                    .Select(ToServerInfo)
                    .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(MaxServerResults),
            ]);
    }

    private const int MinServerSearch = 2;

    private const int MaxServerResults = 5;

    /// <summary>
    /// A server by its id, also one that is not listed, so the terminal can send a trader back to the firm's login
    /// when a link from the firm's portal named the server.
    /// </summary>
    private static async Task<Results<Ok<ServerInfo>, NotFound>> GetServerAsync(string id, TenantCatalog tenants, CancellationToken cancellationToken)
    {
        await tenants.Ready.WaitAsync(cancellationToken);
        return tenants.ById(id) is { } tenant ? TypedResults.Ok(ToServerInfo(tenant)) : TypedResults.NotFound();
    }

    private static async Task<MeResponse> ToMeResponseAsync(User user, Tenant tenant, IUserStore users, EngineConfiguration configuration, CancellationToken cancellationToken) =>
        new(
            user.Id,
            user.Email,
            user.Name,
            ToServerInfo(tenant),
            await users.AccountsOfAsync(user.Id, cancellationToken),
            await users.AccountDetailsOfAsync(user.Id, cancellationToken),
            configuration.MaxQuoteAge.TotalSeconds);

    private static ServerInfo ToServerInfo(Tenant tenant) => new(tenant.Id, tenant.Name, tenant.LoginUrl, tenant.LogoUrl, tenant.Profile);
}

/// <summary>
/// Server is the id of the firm's server, as in MetaTrader, from the firm's own login page. Without it the firm is found
/// from the email address and the password.
/// </summary>
public sealed record LoginRequest(string? Server, string? Email, string? Password);

/// <summary>The email address and the password fit at several firms: the trader chooses which to log in to.</summary>
public sealed record ChooseServerResponse(IReadOnlyList<ServerInfo> Servers);

/// <summary>The token from a login link.</summary>
public sealed record LinkLoginRequest(string? Token);

/// <summary>
/// A firm's server: the id traders log in with and the firm's name. With <paramref name="LoginUrl"/>, the firm's
/// traders log in there, for example on the firm's portal, which opens the terminal with a one-time link.
/// <paramref name="LogoUrl"/> is the firm's logo, when it has one. <paramref name="Profile"/> is how its terminal works
/// (ADR 0058).
/// </summary>
public sealed record ServerInfo(string Id, string Name, Uri? LoginUrl, Uri? LogoUrl, TerminalProfile Profile);

/// <summary>
/// The logged in trader, with their name when the firm told it, their firm's server and the accounts they own, with what
/// the firm says about each in <paramref name="AccountDetails"/>, in the same order. Orders, closes and stop changes are
/// refused while the latest price is older than <paramref name="MaxPriceAgeSeconds"/>.
/// </summary>
public sealed record MeResponse(
    Guid UserId,
    string Email,
    string? Name,
    ServerInfo Server,
    IReadOnlyList<string> Accounts,
    IReadOnlyList<AccountDetails> AccountDetails,
    double MaxPriceAgeSeconds);

internal static class CurrentUser
{
    public const string UserIdClaim = "user_id";
    public const string TenantIdClaim = "tenant_id";

    public static Guid? IdOf(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(UserIdClaim), out var userId) ? userId : null;

    public static string? TenantIdOf(ClaimsPrincipal principal) => principal.FindFirstValue(TenantIdClaim);
}
