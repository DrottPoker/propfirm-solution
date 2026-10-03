using Microsoft.AspNetCore.Identity;

using Prop.Api.Configuration;
using Prop.Api.Firms;

namespace Prop.Api.Portal;

/// <summary>
/// Creates each firm's configured administrators and traders at startup, with the configured passwords. For
/// development: the configuration decides, so a changed or forgotten password is set again on the next start.
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
                await users.SaveAdminAsync(firm.Id, admin.Email, hasher.HashPassword(null!, admin.Password), time.GetUtcNow(), cancellationToken);
            }

            foreach (var trader in firmOptions.SeedTraders)
            {
                await users.SaveTraderAsync(firm.Id, trader.Email, hasher.HashPassword(null!, trader.Password), time.GetUtcNow(), cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
