using Microsoft.Extensions.Options;

using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Service.Configuration;

namespace Trading.Service.Engine;

/// <summary>Creates the configured development accounts at startup. Fails startup if one is rejected.</summary>
internal sealed class AccountSeeder(EngineHost engine, IOptions<TradingOptions> options) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var seed in options.Value.SeedAccounts)
        {
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
