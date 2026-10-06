using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

using Prop.Api.Configuration;
using Prop.Api.Firms;
using Prop.Api.Review;

namespace Prop.Api.Portal;

/// <summary>
/// Portal logins. Traders and administrators have separate sessions, each in its own cookie, so one person can
/// be logged in as both at once, for example a firm testing its own portal.
/// </summary>
internal static class PortalAuth
{
    public const string TraderPolicy = "portal-trader";
    public const string AdminPolicy = "portal-admin";
    public const string LoginRateLimit = "portal-login";

    public const string TraderScheme = "portal-trader";
    public const string AdminScheme = "portal-admin";

    public const string UserIdClaim = "user_id";
    public const string FirmIdClaim = "firm_id";

    /// <summary>Which password the session started with. A new password ends older sessions.</summary>
    public const string PasswordStampClaim = "password_stamp";

    /// <summary>
    /// The host the portal was opened on, without port. The portal sends its requests through its own address
    /// and passes the original host in X-Forwarded-Host, so the login cookie belongs to the firm's domain.
    /// </summary>
    public static string HostOf(HttpContext context)
    {
        var forwarded = context.Request.Headers["X-Forwarded-Host"].FirstOrDefault()?.Split(',')[0].Trim();
        return string.IsNullOrEmpty(forwarded) ? context.Request.Host.Host : new HostString(forwarded).Host;
    }

    public static string SchemeOf(string role) => role == PortalRoles.Admin ? AdminScheme : TraderScheme;

    /// <summary>
    /// Starts the user's session. Every way an administrator logs in comes here, the password, the link after signing up, an
    /// invitation and a new password, so the time is kept for the Team page. Traders' logins are not kept.
    /// </summary>
    public static async Task SignInAsync(HttpContext context, PortalUser user)
    {
        var scheme = SchemeOf(user.Role);
        Claim[] claims =
        [
            new(UserIdClaim, user.Id.ToString()),
            new(FirmIdClaim, user.FirmId),
            new(ClaimTypes.Role, user.Role),
            new(ClaimTypes.Email, user.Email),
            new(PasswordStampClaim, PasswordStamp(user.PasswordHash)),
        ];
        await context.SignInAsync(scheme, new ClaimsPrincipal(new ClaimsIdentity(claims, scheme)));

        if (user.Role == PortalRoles.Admin)
        {
            var services = context.RequestServices;

            // When each administrator last logged in, for the Team page.
            await services.GetRequiredService<FirmAdmins>().LoggedInAsync(user.Id, services.GetRequiredService<TimeProvider>().GetUtcNow(), context.RequestAborted);

            // Logging in to the admin panel uses it, which keeps the firm's sandbox open, or opens it again (ADR 0045).
            await services.GetRequiredService<FirmActivity>().SeenAsync(user.FirmId, context.RequestAborted);
        }
    }

    /// <summary>
    /// The password as the session keeps it: part of a hash of its hash, which differs for each new password, also the same
    /// one chosen again, since each hash has its own salt. "0" without a password.
    /// </summary>
    public static string PasswordStamp(string? passwordHash) =>
        passwordHash is null ? "0" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)), 0, 8);

    public static bool HasPasswordStamp(ClaimsPrincipal principal, string? passwordHash) =>
        principal.FindFirstValue(PasswordStampClaim) == PasswordStamp(passwordHash);

    public static Task SignOutAsync(HttpContext context, string role) => context.SignOutAsync(SchemeOf(role));

    public static Guid UserIdOf(ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(UserIdClaim) ?? throw new InvalidOperationException("The session has no user."));

    public static IServiceCollection AddPortalAuth(this IServiceCollection services)
    {
        services.AddAuthentication()
            .AddCookie(TraderScheme, options =>
            {
                ConfigureCookie(options, "prop_trader");
                options.Events.OnValidatePrincipal = context => ValidateAsync(context, PortalRoles.Trader, TraderScheme);
            })
            .AddCookie(AdminScheme, options =>
            {
                ConfigureCookie(options, "prop_admin");
                options.Events.OnValidatePrincipal = context => ValidateAsync(context, PortalRoles.Admin, AdminScheme);
            });
        foreach (var scheme in new[] { TraderScheme, AdminScheme })
        {
            services.AddOptions<CookieAuthenticationOptions>(scheme)
                .Configure<IOptions<LoginOptions>>((options, login) => options.ExpireTimeSpan = login.Value.SessionLifetime);
        }

        services.AddAuthorizationBuilder()
            .AddPolicy(TraderPolicy, policy => policy.AddAuthenticationSchemes(TraderScheme).RequireRole(PortalRoles.Trader))
            .AddPolicy(AdminPolicy, policy => policy.AddAuthenticationSchemes(AdminScheme).RequireRole(PortalRoles.Admin));
        return services;
    }

    // A removed administrator's session, and a session from before a new password, stop working at once, not when the cookie
    // expires. So does a trader's who is not one of the firm's administrators while we have not approved the firm (ADR 0043).
    private static async Task ValidateAsync(CookieValidatePrincipalContext context, string role, string scheme)
    {
        var users = context.HttpContext.RequestServices.GetRequiredService<PortalUsers>();
        var approval = context.HttpContext.RequestServices.GetRequiredService<FirmApproval>();
        var cancellationToken = context.HttpContext.RequestAborted;
        if (context.Principal is not { } principal
            || await users.FindByIdAsync(UserIdOf(principal), role, cancellationToken) is not { } user
            || !HasPasswordStamp(principal, user.PasswordHash)
            || (role == PortalRoles.Trader && !await approval.MayReachAsync(user.FirmId, user.Email, cancellationToken)))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(scheme);
            return;
        }

        // An administrator using the admin panel keeps the firm's sandbox open, or opens it again (ADR 0045).
        if (role == PortalRoles.Admin)
        {
            await context.HttpContext.RequestServices.GetRequiredService<FirmActivity>().SeenAsync(user.FirmId, cancellationToken);
        }
    }

    internal static void ConfigureCookie(CookieAuthenticationOptions options, string name)
    {
        options.Cookie.Name = name;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.SlidingExpiration = true;

        // An API answers with status codes instead of redirecting to a login page.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }
}

/// <summary>
/// Finds the firm from the portal's host for every portal request. A session from another firm's portal is
/// not accepted, so a cookie can never act across firms.
/// </summary>
internal sealed class PortalFirmFilter(FirmCatalog firms) : IEndpointFilter
{
    private static readonly object FirmKey = new();

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        await firms.Ready.WaitAsync(http.RequestAborted);
        if (firms.ByHost(PortalAuth.HostOf(http)) is not { } firm)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "No firm's portal is at this address.");
        }

        if (http.User.Identity?.IsAuthenticated == true && http.User.FindFirst(PortalAuth.FirmIdClaim)?.Value != firm.Id)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "The session belongs to another firm.");
        }

        http.Items[FirmKey] = firm;
        return await next(context);
    }

    // The firm as it was when the request began.
    public static Firm FirmOf(HttpContext context) =>
        context.Items[FirmKey] as Firm ?? throw new InvalidOperationException("The portal firm filter did not run.");
}
