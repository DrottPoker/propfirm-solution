using Microsoft.Extensions.Options;

using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Service.Configuration;

namespace Trading.Service.Engine;

/// <summary>
/// Creates the configured development accounts at startup if they do not exist yet. Accounts restored
/// from the journal are left as they are. Fails startup if a new account is rejected.
/// </summary>
internal sealed class AccountSeeder(EngineHost engine, IOptions<TradingOptions> options) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Fails with the recovery error if the journal could not be replayed.
        await engine.Ready.WaitAsync(cancellationToken);

        foreach (var seed in options.Value.SeedAccounts)
        {
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
