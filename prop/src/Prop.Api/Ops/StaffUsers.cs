using System.Security.Claims;

using Common.Postgres;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using Npgsql;

using Prop.Api.Challenges;
using Prop.Api.Configuration;
using Prop.Api.Portal;

namespace Prop.Api.Ops;

/// <summary>One of our own staff, who review firms in our admin view. Not a firm's administrator.</summary>
internal sealed record StaffUser(Guid Id, string Email, string PasswordHash);

/// <summary>Our staff in the database.</summary>
internal sealed class StaffUsers(NpgsqlDataSource dataSource, DatabaseSchema schema)
{
    private const string SelectStaff = "select id, email, password_hash from staff_users";

    public Task<StaffUser?> FindByEmailAsync(string email, CancellationToken cancellationToken) =>
        FindAsync($"{SelectStaff} where normalized_email = $1", Emails.Normalize(email), cancellationToken);

    public Task<StaffUser?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        FindAsync($"{SelectStaff} where id = $1", id, cancellationToken);

    public async Task<IReadOnlyList<StaffUser>> ListAsync(CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand($"{SelectStaff} order by normalized_email");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var staff = new List<StaffUser>();
        while (await reader.ReadAsync(cancellationToken))
        {
            staff.Add(new StaffUser(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        }

        return staff;
    }

    /// <summary>Creates the staff member, or gives an existing one the password.</summary>
    public async Task SaveAsync(string email, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            """
            insert into staff_users (id, email, normalized_email, password_hash, created_at) values ($1, $2, $3, $4, $5)
            on conflict (normalized_email) do update set password_hash = excluded.password_hash
            """);
        command.Parameters.AddWithValue(Guid.CreateVersion7(now));
        command.Parameters.AddWithValue(email.Trim());
        command.Parameters.AddWithValue(Emails.Normalize(email));
        command.Parameters.AddWithValue(passwordHash);
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<StaffUser?> FindAsync(string sql, object parameter, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(parameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new StaffUser(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)) : null;
    }
}

/// <summary>
/// Our staff's login to our admin view. The session has its own cookie, on our admin view's host only, so it never
/// works on the sign-up or a firm's portal, and a firm's session never works here.
/// </summary>
internal static class StaffAuth
{
    public const string Scheme = "ops-staff";
    public const string Policy = "ops-staff";
    public const string Role = "staff";

    public static Task SignInAsync(HttpContext context, StaffUser staff)
    {
        Claim[] claims =
        [
            new(PortalAuth.UserIdClaim, staff.Id.ToString()),
            new(ClaimTypes.Role, Role),
            new(ClaimTypes.Email, staff.Email),
        ];
        return context.SignInAsync(Scheme, new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)));
    }

    public static Task SignOutAsync(HttpContext context) => context.SignOutAsync(Scheme);

    public static string EmailOf(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Email) ?? throw new InvalidOperationException("The session has no email.");

    public static IServiceCollection AddStaffAuth(this IServiceCollection services)
    {
        services.AddAuthentication()
            .AddCookie(Scheme, options =>
            {
                PortalAuth.ConfigureCookie(options, "prop_ops");

                // A staff member who is removed loses the session at once, not when the cookie expires.
                options.Events.OnValidatePrincipal = async context =>
                {
                    var staff = context.HttpContext.RequestServices.GetRequiredService<StaffUsers>();
                    if (context.Principal is not { } principal
                        || await staff.FindByIdAsync(PortalAuth.UserIdOf(principal), context.HttpContext.RequestAborted) is null)
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
}

/// <summary>
/// Our admin view's requests come only on its own host, checked before anyone is asked to log in. Elsewhere they
/// look like they do not exist, and the portal knows from <c>GET /api/portal/ops</c> that an address is our admin view.
/// </summary>
internal static class OpsHost
{
    public const string PathPrefix = "/api/portal/ops";

    public static IApplicationBuilder UseOpsHost(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var ops = context.RequestServices.GetRequiredService<IOptions<PlatformOptions>>().Value.OpsUrl;
            if (context.Request.Path.StartsWithSegments(PathPrefix, StringComparison.OrdinalIgnoreCase)
                && (ops is null || !string.Equals(PortalAuth.HostOf(context), ops.Host, StringComparison.OrdinalIgnoreCase)))
            {
                await TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Our admin view is not at this address.").ExecuteAsync(context);
                return;
            }

            await next(context);
        });
}

/// <summary>Creates the configured staff at startup, with the configured passwords. For development: the configuration decides.</summary>
internal sealed class StaffSeeder(StaffUsers staff, IPasswordHasher<StaffUser> hasher, IOptions<StaffOptions> options, TimeProvider time) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var user in options.Value.SeedUsers)
        {
            await staff.SaveAsync(user.Email, hasher.HashPassword(null!, user.Password), time.GetUtcNow(), cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
