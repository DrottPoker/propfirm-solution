using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

using Prop.Api.Configuration;
using Prop.Api.Firms;

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

    public static Task SignInAsync(HttpContext context, PortalUser user)
    {
        var scheme = SchemeOf(user.Role);
        Claim[] claims =
        [
            new(UserIdClaim, user.Id.ToString()),
            new(FirmIdClaim, user.FirmId),
            new(ClaimTypes.Role, user.Role),
            new(ClaimTypes.Email, user.Email),
        ];
        return context.SignInAsync(scheme, new ClaimsPrincipal(new ClaimsIdentity(claims, scheme)));
    }

    public static Task SignOutAsync(HttpContext context, string role) => context.SignOutAsync(SchemeOf(role));

    public static Guid UserIdOf(ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(UserIdClaim) ?? throw new InvalidOperationException("The session has no user."));

    public static IServiceCollection AddPortalAuth(this IServiceCollection services)
    {
        services.AddAuthentication()
            .AddCookie(TraderScheme, options => ConfigureCookie(options, "prop_trader"))
            .AddCookie(AdminScheme, options => ConfigureCookie(options, "prop_admin"));
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

    private static void ConfigureCookie(CookieAuthenticationOptions options, string name)
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

    public static Firm FirmOf(HttpContext context) =>
        context.Items[FirmKey] as Firm ?? throw new InvalidOperationException("The portal firm filter did not run.");
}
