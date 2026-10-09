using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using Trading.Service.Configuration;

namespace Trading.Service.Staff;

/// <summary>
/// Our staff's login to the staff panel (ADR 0057). The session has its own cookie and scheme, so a trader's session
/// never reaches the staff API and a staff session never reaches a trader's accounts.
/// </summary>
internal static class StaffAuth
{
    public const string Scheme = "staff";
    public const string Policy = "staff";
    public const string CookieName = "trading_staff_session";

    private const string Role = "staff";
    private const string StaffIdClaim = "staff_id";
    private const string PasswordStampClaim = "password_stamp";

    public static Task SignInAsync(HttpContext context, StaffUser staff)
    {
        Claim[] claims =
        [
            new(StaffIdClaim, staff.Id.ToString()),
            new(ClaimTypes.Role, Role),
            new(ClaimTypes.Email, staff.Email),
            new(PasswordStampClaim, PasswordStamp(staff.PasswordHash)),
        ];
        return context.SignInAsync(Scheme, new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)));
    }

    public static Task SignOutAsync(HttpContext context) => context.SignOutAsync(Scheme);

    public static string EmailOf(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Email) ?? throw new InvalidOperationException("The staff session has no email.");

    public static IServiceCollection AddStaffAuth(this IServiceCollection services)
    {
        services.AddAuthentication()
            .AddCookie(Scheme, options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.SlidingExpiration = true;
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

                // A staff member who is removed, or whose password changed since, loses the session at once.
                options.Events.OnValidatePrincipal = async context =>
                {
                    var staff = context.HttpContext.RequestServices.GetRequiredService<IStaffStore>();
                    if (context.Principal is not { } principal
                        || !Guid.TryParse(principal.FindFirstValue(StaffIdClaim), out var id)
                        || await staff.FindByIdAsync(id, context.HttpContext.RequestAborted) is not { } user
                        || principal.FindFirstValue(PasswordStampClaim) != PasswordStamp(user.PasswordHash))
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(Scheme);
                    }
                };
            });
        services.AddOptions<CookieAuthenticationOptions>(Scheme)
            .Configure<IOptions<LoginOptions>>((options, login) => options.ExpireTimeSpan = login.Value.SessionLifetime);
        services.AddAuthorizationBuilder()
            .AddPolicy(Policy, policy => policy.AddAuthenticationSchemes(Scheme).RequireRole(Role));
        return services;
    }

    // Changes with the password, without carrying the hash in the cookie.
    private static string PasswordStamp(string passwordHash) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)).AsSpan(0, 8));
}

/// <summary>Our staff on the trading platform (ADR 0057).</summary>
public sealed class StaffOptions
{
    public const string SectionName = "Staff";

    /// <summary>Staff created at startup, or given the configured password. For development, until staff are invited.</summary>
    public IReadOnlyList<StaffSeedOptions> SeedUsers { get; init; } = [];
}

public sealed class StaffSeedOptions
{
    public string Email { get; init; } = "";

    public string Password { get; init; } = "";
}

/// <summary>Creates the configured staff at startup, with the configured passwords.</summary>
internal sealed class StaffSeeder(IStaffStore staff, IPasswordHasher<StaffUser> hasher, IOptions<StaffOptions> options, TimeProvider time) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // A password that is already the configured one is kept, since a new hash would end its sessions.
        foreach (var user in options.Value.SeedUsers)
        {
            if (user.Email.Trim().Length == 0 || user.Password.Length == 0)
            {
                throw new InvalidOperationException($"Every user in {StaffOptions.SectionName}:{nameof(StaffOptions.SeedUsers)} needs an email and a password.");
            }

            if (await staff.FindByEmailAsync(user.Email, cancellationToken) is not { } existing
                || hasher.VerifyHashedPassword(existing, existing.PasswordHash, user.Password) == PasswordVerificationResult.Failed)
            {
                await staff.SaveAsync(user.Email, hasher.HashPassword(null!, user.Password), time.GetUtcNow(), cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
