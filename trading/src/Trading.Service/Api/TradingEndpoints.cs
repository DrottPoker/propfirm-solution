using System.Security.Claims;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

using Trading.Engine;
using Trading.Engine.Inputs;
using Trading.Service.Candles;
using Trading.Service.Configuration;
using Trading.Service.Engine;
using Trading.Service.Identity;
using Trading.Service.Persistence;
using Trading.Service.Reports;

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
        account.MapGet("/instruments/{symbol}/point-value", GetPointValueAsync);
        account.MapGet("/market-hours", GetMarketHoursAsync);
        account.MapGet("/rules", GetRulesAsync);
        account.MapGet("/notice", GetNoticeAsync);
        account.MapGet("/positions/{positionId}/receipt", GetReceiptAsync);
        account.MapGet("/breach-report", GetBreachReportAsync);
        account.MapGet("/prices", GetPricesAsync);
        account.MapGet("/candles/{symbol}", GetCandlesAsync);
        account.MapGet("/events", GetEventsAsync);
        account.MapPost("/orders", PlaceOrderAsync);
        account.MapPut("/orders/{orderId}", ModifyOrderAsync);
        account.MapDelete("/orders/{orderId}", CancelOrderAsync);
        account.MapPost("/positions/close-all", CloseAllPositionsAsync);
        account.MapPost("/positions/{positionId}/close", ClosePositionAsync);
        account.MapPut("/positions/{positionId}/stops", ModifyStopsAsync);
        account.MapPut("/limits", SetOwnLimitsAsync);
        account.MapPost("/lock", LockTradingAsync);
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

    /// <summary>When each of the account's symbols can be traded, from now on. Orders, closes and stop changes are refused while a market is closed.</summary>
    private static async Task<Results<Ok<IReadOnlyList<MarketHours>>, NotFound>> GetMarketHoursAsync(
        string accountId,
        EngineHost engine,
        MarketCatalog catalog,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        await GroupConditionsOfAsync(engine, accountId, cancellationToken) is { } group
            ? TypedResults.Ok(catalog.MarketHoursFor(group, time.GetUtcNow()))
            : TypedResults.NotFound();

    /// <summary>
    /// The account's rules as the firm's system last told them: trading days, deadlines and the consistency rule. Every
    /// field is null when it has told none. Changes arrive in realtime as Rules (ADR 0052).
    /// </summary>
    private static async Task<Ok<AccountRules>> GetRulesAsync(string accountId, IUserStore users, CancellationToken cancellationToken) =>
        TypedResults.Ok(await users.AccountRulesOfAsync(accountId, cancellationToken) ?? AccountRules.None);

    /// <summary>
    /// The notice of the firm the account belongs to, for its terminals, such as an outage of the price feed, or null
    /// for none (ADR 0053). The trader belongs to the same firm as their accounts.
    /// </summary>
    private static async Task<Ok<NoticeResponse>> GetNoticeAsync(
        [FromRoute(Name = "accountId")] string _,
        ClaimsPrincipal principal,
        IUserStore users,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(new NoticeResponse(CurrentUser.TenantIdOf(principal) is { } tenantId ? await users.TenantNoticeOfAsync(tenantId, cancellationToken) : null));

    /// <summary>
    /// The receipt for one of the account's positions, open or closed (ADR 0053): its fills with the feed's prices
    /// behind them and the markup, and the prices around each fill.
    /// </summary>
    private static async Task<Results<Ok<TradeReceipt>, NotFound>> GetReceiptAsync(
        string accountId,
        string positionId,
        TradeReceipts receipts,
        CancellationToken cancellationToken) =>
        await receipts.BuildAsync(accountId, positionId, cancellationToken) is { } receipt ? TypedResults.Ok(receipt) : TypedResults.NotFound();

    /// <summary>Why the account's loss limit was broken (ADR 0053). Not found when no limit was broken.</summary>
    private static async Task<Results<Ok<BreachReport>, NotFound>> GetBreachReportAsync(string accountId, BreachReports reports, CancellationToken cancellationToken) =>
        await reports.BuildAsync(accountId, cancellationToken) is { } report ? TypedResults.Ok(report) : TypedResults.NotFound();

    // Not found also when the symbol has no conversion rate yet, which a price soon brings.
    private static async Task<Results<Ok<PointValue>, NotFound>> GetPointValueAsync(
        string accountId,
        string symbol,
        EngineHost engine,
        CancellationToken cancellationToken) =>
        await engine.QueryAsync(e => e.GetPointValue(accountId, symbol), cancellationToken) is { } value
            ? TypedResults.Ok(value)
            : TypedResults.NotFound();

    private static async Task<Results<Ok<IReadOnlyList<SymbolPrice>>, NotFound>> GetPricesAsync(string accountId, EngineHost engine, CancellationToken cancellationToken)
    {
        if (await GroupOfAsync(engine, accountId, cancellationToken) is not { } groupId)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await engine.QueryAsync(e => e.GetPrices(groupId) ?? [], cancellationToken));
    }

    /// <summary>
    /// Bid candles as the account's group sees them, oldest first: the latest, or with <c>before</c> the latest that start
    /// before that time, to scroll back. Waits until the history is loaded after a start.
    /// </summary>
    private static async Task<Results<Ok<IReadOnlyList<Candle>>, NotFound>> GetCandlesAsync(
        string accountId,
        string symbol,
        Timeframe timeframe,
        int? count,
        DateTimeOffset? before,
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

        await candles.Ready.WaitAsync(cancellationToken);
        var shift = -conditions.BidMarkupPoints * instrument.Point;
        return TypedResults.Ok(candles.Get(symbol, timeframe, Math.Clamp(count ?? DefaultCandles, 1, MaxCandles), shift, before));
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
            t => new PlaceOrder(
                t,
                accountId,
                request.OrderId,
                request.Symbol,
                request.Side,
                request.Type,
                request.Volume,
                request.Price,
                request.StopLoss,
                request.TakeProfit,
                request.TrailingStop),
            cancellationToken));

    /// <summary>Moves a pending order to a new price and sets its stops.</summary>
    private static async Task<Results<Ok<CommandResponse>, ProblemHttpResult>> ModifyOrderAsync(
        string accountId,
        string orderId,
        ModifyOrderRequest request,
        EngineHost engine,
        CancellationToken cancellationToken) =>
        CommandResults.From(await engine.SendAsync(
            t => new ModifyOrder(t, accountId, orderId, request.Price, request.StopLoss, request.TakeProfit, request.TrailingStop),
            cancellationToken));

    private static async Task<Results<Ok<CommandResponse>, ProblemHttpResult>> CancelOrderAsync(
        string accountId,
        string orderId,
        EngineHost engine,
        CancellationToken cancellationToken) =>
        CommandResults.From(await engine.SendAsync(t => new CancelOrder(t, accountId, orderId), cancellationToken));

    /// <summary>Closes the position, or with a volume only that part of it.</summary>
    private static async Task<Results<Ok<CommandResponse>, ProblemHttpResult>> ClosePositionAsync(
        string accountId,
        string positionId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ClosePositionRequest? request,
        EngineHost engine,
        CancellationToken cancellationToken) =>
        CommandResults.From(await engine.SendAsync(t => new ClosePosition(t, accountId, positionId, request?.Volume), cancellationToken));

    /// <summary>Closes every position, or those in a symbol, at the same moment. Positions whose market is closed stay open.</summary>
    private static async Task<Results<Ok<CommandResponse>, ProblemHttpResult>> CloseAllPositionsAsync(
        string accountId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CloseAllPositionsRequest? request,
        EngineHost engine,
        CancellationToken cancellationToken) =>
        CommandResults.From(await engine.SendAsync(t => new CloseAllPositions(t, accountId, request?.Symbol), cancellationToken));

    private static async Task<Results<Ok<CommandResponse>, ProblemHttpResult>> ModifyStopsAsync(
        string accountId,
        string positionId,
        ModifyStopsRequest request,
        EngineHost engine,
        CancellationToken cancellationToken) =>
        CommandResults.From(await engine.SendAsync(
            t => new ModifyPosition(t, accountId, positionId, request.StopLoss, request.TakeProfit, request.TrailingStop),
            cancellationToken));

    /// <summary>
    /// The trader's own limits (ADR 0054). Each limit that is stricter applies at once, and each that is looser or turned
    /// off from the next trading day. The account then shows both in <c>ownLimits</c>.
    /// </summary>
    private static async Task<Results<Ok<CommandResponse>, ProblemHttpResult>> SetOwnLimitsAsync(
        string accountId,
        OwnLimitsRequest request,
        EngineHost engine,
        CancellationToken cancellationToken) =>
        CommandResults.From(await engine.SendAsync(
            t => new SetOwnLimits(t, accountId, new OwnLimits(request.DailyLoss, request.DailyTarget, request.MaxTrades)),
            cancellationToken));

    /// <summary>
    /// Locks new orders until the next trading day, which nobody can undo (ADR 0054). An account that is already locked
    /// answers 200 without events, so a retry is safe.
    /// </summary>
    private static async Task<Results<Ok<CommandResponse>, ProblemHttpResult>> LockTradingAsync(
        string accountId,
        LockTradingRequest request,
        EngineHost engine,
        CancellationToken cancellationToken) =>
        CommandResults.From(await engine.SendAsync(t => new LockTrading(t, accountId, request.ClosePositions), cancellationToken), alreadyDone: RejectReason.AccountLocked);

    private static Task<string?> GroupOfAsync(EngineHost engine, string accountId, CancellationToken cancellationToken) =>
        engine.QueryAsync(e => e.GetGroupId(accountId), cancellationToken);

    // The engine knows every group, also those created while it runs.
    private static Task<TradingGroup?> GroupConditionsOfAsync(EngineHost engine, string accountId, CancellationToken cancellationToken) =>
        engine.QueryAsync(e => e.GetGroupId(accountId) is { } groupId ? e.GetGroup(groupId) : null, cancellationToken);
}
