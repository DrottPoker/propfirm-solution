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

/// <summary>Creates a trading account owned by a user of the same firm.</summary>
public sealed record CreateAccountRequest(string AccountId, string GroupId, decimal InitialBalance, Guid OwnerUserId);

public sealed record CreateUserRequest(string? Email, string? Password);

public sealed record UserResponse(Guid UserId, string Email);

public sealed record SetFloorRequest(EquityFloorRule Rule);

/// <summary>The events a command caused, in order.</summary>
public sealed record CommandResponse(IReadOnlyList<EventEnvelope> Events);
