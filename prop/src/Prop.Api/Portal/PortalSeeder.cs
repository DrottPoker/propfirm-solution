using Microsoft.AspNetCore.Identity;

using Prop.Api.Configuration;
using Prop.Api.Firms;

namespace Prop.Api.Portal;

/// <summary>
/// Creates each firm's configured administrators and traders at startup, with the configured passwords. For
/// development: the configuration decides, so a changed or forgotten password is set again on the next start. A password
/// that is already the configured one is kept, since a new hash would log out its sessions.
/// </summary>
internal sealed class PortalSeeder(
    FirmCatalog firms,
    PortalUsers users,
    IPasswordHasher<PortalUser> hasher,
    IConfiguration configuration,
    TimeProvider time) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var firmOptions in configuration.GetSection(FirmOptions.SectionName).Get<List<FirmOptions>>() ?? [])
        {
            var firm = firms.ById(firmOptions.Id)!;
            foreach (var admin in firmOptions.SeedAdmins)
            {
                if (!HasPassword(await users.FindAdminAsync(firm.Id, admin.Email, cancellationToken), admin.Password))
                {
                    await users.SaveAdminAsync(firm.Id, admin.Email, hasher.HashPassword(null!, admin.Password), time.GetUtcNow(), cancellationToken);
                }
            }

            foreach (var trader in firmOptions.SeedTraders)
            {
                if (!HasPassword(await users.FindTraderAsync(firm.Id, trader.Email, cancellationToken), trader.Password))
                {
                    await users.SaveTraderAsync(firm.Id, trader.Email, hasher.HashPassword(null!, trader.Password), time.GetUtcNow(), cancellationToken);
                }
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private bool HasPassword(PortalUser? user, string password) =>
        user?.PasswordHash is { } hash && hasher.VerifyHashedPassword(user, hash, password) == PasswordVerificationResult.Success;
}
