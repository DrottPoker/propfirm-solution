using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Service.Configuration;
using Trading.Service.Identity;
using Trading.Service.Tenancy;

namespace Trading.Service.Engine;

/// <summary>
/// Creates the configured development accounts and their owners at startup if they do not exist yet.
/// Accounts restored from the journal are left as they are. Fails startup if a new account is rejected.
/// </summary>
internal sealed class AccountSeeder(
    EngineHost engine,
    IUserStore users,
    IPasswordHasher<User> hasher,
    TenantCatalog tenants,
    IOptions<TradingOptions> options) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Fails with the recovery error if the journal could not be replayed. The failure also stops the host,
        // which cancels this wait, and the cancellation must not hide why the service did not start.
        try
        {
            await engine.Ready.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (engine.Ready.IsFaulted)
        {
            await engine.Ready;
        }

        await tenants.Ready.WaitAsync(cancellationToken);
        foreach (var seed in options.Value.SeedAccounts)
        {
            var tenant = tenants.ByGroup(seed.GroupId)
                ?? throw new InvalidOperationException($"Seed account {seed.AccountId} is in group {seed.GroupId}, which no tenant has.");
            var owner = await users.FindByEmailAsync(tenant.Id, seed.OwnerEmail, cancellationToken)
                ?? await users.CreateAsync(tenant.Id, seed.OwnerEmail, hasher.HashPassword(null!, seed.OwnerPassword), cancellationToken)
                ?? throw new InvalidOperationException($"Could not create the owner of seed account {seed.AccountId}.");
            await users.AddAccountAsync(owner.Id, seed.AccountId, cancellationToken);

            if (await engine.QueryAsync(e => e.GetAccount(seed.AccountId) is not null, cancellationToken))
            {
                continue;
            }

            Ensure(await engine.SendAsync(t => new CreateAccount(t, seed.AccountId, seed.GroupId, seed.InitialBalance), cancellationToken));
            foreach (var floor in seed.Floors)
            {
                var rule = floor.ToRule();
                Ensure(await engine.SendAsync(t => new SetEquityFloor(t, seed.AccountId, floor.FloorId, rule), cancellationToken));
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static void Ensure(IReadOnlyList<EventEnvelope> events)
    {
        if (events.Select(e => e.Event).OfType<InputRejected>().FirstOrDefault() is { } rejected)
        {
            throw new InvalidOperationException($"Seed input {rejected.Input.GetType().Name} was rejected: {rejected.Reason}.");
        }
    }
}
