using System.Text.Json;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

using Trading.Engine;
using Trading.Service.Engine;
using Trading.Service.Json;

namespace Trading.Service.Realtime;

public sealed class RealtimeOptions
{
    public const string SectionName = "Realtime";

    /// <summary>How often changed prices are sent. Limits traffic to the browser.</summary>
    public TimeSpan PriceInterval { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>How often changed account snapshots are sent.</summary>
    public TimeSpan AccountInterval { get; init; } = TimeSpan.FromMilliseconds(250);
}

/// <summary>
/// Pushes events as they happen, and prices and account snapshots at a limited rate,
/// to the subscribed clients. Never blocks the engine loop.
/// </summary>
internal sealed partial class RealtimePublisher(
    EngineHost engine,
    EventLog eventLog,
    SubscriptionRegistry subscriptions,
    IHubContext<TradingHub, ITradingClient> hub,
    TimeProvider time,
    IOptions<RealtimeOptions> options,
    ILogger<RealtimePublisher> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions SnapshotJson = EngineJson.CreateOptions();

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(ForwardEventsAsync(stoppingToken), PublishPricesAsync(stoppingToken), PublishAccountsAsync(stoppingToken));

    private async Task ForwardEventsAsync(CancellationToken cancellationToken)
    {
        var reader = eventLog.Published;
        while (await reader.WaitToReadAsync(cancellationToken))
        {
            var batch = new List<EventEnvelope>();
            while (reader.TryRead(out var envelope))
            {
                batch.Add(envelope);
            }

            foreach (var accountEvents in batch.GroupBy(e => EventLog.AccountIdOf(e.Event)))
            {
                if (accountEvents.Key is { } accountId)
                {
                    await SendAsync(hub.Clients.Group(RealtimeGroups.Account(accountId)).Events(accountEvents.ToList()));
                }
            }
        }
    }

    private async Task PublishPricesAsync(CancellationToken cancellationToken)
    {
        var lastSent = new Dictionary<(string GroupId, string Symbol), SymbolPrice>();
        using var timer = new PeriodicTimer(options.Value.PriceInterval, time);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var groupIds = subscriptions.Groups();
            foreach (var key in lastSent.Keys.Where(k => !groupIds.Contains(k.GroupId)).ToList())
            {
                lastSent.Remove(key);
            }

            if (groupIds.Count == 0)
            {
                continue;
            }

            var pricesByGroup = await engine.QueryAsync(
                e => groupIds.Select(g => (GroupId: g, Prices: e.GetPrices(g) ?? [])).ToList(),
                cancellationToken);

            foreach (var (groupId, prices) in pricesByGroup)
            {
                var changed = prices.Where(p => !lastSent.TryGetValue((groupId, p.Symbol), out var last) || last != p).ToList();
                if (changed.Count == 0)
                {
                    continue;
                }

                foreach (var price in changed)
                {
                    lastSent[(groupId, price.Symbol)] = price;
                }

                await SendAsync(hub.Clients.Group(RealtimeGroups.Prices(groupId)).Prices(changed));
            }
        }
    }

    private async Task PublishAccountsAsync(CancellationToken cancellationToken)
    {
        // Snapshots hold lists, so they are compared as JSON.
        var lastSent = new Dictionary<string, string>(StringComparer.Ordinal);
        using var timer = new PeriodicTimer(options.Value.AccountInterval, time);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var accountIds = subscriptions.Accounts();
            foreach (var accountId in lastSent.Keys.Where(k => !accountIds.Contains(k)).ToList())
            {
                lastSent.Remove(accountId);
            }

            if (accountIds.Count == 0)
            {
                continue;
            }

            var snapshots = await engine.QueryAsync(
                e => accountIds.Select(e.GetAccount).OfType<AccountSnapshot>().ToList(),
                cancellationToken);

            foreach (var snapshot in snapshots)
            {
                var json = JsonSerializer.Serialize(snapshot, SnapshotJson);
                if (lastSent.TryGetValue(snapshot.AccountId, out var last) && last == json)
                {
                    continue;
                }

                lastSent[snapshot.AccountId] = json;
                await SendAsync(hub.Clients.Group(RealtimeGroups.Account(snapshot.AccountId)).Account(snapshot));
            }
        }
    }

    // A failed send must not stop publishing to other clients.
    private async Task SendAsync(Task send)
    {
        try
        {
            await send;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogSendFailed(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to push a realtime message")]
    private static partial void LogSendFailed(ILogger logger, Exception exception);
}
