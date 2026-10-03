using Microsoft.AspNetCore.Http.HttpResults;

using Trading.Engine;
using Trading.Engine.Inputs;
using Trading.Service.Candles;
using Trading.Service.Configuration;
using Trading.Service.Engine;
using Trading.Service.Identity;
using Trading.Service.Persistence;

namespace Trading.Service.Api;

/// <summary>The trader's API, scoped to one account the logged in trader owns. Used by the trading terminal.</summary>
internal static class TradingEndpoints
{
    private const int DefaultCandles = 500;
    private const int MaxCandles = CandleStore.DefaultCapacity;
    private const int DefaultEvents = 500;
    private const int MaxEvents = 1_000;

    public static IEndpointRouteBuilder MapTradingApi(this IEndpointRouteBuilder app)
    {
        var account = app.MapGroup("/api/accounts/{accountId}")
            .WithTags("Trading")
            .RequireAuthorization()
            .AddEndpointFilter<AccountOwnerFilter>();
        account.MapGet("", GetAccountAsync);
        account.MapGet("/instruments", GetInstrumentsAsync);
        account.MapGet("/prices", GetPricesAsync);
        account.MapGet("/candles/{symbol}", GetCandlesAsync);
        account.MapGet("/events", GetEventsAsync);
        account.MapPost("/orders", PlaceOrderAsync);
        account.MapDelete("/orders/{orderId}", CancelOrderAsync);
        account.MapPost("/positions/{positionId}/close", ClosePositionAsync);
        account.MapPut("/positions/{positionId}/stops", ModifyStopsAsync);
        return app;
    }

    private static async Task<Results<Ok<AccountSnapshot>, NotFound>> GetAccountAsync(string accountId, EngineHost engine, CancellationToken cancellationToken) =>
        await engine.QueryAsync(e => e.GetAccount(accountId), cancellationToken) is { } snapshot
            ? TypedResults.Ok(snapshot)
            : TypedResults.NotFound();

    private static async Task<Results<Ok<IReadOnlyList<InstrumentInfo>>, NotFound>> GetInstrumentsAsync(
        string accountId,
        EngineHost engine,
        MarketCatalog catalog,
        CancellationToken cancellationToken) =>
        await GroupConditionsOfAsync(engine, accountId, cancellationToken) is { } group
            ? TypedResults.Ok(catalog.InstrumentsFor(group))
            : TypedResults.NotFound();

    private static async Task<Results<Ok<IReadOnlyList<SymbolPrice>>, NotFound>> GetPricesAsync(string accountId, EngineHost engine, CancellationToken cancellationToken)
    {
        if (await GroupOfAsync(engine, accountId, cancellationToken) is not { } groupId)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await engine.QueryAsync(e => e.GetPrices(groupId) ?? [], cancellationToken));
    }

    /// <summary>Bid candles as the account's group sees them, oldest first.</summary>
    private static async Task<Results<Ok<IReadOnlyList<Candle>>, NotFound>> GetCandlesAsync(
        string accountId,
        string symbol,
        Timeframe timeframe,
        int? count,
        EngineHost engine,
        MarketCatalog catalog,
        CandleStore candles,
        CancellationToken cancellationToken)
    {
        if (await GroupConditionsOfAsync(engine, accountId, cancellationToken) is not { } group
            || !catalog.TryGet(group, symbol, out var instrument, out var conditions))
        {
            return TypedResults.NotFound();
        }

        var shift = -conditions.BidMarkupPoints * instrument.Point;
        return TypedResults.Ok(candles.Get(symbol, timeframe, Math.Clamp(count ?? DefaultCandles, 1, MaxCandles), shift));
    }

    /// <summary>
    /// The account's events, oldest first. Without a cursor the latest ones. With <c>after</c> the first ones after
    /// that sequence number, to catch up. With <c>before</c> the last ones before it, to page back.
    /// </summary>
    private static async Task<Results<Ok<IReadOnlyList<EventEnvelope>>, NotFound, ProblemHttpResult>> GetEventsAsync(
        string accountId,
        long? after,
        long? before,
        int? limit,
        EngineHost engine,
        IEngineJournal journal,
        CancellationToken cancellationToken)
    {
        if (after is not null && before is not null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Use after or before, not both.");
        }

        if (await GroupOfAsync(engine, accountId, cancellationToken) is null)
        {
            return TypedResults.NotFound();
        }

        var count = Math.Clamp(limit ?? DefaultEvents, 1, MaxEvents);
        return TypedResults.Ok(after is { } afterSequence
            ? await journal.ReadEventsAsync(accountId, afterSequence, count, cancellationToken)
            : await journal.ReadEventsBeforeAsync(accountId, before ?? long.MaxValue, count, cancellationToken));
    }

    private static async Task<Results<Ok<CommandResponse>, ProblemHttpResult>> PlaceOrderAsync(
        string accountId,
        PlaceOrderRequest request,
        EngineHost engine,
        CancellationToken cancellationToken) =>
        CommandResults.From(await engine.SendAsync(
            t => new PlaceOrder(t, accountId, request.OrderId, request.Symbol, request.Side, request.Type, request.Volume, request.Price, request.StopLoss, request.TakeProfit),
            cancellationToken));

    private static async Task<Results<Ok<CommandResponse>, ProblemHttpResult>> CancelOrderAsync(
        string accountId,
        string orderId,
        EngineHost engine,
        CancellationToken cancellationToken) =>
        CommandResults.From(await engine.SendAsync(t => new CancelOrder(t, accountId, orderId), cancellationToken));

    private static async Task<Results<Ok<CommandResponse>, ProblemHttpResult>> ClosePositionAsync(
        string accountId,
        string positionId,
        EngineHost engine,
        CancellationToken cancellationToken) =>
        CommandResults.From(await engine.SendAsync(t => new ClosePosition(t, accountId, positionId), cancellationToken));

    private static async Task<Results<Ok<CommandResponse>, ProblemHttpResult>> ModifyStopsAsync(
        string accountId,
        string positionId,
        ModifyStopsRequest request,
        EngineHost engine,
        CancellationToken cancellationToken) =>
        CommandResults.From(await engine.SendAsync(
            t => new ModifyPosition(t, accountId, positionId, request.StopLoss, request.TakeProfit),
            cancellationToken));

    private static Task<string?> GroupOfAsync(EngineHost engine, string accountId, CancellationToken cancellationToken) =>
        engine.QueryAsync(e => e.GetGroupId(accountId), cancellationToken);

    // The engine knows every group, also those created while it runs.
    private static Task<TradingGroup?> GroupConditionsOfAsync(EngineHost engine, string accountId, CancellationToken cancellationToken) =>
        engine.QueryAsync(e => e.GetGroupId(accountId) is { } groupId ? e.GetGroup(groupId) : null, cancellationToken);
}
