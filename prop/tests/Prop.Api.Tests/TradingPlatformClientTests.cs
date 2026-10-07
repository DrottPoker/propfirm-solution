using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Prop.Api.Firms;
using Prop.Api.Trading;
using Prop.Rules;

namespace Prop.Api.Tests;

/// <summary>The HTTP client for our trading platform's admin API, against answers like the platform's.</summary>
public sealed class TradingPlatformClientTests
{
    private static readonly FirmTrading Firm = new("demo-firm", "dev-admin-key", "standard", "USD");

    [Fact]
    public async Task OpeningAnAccountAgainIsDoneWhenTheFirmAlreadyHasIt()
    {
        var platform = new StubPlatform((HttpStatusCode.Conflict, """{"status":409,"reason":"DuplicateId"}"""), (HttpStatusCode.OK, "{}"));

        await Client(platform).OpenAccountAsync(Firm, "demo-firm-1001-1", 100_000m, Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(["POST api/admin/v1/accounts", "GET api/admin/v1/accounts/demo-firm-1001-1"], platform.Requests);
        Assert.All(platform.ApiKeys, key => Assert.Equal("dev-admin-key", key));
    }

    [Fact]
    public async Task AnAccountIdTakenOutsideTheFirmIsRefused()
    {
        var platform = new StubPlatform((HttpStatusCode.Conflict, "{}"), (HttpStatusCode.NotFound, "{}"));

        await Assert.ThrowsAsync<TradingPlatformRejectedException>(
            () => Client(platform).OpenAccountAsync(Firm, "demo-firm-1001-1", 100_000m, Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, typeof(TradingPlatformUnavailableException))]
    [InlineData(HttpStatusCode.TooManyRequests, typeof(TradingPlatformUnavailableException))]
    [InlineData(HttpStatusCode.NotFound, typeof(TradingPlatformRejectedException))]
    public async Task ServerTroubleCanBeTriedAgainButRefusalsCannot(HttpStatusCode status, Type expected)
    {
        var platform = new StubPlatform((status, """{"status":0}"""));

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => Client(platform).CloseAccountAsync(Firm, "A1", TestContext.Current.CancellationToken));

        Assert.IsType(expected, exception);
    }

    // A disabled account has nothing left to protect, so a floor or a close for it is done.
    [Fact]
    public async Task FloorsAndClosesOnADisabledAccountAreDone()
    {
        const string disabled = """{"status":422,"reason":"AccountDisabled"}""";
        var platform = new StubPlatform((HttpStatusCode.UnprocessableEntity, disabled), (HttpStatusCode.UnprocessableEntity, disabled));
        var client = Client(platform);

        await client.SetFloorAsync(Firm, "A1", "daily", new StartOfDayFloor(5_000m, DailyLossReference.Balance), TestContext.Current.CancellationToken);
        await client.CloseAccountAsync(Firm, "A1", TestContext.Current.CancellationToken);
    }

    // While a firm's month is unpaid, its accounts take no new positions.
    [Fact]
    public async Task AccountsAreSuspendedAndResumedAndADisabledOneNeedsNeither()
    {
        const string disabled = """{"status":422,"reason":"AccountDisabled"}""";
        var platform = new StubPlatform((HttpStatusCode.OK, """{"events":[]}"""), (HttpStatusCode.OK, """{"events":[]}"""), (HttpStatusCode.UnprocessableEntity, disabled));
        var client = Client(platform);

        await client.SuspendAccountAsync(Firm, "A1", TestContext.Current.CancellationToken);
        await client.ResumeAccountAsync(Firm, "A1", TestContext.Current.CancellationToken);
        await client.SuspendAccountAsync(Firm, "A2", TestContext.Current.CancellationToken);

        Assert.Equal(["POST api/admin/v1/accounts/A1/suspend", "POST api/admin/v1/accounts/A1/resume", "POST api/admin/v1/accounts/A2/suspend"], platform.Requests);
    }

    [Fact]
    public async Task AWithdrawalIsANegativeBalanceOperationKeepingTheMinimumBalance()
    {
        var platform = new StubPlatform((HttpStatusCode.OK, """{"events":[]}"""));

        await Client(platform).WithdrawAsync(Firm, "A1", "payout-1", 8_000m, 100_000m, TestContext.Current.CancellationToken);

        Assert.Equal(["POST api/admin/v1/accounts/A1/balance-operations"], platform.Requests);
        Assert.Equal("""{"operationId":"payout-1","amount":-8000,"minBalance":100000}""", platform.Bodies[0]);
    }

    // The first try went through, but its answer was lost.
    [Fact]
    public async Task AWithdrawalAlreadyAppliedIsDone()
    {
        var platform = new StubPlatform((HttpStatusCode.Conflict, """{"status":409,"reason":"DuplicateId"}"""));

        await Client(platform).WithdrawAsync(Firm, "A1", "payout-1", 8_000m, 100_000m, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ARefusedWithdrawalCarriesThePlatformsReason()
    {
        var platform = new StubPlatform((HttpStatusCode.UnprocessableEntity, """{"status":422,"reason":"InsufficientFunds"}"""));

        var refused = await Assert.ThrowsAsync<TradingPlatformRejectedException>(
            () => Client(platform).WithdrawAsync(Firm, "A1", "payout-1", 8_000m, 100_000m, TestContext.Current.CancellationToken));

        Assert.Equal("InsufficientFunds", refused.Reason);
    }

    [Fact]
    public async Task AUserIsFoundOrCreated()
    {
        var id = Guid.NewGuid();
        var platform = new StubPlatform((HttpStatusCode.NotFound, "{}"), (HttpStatusCode.OK, $$"""{"userId":"{{id}}","email":"a@test.example"}"""));

        var userId = await Client(platform).EnsureUserAsync(Firm, "a@test.example", "random-password", TestContext.Current.CancellationToken);

        Assert.Equal(id, userId);
        Assert.Equal(["GET api/admin/v1/users?email=a%40test.example", "POST api/admin/v1/users"], platform.Requests);
    }

    [Fact]
    public async Task AnAccountIsValuedWithItsFloors()
    {
        var platform = new StubPlatform(
            (HttpStatusCode.OK, """{"accountId":"A1","balance":100000,"equity":99250.5,"floors":[{"floorId":"daily","level":95000,"headroom":4250.5}]}"""),
            (HttpStatusCode.NotFound, "{}"));
        var client = Client(platform);

        var account = await client.GetAccountAsync(Firm, "A1", TestContext.Current.CancellationToken);
        var missing = await client.GetAccountAsync(Firm, "A2", TestContext.Current.CancellationToken);

        Assert.NotNull(account);
        Assert.Equal(100_000m, account.Balance);
        Assert.Equal(99_250.5m, account.Equity);
        Assert.Equal(new TradingFloorSnapshot("daily", 95_000m, 4_250.5m), Assert.Single(account.Floors));
        Assert.Null(missing);
        Assert.Equal(["GET api/admin/v1/accounts/A1", "GET api/admin/v1/accounts/A2"], platform.Requests);
    }

    [Fact]
    public async Task EventsAreReadInOrderAndUnknownKindsOnlyMoveTheCursor()
    {
        var page = """
            {"events":[
              {"sequence":7,"event":{"kind":"AccountCreated","accountId":"A1","groupId":"standard","currency":"USD","balance":100000,"timestamp":"2026-10-05T08:00:00+00:00"}},
              {"sequence":9,"event":{"kind":"OrderPlaced","accountId":"A1","timestamp":"2026-10-05T08:01:00+00:00"}},
              {"sequence":10,"event":{"kind":"PositionOpened","accountId":"A1","positionId":"p-1","symbol":"XAUUSD","side":"Sell","volume":0.2,"openPrice":2412.5,"stopLoss":null,"takeProfit":null,"commission":3.5,"balanceAfter":99996.5,"timestamp":"2026-10-05T08:02:00+00:00"}},
              {"sequence":11,"event":{"kind":"PositionClosed","accountId":"A1","positionId":"p-1","symbol":"XAUUSD","side":"Sell","volume":0.2,"openPrice":2412.5,"closePrice":2398.2,"profit":286,"commission":3.5,"reason":"TakeProfit","balanceAfter":100279,"timestamp":"2026-10-05T08:30:00+00:00"}},
              {"sequence":12,"event":{"kind":"EquityFloorBreached","accountId":"A1","floorId":"daily","level":95000,"equity":94980.5,"prices":[],"positions":[],"timestamp":"2026-10-05T09:00:00+00:00"}},
              {"sequence":13,"event":{"kind":"BalanceAdjusted","accountId":"A1","operationId":"payout-1","amount":-8000,"balanceAfter":100000,"timestamp":"2026-10-05T10:00:00+00:00"}}
            ],"cursor":13}
            """;
        var platform = new StubPlatform((HttpStatusCode.OK, page));

        var read = await Client(platform).ReadEventsAsync(Firm, 6, 500, 30, TestContext.Current.CancellationToken);

        Assert.Equal("GET api/admin/v1/events?after=6&limit=500&wait=30", platform.Requests[0]);
        Assert.Equal(13, read.Cursor);
        Assert.Collection(
            read.Events,
            e => Assert.Equal(100_000m, Assert.IsType<TradingAccountCreated>(e).Balance),
            e => Assert.IsType<TradingOtherEvent>(e),
            e => Assert.Equal(
                new TradingPositionOpened(10, new DateTimeOffset(2026, 10, 5, 8, 2, 0, TimeSpan.Zero), "A1", e.Raw, "p-1", "XAUUSD", TradeSide.Sell, 0.2m, 2_412.5m, 3.5m, 99_996.5m),
                e),
            e => Assert.Equal(
                new TradingPositionClosed(
                    11, new DateTimeOffset(2026, 10, 5, 8, 30, 0, TimeSpan.Zero), "A1", e.Raw, "p-1", "XAUUSD", TradeSide.Sell, 0.2m, 2_412.5m, 2_398.2m, 286m, 3.5m, "TakeProfit", 100_279m),
                e),
            e => Assert.Equal(("daily", 95_000m, 94_980.5m), (Assert.IsType<TradingFloorBreached>(e).FloorId, ((TradingFloorBreached)e).Level, ((TradingFloorBreached)e).Equity)),
            e => Assert.Equal(("payout-1", -8_000m, 100_000m), (Assert.IsType<TradingBalanceAdjusted>(e).OperationId, ((TradingBalanceAdjusted)e).Amount, ((TradingBalanceAdjusted)e).BalanceAfter)));
        Assert.Contains("\"positions\"", read.Events[4].Raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APartOfAPositionIsReadWithWhatStaysOpen()
    {
        var page = """
            {"events":[
              {"sequence":5,"event":{"kind":"PositionPartiallyClosed","accountId":"A1","positionId":"p-1","symbol":"EURUSD","side":"Buy","volume":0.4,"remainingVolume":0.6,"openPrice":1.08,"closePrice":1.082,"profit":80,"commission":1.4,"reason":"Manual","balanceAfter":100075.1,"timestamp":"2026-10-05T08:30:00+00:00"}}
            ],"cursor":5}
            """;

        var read = await Client(new StubPlatform((HttpStatusCode.OK, page))).ReadEventsAsync(Firm, 4, 500, 30, TestContext.Current.CancellationToken);

        var e = Assert.Single(read.Events);
        Assert.Equal(new TradingPositionPartiallyClosed(5, new DateTimeOffset(2026, 10, 5, 8, 30, 0, TimeSpan.Zero), "A1", e.Raw, "p-1", 0.4m, 0.6m, 1.082m, 80m, 1.4m, 100_075.1m), e);
    }

    [Fact]
    public void TheRuleEnginesFloorsBecomeTheTradingPlatformsRules()
    {
        Assert.Equal(
            """{"kind":"AnchoredFloor","distance":5000,"anchor":"HigherOfBalanceAndEquity"}""",
            TradingPlatformClient.ToTradingRule(new StartOfDayFloor(5_000m, DailyLossReference.HigherOfBalanceAndEquity)).ToJsonString());
        Assert.Equal("""{"kind":"FixedFloor","level":90000}""", TradingPlatformClient.ToTradingRule(new FixedFloor(90_000m)).ToJsonString());
        Assert.Equal(
            """{"kind":"TrailingFloor","distance":10000,"lockLevel":100000}""",
            TradingPlatformClient.ToTradingRule(new TrailingFloor(10_000m, 100_000m)).ToJsonString());
    }

    private static TradingPlatformClient Client(StubPlatform platform) => new(new Factory(platform));

    private sealed class Factory(StubPlatform platform) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(platform, disposeHandler: false) { BaseAddress = new Uri("https://trading.test/") };
    }

    /// <summary>Answers in turn with the given status codes and bodies, and records what was asked.</summary>
    private sealed class StubPlatform(params (HttpStatusCode Status, string Body)[] answers) : HttpMessageHandler
    {
        private int _next;

        public List<string> Requests { get; } = [];

        public List<string?> ApiKeys { get; } = [];

        public List<string?> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method} {request.RequestUri!.PathAndQuery.TrimStart('/')}");
            ApiKeys.Add(request.Headers.GetValues("X-Api-Key").SingleOrDefault());
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            var (status, body) = answers[_next++];
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}

/// <summary>
/// Everything the client uses must be in the trading platform's contract, so a change on either side that
/// breaks the other fails here first (ADR 0012).
/// </summary>
public sealed class TradingContractTests
{
    private static readonly JsonNode Contract = JsonNode.Parse(File.ReadAllText(ContractPath()))!;

    [Theory]
    [InlineData("/api/admin/v1/users", "get")]
    [InlineData("/api/admin/v1/users", "post")]
    [InlineData("/api/admin/v1/users/{userId}/login-links", "post")]
    [InlineData("/api/admin/v1/accounts", "post")]
    [InlineData("/api/admin/v1/accounts/{accountId}", "get")]
    [InlineData("/api/admin/v1/accounts/{accountId}/floors/{floorId}", "put")]
    [InlineData("/api/admin/v1/accounts/{accountId}/close", "post")]
    [InlineData("/api/admin/v1/accounts/{accountId}/suspend", "post")]
    [InlineData("/api/admin/v1/accounts/{accountId}/resume", "post")]
    [InlineData("/api/admin/v1/accounts/{accountId}/balance-operations", "post")]
    [InlineData("/api/admin/v1/accounts/{accountId}/rules", "put")]
    [InlineData("/api/admin/v1/events", "get")]
    [InlineData("/api/admin/v1/accounts/{accountId}/reopen", "post")]
    [InlineData("/api/admin/v1/accounts/{accountId}/positions/{positionId}/receipt", "get")]
    [InlineData("/api/admin/v1/accounts/{accountId}/breach-report", "get")]
    [InlineData("/api/admin/v1/impact", "get")]
    [InlineData("/api/admin/v1/notice", "put")]
    [InlineData("/api/admin/v1/notice", "delete")]
    [InlineData("/api/partner/v1/price-feed", "get")]
    public void EveryPathTheClientUsesIsInTheContract(string path, string method)
    {
        Assert.NotNull(Contract["paths"]?[path]?[method]);
    }

    [Theory]
    [InlineData("UserResponse", "userId")]
    [InlineData("LoginLinkResponse", "url")]
    [InlineData("LoginLinkResponse", "expiresAt")]
    [InlineData("FirmEventsResponse", "events")]
    [InlineData("FirmEventsResponse", "cursor")]
    [InlineData("EventEnvelope", "sequence")]
    [InlineData("EventEnvelope", "event")]
    [InlineData("CreateAccountRequest", "accountId")]
    [InlineData("CreateAccountRequest", "groupId")]
    [InlineData("CreateAccountRequest", "initialBalance")]
    [InlineData("CreateAccountRequest", "ownerUserId")]
    [InlineData("AccountSnapshot", "balance")]
    [InlineData("AccountSnapshot", "equity")]
    [InlineData("AccountSnapshot", "floors")]
    [InlineData("FloorSnapshot", "floorId")]
    [InlineData("FloorSnapshot", "level")]
    [InlineData("FloorSnapshot", "headroom")]
    [InlineData("AccountCreated", "balance")]
    [InlineData("PositionOpened", "positionId")]
    [InlineData("PositionOpened", "symbol")]
    [InlineData("PositionOpened", "side")]
    [InlineData("PositionOpened", "volume")]
    [InlineData("PositionOpened", "openPrice")]
    [InlineData("PositionOpened", "commission")]
    [InlineData("PositionOpened", "balanceAfter")]
    [InlineData("PositionClosed", "positionId")]
    [InlineData("PositionClosed", "symbol")]
    [InlineData("PositionClosed", "side")]
    [InlineData("PositionClosed", "volume")]
    [InlineData("PositionClosed", "openPrice")]
    [InlineData("PositionClosed", "closePrice")]
    [InlineData("PositionClosed", "profit")]
    [InlineData("PositionClosed", "commission")]
    [InlineData("PositionClosed", "reason")]
    [InlineData("PositionClosed", "balanceAfter")]
    [InlineData("PositionPartiallyClosed", "positionId")]
    [InlineData("PositionPartiallyClosed", "volume")]
    [InlineData("PositionPartiallyClosed", "remainingVolume")]
    [InlineData("PositionPartiallyClosed", "closePrice")]
    [InlineData("PositionPartiallyClosed", "profit")]
    [InlineData("PositionPartiallyClosed", "commission")]
    [InlineData("PositionPartiallyClosed", "balanceAfter")]
    [InlineData("EquityFloorSet", "level")]
    [InlineData("EquityFloorBreached", "floorId")]
    [InlineData("EquityFloorBreached", "level")]
    [InlineData("EquityFloorBreached", "equity")]
    [InlineData("EquityFloorRuleAnchoredFloor", "distance")]
    [InlineData("EquityFloorRuleAnchoredFloor", "anchor")]
    [InlineData("EquityFloorRuleTrailingFloor", "lockLevel")]
    [InlineData("BalanceOperationRequest", "operationId")]
    [InlineData("BalanceOperationRequest", "amount")]
    [InlineData("BalanceOperationRequest", "minBalance")]
    [InlineData("BalanceAdjusted", "operationId")]
    [InlineData("BalanceAdjusted", "amount")]
    [InlineData("BalanceAdjusted", "balanceAfter")]
    [InlineData("AccountRulesRequest", "funded")]
    [InlineData("AccountRulesRequest", "tradingDaysRequired")]
    [InlineData("AccountRulesRequest", "tradingDaysCounted")]
    [InlineData("AccountRulesRequest", "passBy")]
    [InlineData("AccountRulesRequest", "openPositionBy")]
    [InlineData("AccountRulesRequest", "consistencyPercent")]
    [InlineData("AccountRulesRequest", "bestDayPercent")]
    [InlineData("ReopenAccountRequest", "balance")]
    [InlineData("AccountReopened", "balance")]
    [InlineData("TerminalNoticeRequest", "title")]
    [InlineData("TerminalNoticeRequest", "text")]
    [InlineData("TerminalNoticeRequest", "level")]
    [InlineData("TerminalNoticeRequest", "url")]
    [InlineData("TradeReceipt", "positionId")]
    [InlineData("TradeReceipt", "digits")]
    [InlineData("TradeReceipt", "order")]
    [InlineData("TradeReceipt", "opened")]
    [InlineData("TradeReceipt", "closes")]
    [InlineData("TradeReceipt", "result")]
    [InlineData("ReceiptFill", "feed")]
    [InlineData("ReceiptFill", "markupPoints")]
    [InlineData("ReceiptFill", "prices")]
    [InlineData("ReceiptClose", "fill")]
    [InlineData("ReceiptClose", "reason")]
    [InlineData("ReceiptClose", "level")]
    [InlineData("FeedPrice", "inputSequence")]
    [InlineData("FeedPrice", "receivedAt")]
    [InlineData("BreachReport", "balanceAfter")]
    [InlineData("BreachReport", "prices")]
    [InlineData("BreachReport", "positions")]
    [InlineData("BreachReport", "equityCurve")]
    [InlineData("BreachReport", "gaps")]
    [InlineData("BreachReport", "events")]
    [InlineData("BreachPrice", "bidMarkupPoints")]
    [InlineData("BreachPrice", "askMarkupPoints")]
    [InlineData("IncidentImpact", "accounts")]
    [InlineData("AccountImpact", "balanceAtStart")]
    [InlineData("AccountImpact", "equityAtStart")]
    [InlineData("AccountImpact", "ordersRefused")]
    [InlineData("AccountImpact", "closesRefused")]
    [InlineData("AccountImpact", "changesRefused")]
    [InlineData("AccountImpact", "breach")]
    [InlineData("ImpactBreach", "floorId")]
    [InlineData("PriceFeedStatus", "lastPriceAt")]
    [InlineData("PriceFeedStatus", "symbols")]
    [InlineData("SymbolFeedStatus", "marketOpen")]
    public void EveryFieldTheClientUsesIsInTheContract(string schema, string property)
    {
        var schemas = Contract["components"]!["schemas"]!.AsObject();

        // Event types appear as the base type's variants, named after it.
        var found = schemas.Where(s => s.Key == schema || s.Key.EndsWith(schema, StringComparison.Ordinal))
            .Any(s => s.Value?["properties"]?[property] is not null);
        Assert.True(found, $"{schema}.{property} is not in the contract.");
    }

    // Copied next to the tests by the project file. The source path is unknown in CI builds, which map it to /_/.
    private static string ContractPath() => Path.Combine(AppContext.BaseDirectory, "Contracts", "trading-service.json");
}
