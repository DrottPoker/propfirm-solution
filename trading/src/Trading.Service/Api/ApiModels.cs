using Trading.Engine;
using Trading.Service.Engine;

namespace Trading.Service.Api;

/// <summary>Places an order. The client creates the order id, so a retry can never place the order twice.</summary>
public sealed record PlaceOrderRequest(
    string OrderId,
    string Symbol,
    Side Side,
    OrderType Type,
    decimal Volume,
    decimal? Price = null,
    decimal? StopLoss = null,
    decimal? TakeProfit = null);

public sealed record ModifyStopsRequest(decimal? StopLoss, decimal? TakeProfit);

public sealed record CreateAccountRequest(string AccountId, string GroupId, decimal InitialBalance);

public sealed record SetFloorRequest(EquityFloorRule Rule);

/// <summary>The events a command caused, in order.</summary>
public sealed record CommandResponse(IReadOnlyList<EventEnvelope> Events);
