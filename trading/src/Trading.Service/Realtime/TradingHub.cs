using Microsoft.AspNetCore.SignalR;

using Trading.Engine;
using Trading.Service.Engine;
using Trading.Service.Identity;

namespace Trading.Service.Realtime;

/// <summary>Messages the server pushes to the trading terminal.</summary>
public interface ITradingClient
{
    /// <summary>Prices that changed for the account's group, after markup.</summary>
    Task Prices(IReadOnlyList<SymbolPrice> prices);

    /// <summary>The account valued at the latest prices. Sent when it changes, at most a few times per second.</summary>
    Task Account(AccountSnapshot account);

    /// <summary>New events for the account, as they happen.</summary>
    Task Events(IReadOnlyList<EventEnvelope> events);
}

/// <summary>Realtime connection for the trading terminal. Commands go through the REST API.</summary>
internal sealed class TradingHub(EngineHost engine, IUserStore users, SubscriptionRegistry subscriptions) : Hub<ITradingClient>
{
    /// <summary>Starts sending the account's state, events and prices to this connection. Only the owner may subscribe.</summary>
    public async Task Subscribe(string accountId)
    {
        var cancellationToken = Context.ConnectionAborted;
        if (Context.User is null || CurrentUser.IdOf(Context.User) is not { } userId || !await users.OwnsAsync(userId, accountId, cancellationToken))
        {
            throw new HubException("Unknown account.");
        }

        var snapshot = await engine.QueryAsync(e => e.GetAccount(accountId), cancellationToken)
            ?? throw new HubException("Unknown account.");

        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Account(accountId), cancellationToken);
        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Prices(snapshot.GroupId), cancellationToken);
        subscriptions.Add(Context.ConnectionId, accountId, snapshot.GroupId);

        await Clients.Caller.Account(snapshot);
        await Clients.Caller.Prices(await engine.QueryAsync(e => e.GetPrices(snapshot.GroupId) ?? [], cancellationToken));
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        subscriptions.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

internal static class RealtimeGroups
{
    public static string Account(string accountId) => $"account:{accountId}";

    public static string Prices(string groupId) => $"prices:{groupId}";
}
