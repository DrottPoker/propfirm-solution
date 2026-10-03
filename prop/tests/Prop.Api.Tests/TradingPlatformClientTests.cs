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
    private static readonly FirmTrading Firm = new("demo-firm", "dev-admin-key", "standard");

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
    public async Task EventsAreReadInOrderAndUnknownKindsOnlyMoveTheCursor()
    {
        var page = """
            {"events":[
              {"sequence":7,"event":{"kind":"AccountCreated","accountId":"A1","groupId":"standard","currency":"USD","balance":100000,"timestamp":"2026-10-05T08:00:00+00:00"}},
              {"sequence":9,"event":{"kind":"OrderPlaced","accountId":"A1","timestamp":"2026-10-05T08:01:00+00:00"}},
              {"sequence":12,"event":{"kind":"EquityFloorBreached","accountId":"A1","floorId":"daily","level":95000,"equity":94980.5,"prices":[],"positions":[],"timestamp":"2026-10-05T09:00:00+00:00"}}
            ],"cursor":12}
            """;
        var platform = new StubPlatform((HttpStatusCode.OK, page));

        var read = await Client(platform).ReadEventsAsync(Firm, 6, 500, 30, TestContext.Current.CancellationToken);

        Assert.Equal("GET api/admin/v1/events?after=6&limit=500&wait=30", platform.Requests[0]);
        Assert.Equal(12, read.Cursor);
        Assert.Collection(
            read.Events,
            e => Assert.Equal(100_000m, Assert.IsType<TradingAccountCreated>(e).Balance),
            e => Assert.IsType<TradingOtherEvent>(e),
            e => Assert.Equal(("daily", 95_000m, 94_980.5m), (Assert.IsType<TradingFloorBreached>(e).FloorId, ((TradingFloorBreached)e).Level, ((TradingFloorBreached)e).Equity)));
        Assert.Contains("\"positions\"", read.Events[2].Raw, StringComparison.Ordinal);
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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method} {request.RequestUri!.PathAndQuery.TrimStart('/')}");
            ApiKeys.Add(request.Headers.GetValues("X-Api-Key").SingleOrDefault());
            var (status, body) = answers[_next++];
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
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
    [InlineData("/api/admin/v1/events", "get")]
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
    [InlineData("AccountCreated", "balance")]
    [InlineData("PositionOpened", "balanceAfter")]
    [InlineData("PositionClosed", "balanceAfter")]
    [InlineData("EquityFloorSet", "level")]
    [InlineData("EquityFloorBreached", "floorId")]
    [InlineData("EquityFloorBreached", "level")]
    [InlineData("EquityFloorBreached", "equity")]
    [InlineData("EquityFloorRuleAnchoredFloor", "distance")]
    [InlineData("EquityFloorRuleAnchoredFloor", "anchor")]
    [InlineData("EquityFloorRuleTrailingFloor", "lockLevel")]
    public void EveryFieldTheClientUsesIsInTheContract(string schema, string property)
    {
        var schemas = Contract["components"]!["schemas"]!.AsObject();

        // Event types appear as the base type's variants, named after it.
        var found = schemas.Where(s => s.Key == schema || s.Key.EndsWith(schema, StringComparison.Ordinal))
            .Any(s => s.Value?["properties"]?[property] is not null);
        Assert.True(found, $"{schema}.{property} is not in the contract.");
    }

    private static string ContractPath([System.Runtime.CompilerServices.CallerFilePath] string callerPath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerPath)!, "..", "..", "..", "contracts", "trading", "trading-service.json"));
}
