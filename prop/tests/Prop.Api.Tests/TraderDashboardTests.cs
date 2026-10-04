using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Npgsql;

using Prop.Api.Tests.Support;
using Prop.Api.Trading;

namespace Prop.Api.Tests;

/// <summary>
/// The trader's dashboard in the portal: every account with its stages and results, and the trading history behind
/// the balance chart, the days, the statistics and the closed positions (ADR 0022).
/// </summary>
public sealed class TraderDashboardTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    // The development challenge: 10 % and 5 % targets, 4 trading days, 5 % daily and 10 % max loss.
    private const string Phase1 = "demo-firm-1001-1";

    [Fact]
    public async Task EveryTradeIsKeptWithItsCommissionsAndTheBalanceAfterIt()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var id = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        using var portal = await factory.LogInAsTraderAsync(id);

        factory.Trading.OpenPosition(Phase1, "XAUUSD", TradeSide.Sell, volume: 0.2m, openPrice: 2_412.5m, commission: 3.5m);
        factory.Trading.ClosePosition(Phase1, 286m, closePrice: 2_398.2m, commission: 3.5m, reason: "TakeProfit");

        // The rule engine counts the trading day while the history records the trade, each reading the stream on its own.
        await factory.WaitForAccountAsync(id, a => a.GetProperty("tradingDays").GetInt32() == 1 && a.GetProperty("balance").GetDecimal() == 100_279m);
        var performance = await PerformanceAsync(portal, id, p => p.GetProperty("statistics").GetProperty("trades").GetInt32() == 1);
        var trades = await portal.GetFromJsonAsync<JsonElement>(Url($"accounts/{id}/trades"), TestContext.Current.CancellationToken);

        Assert.Equal((0, "Phase 1", Phase1), (performance.GetProperty("stage").GetInt32(), performance.GetProperty("stageName").GetString(), performance.GetProperty("tradingAccountId").GetString()));
        Assert.Equal((100_000m, 110_000m), (performance.GetProperty("initialBalance").GetDecimal(), performance.GetProperty("profitTarget").GetDecimal()));
        Assert.Equal(
            [("Created", 100_000m, 100_000m), ("Opened", -3.5m, 99_996.5m), ("Closed", 282.5m, 100_279m)],
            performance.GetProperty("balance").EnumerateArray().Select(b => (b.GetProperty("kind").GetString(), b.GetProperty("change").GetDecimal(), b.GetProperty("balance").GetDecimal())));
        Assert.Equal([95_000m], performance.GetProperty("dailyFloor").EnumerateArray().Select(f => f.GetProperty("level").GetDecimal()));
        Assert.Equal([90_000m], performance.GetProperty("maxLossFloor").EnumerateArray().Select(f => f.GetProperty("level").GetDecimal()));

        var statistics = performance.GetProperty("statistics");
        Assert.Equal((1, 1, 0), (statistics.GetProperty("trades").GetInt32(), statistics.GetProperty("wins").GetInt32(), statistics.GetProperty("losses").GetInt32()));
        Assert.Equal((100m, 279m, 0.2m, 7m), (statistics.GetProperty("winRatePercent").GetDecimal(), statistics.GetProperty("averageWin").GetDecimal(), statistics.GetProperty("lots").GetDecimal(), statistics.GetProperty("commission").GetDecimal()));
        Assert.Equal(JsonValueKind.Null, statistics.GetProperty("profitFactor").ValueKind);

        var day = Assert.Single(performance.GetProperty("days").EnumerateArray());
        Assert.Equal(("2026-10-05", 1, 0.2m, 279m, true), (day.GetProperty("day").GetString(), day.GetProperty("trades").GetInt32(), day.GetProperty("lots").GetDecimal(), day.GetProperty("result").GetDecimal(), day.GetProperty("counted").GetBoolean()));

        var trade = Assert.Single(trades.GetProperty("trades").EnumerateArray());
        Assert.Equal(("XAUUSD", "Sell", 0.2m, 2_412.5m, 2_398.2m), (trade.GetProperty("symbol").GetString(), trade.GetProperty("side").GetString(), trade.GetProperty("volume").GetDecimal(), trade.GetProperty("openPrice").GetDecimal(), trade.GetProperty("closePrice").GetDecimal()));
        Assert.Equal((286m, 7m, 279m, "TakeProfit"), (trade.GetProperty("profit").GetDecimal(), trade.GetProperty("commission").GetDecimal(), trade.GetProperty("result").GetDecimal(), trade.GetProperty("closeReason").GetString()));
        Assert.Equal(JsonValueKind.String, trade.GetProperty("openedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, trades.GetProperty("next").ValueKind);
    }

    [Fact]
    public async Task TheHistoryIsBuiltAgainByReadingTheStreamFromTheStart()
    {
        var database = await postgres.CreateDatabaseAsync();
        PropFactory restarted;
        Guid id;
        string before;
        await using (var factory = PropFactory.Create(database))
        {
            id = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
            using var portal = await factory.LogInAsTraderAsync(id);
            factory.Trading.OpenPosition(Phase1, commission: 2m);
            factory.Trading.ClosePosition(Phase1, 500m, commission: 2m);
            factory.Trading.OpenPosition(Phase1);
            await factory.WaitForAccountAsync(id, a => a.GetProperty("tradingDays").GetInt32() == 1 && a.GetProperty("openPositions").GetInt32() == 1);
            before = (await PerformanceAsync(portal, id, p => p.GetProperty("balance").GetArrayLength() == 4)).GetRawText();
            restarted = factory.Restart();
        }

        // As for an account that traded before the history existed: nothing is kept, and nothing has been read.
        await ExecuteAsync(
            database,
            "delete from trading_positions; delete from trading_balance_changes; delete from trading_floor_levels; delete from trading_history_cursors");

        await using (restarted)
        {
            using var portal = await restarted.LogInAsTraderAsync(id);
            var after = await PerformanceAsync(portal, id, p => p.GetProperty("balance").GetArrayLength() == 4);

            Assert.Equal(before, after.GetRawText());
        }
    }

    [Fact]
    public async Task TheDashboardShowsEachAccountWithItsStagesAndResults()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var id = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        factory.Trading.OpenPosition(Phase1);
        factory.Trading.ClosePosition(Phase1, 2_000m);
        await factory.WaitForAccountAsync(id, a => a.GetProperty("balance").GetDecimal() == 102_000m);

        // The next day starts at 102 000. 500 is made, and the open positions are worth 300 more. The trader logs in
        // then, since a session ends after 12 hours unused.
        await NextTradingDayAsync(factory);
        await factory.WaitForAccountAsync(id, a => a.GetProperty("dailyFloor").GetDecimal() == 97_000m);
        using var portal = await factory.LogInAsTraderAsync(id);
        factory.Trading.OpenPosition(Phase1);
        factory.Trading.ClosePosition(Phase1, 500m);
        factory.Trading.OpenPosition(Phase1);
        factory.Trading.SetEquity(Phase1, 102_800m);
        await factory.WaitForAccountAsync(id, a => a.GetProperty("tradingDays").GetInt32() == 2 && a.GetProperty("openPositions").GetInt32() == 1);
        var account = await DashboardAccountAsync(portal, a => a.GetProperty("results").GetProperty("today").ValueKind == JsonValueKind.Number
            && a.GetProperty("results").GetProperty("today").GetDecimal() == 800m);

        Assert.Equal("Two-step 100000 USD", account.GetProperty("challenge").GetProperty("name").GetString());
        Assert.Equal(
            [("Phase 1", "Current", 2), ("Phase 2", "Upcoming", -1), ("Funded", "Upcoming", -1)],
            account.GetProperty("stages").EnumerateArray().Select(s => (
                s.GetProperty("name").GetString(),
                s.GetProperty("progress").GetString(),
                s.GetProperty("tradingDays").ValueKind == JsonValueKind.Number ? s.GetProperty("tradingDays").GetInt32() : -1)));

        var results = account.GetProperty("results");
        Assert.Equal((102_500m, 102_800m, 300m), (results.GetProperty("balance").GetDecimal(), results.GetProperty("equity").GetDecimal(), results.GetProperty("floating").GetDecimal()));
        Assert.Equal((2_800m, 2.8m), (results.GetProperty("stageResult").GetDecimal(), results.GetProperty("stageResultPercent").GetDecimal()));
        Assert.Equal((800m, 102_000m), (results.GetProperty("today").GetDecimal(), results.GetProperty("dayStartBalance").GetDecimal()));
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 22, 0, 0, TimeSpan.Zero), results.GetProperty("dayStartedAt").GetDateTimeOffset());
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 22, 0, 0, TimeSpan.Zero), results.GetProperty("nextDayStartsAt").GetDateTimeOffset());
        Assert.Equal((2_500m, 10_000m, 25m), (results.GetProperty("targetGained").GetDecimal(), results.GetProperty("targetRequired").GetDecimal(), results.GetProperty("targetPercent").GetDecimal()));
        Assert.Equal(0m, results.GetProperty("paidOut").GetDecimal());

        var distances = account.GetProperty("live").GetProperty("floors").EnumerateArray()
            .ToDictionary(f => f.GetProperty("floorId").GetString()!, f => f.GetProperty("distance").GetDecimal());
        Assert.Equal(new Dictionary<string, decimal> { ["daily"] = 5_000m, ["max-loss"] = 10_000m }, distances);
    }

    [Fact]
    public async Task TheHistoryVersionChangesWithTheHistory()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var id = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        using var portal = await factory.LogInAsTraderAsync(id);
        await PerformanceAsync(portal, id, p => p.GetProperty("balance").GetArrayLength() == 1 && p.GetProperty("dailyFloor").GetArrayLength() == 1);
        var first = await VersionAsync(portal, id);

        factory.Trading.OpenPosition(Phase1);
        await factory.WaitForAccountAsync(id, a => a.GetProperty("tradingDays").GetInt32() == 1);
        await PerformanceAsync(portal, id, p => p.GetProperty("balance").GetArrayLength() == 2);
        var second = await VersionAsync(portal, id);
        var unchanged = await VersionAsync(portal, id);

        Assert.NotEqual(first, second);
        Assert.Equal(second, unchanged);
    }

    [Fact]
    public async Task APassedStageKeepsWhenItStartedAndHowItWasPassed()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var id = (await factory.StartActiveAccountAsync(challengeId: "quick-test-100k")).GetProperty("id").GetGuid();
        using var portal = await factory.LogInAsTraderAsync(id);

        // 0.1 % of 100 000 passes the quick challenge's first stage.
        factory.Trading.OpenPosition(Phase1);
        factory.Trading.ClosePosition(Phase1, 150m);
        await factory.WaitForAccountAsync(id, a => a.GetProperty("stage").GetInt32() == 1 && a.GetProperty("status").GetString() == "Active");
        var details = await portal.GetFromJsonAsync<JsonElement>(Url($"accounts/{id}"), TestContext.Current.CancellationToken);
        var current = await PerformanceAsync(portal, id, p => p.GetProperty("stage").GetInt32() == 1 && p.GetProperty("balance").GetArrayLength() == 1);
        var first = await portal.GetFromJsonAsync<JsonElement>(Url($"accounts/{id}/performance?stage=0"), TestContext.Current.CancellationToken);
        var firstTrades = await portal.GetFromJsonAsync<JsonElement>(Url($"accounts/{id}/trades?stage=0"), TestContext.Current.CancellationToken);
        using var notStarted = await portal.GetAsync(Url($"accounts/{id}/performance?stage=2"), TestContext.Current.CancellationToken);

        var passed = details.GetProperty("stages")[0];
        Assert.Equal(("Passed", Phase1, 150m, 1), (passed.GetProperty("progress").GetString(), passed.GetProperty("tradingAccountId").GetString(), passed.GetProperty("result").GetDecimal(), passed.GetProperty("tradingDays").GetInt32()));
        Assert.Equal(PropFactory.Start, passed.GetProperty("startedAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.String, passed.GetProperty("passedAt").ValueKind);
        Assert.Equal(("Current", "demo-firm-1001-2"), (details.GetProperty("stages")[1].GetProperty("progress").GetString(), details.GetProperty("stages")[1].GetProperty("tradingAccountId").GetString()));
        Assert.Equal(("demo-firm-1001-2", 1), (current.GetProperty("tradingAccountId").GetString(), current.GetProperty("balance").GetArrayLength()));
        Assert.Equal(1, first.GetProperty("statistics").GetProperty("trades").GetInt32());
        Assert.Equal(150m, Assert.Single(firstTrades.GetProperty("trades").EnumerateArray()).GetProperty("result").GetDecimal());
        Assert.Equal(HttpStatusCode.NotFound, notStarted.StatusCode);
    }

    [Fact]
    public async Task AnEndedAccountSaysWhenItEndedAndWhy()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var failed = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        var cancelled = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        using var portal = await factory.LogInAsTraderAsync(failed);
        factory.Trading.Breach(Phase1, "daily", 94_900m);
        await factory.WaitForAccountAsync(failed, a => a.GetProperty("status").GetString() == "Failed");
        using var firm = factory.CreateFirmClient();
        await firm.PostJsonAsync($"accounts/{cancelled}/cancel", new { reason = "Refunded." });

        var accounts = await portal.GetFromJsonAsync<JsonElement>(Url("accounts"), TestContext.Current.CancellationToken);

        var (breached, ended) = (accounts[0], accounts[1]);
        Assert.Equal(("Failed", "DailyLoss"), (breached.GetProperty("account").GetProperty("status").GetString(), breached.GetProperty("breach").GetProperty("reason").GetString()));
        Assert.Equal(breached.GetProperty("breach").GetProperty("time").GetDateTimeOffset(), breached.GetProperty("endedAt").GetDateTimeOffset());
        Assert.Equal((94_900m, 0m), (breached.GetProperty("breach").GetProperty("equity").GetDecimal(), breached.GetProperty("results").GetProperty("stageResult").GetDecimal()));
        Assert.Equal(JsonValueKind.Null, breached.GetProperty("live").ValueKind);
        Assert.Equal(("Cancelled", JsonValueKind.Null, JsonValueKind.Null), (ended.GetProperty("account").GetProperty("status").GetString(), ended.GetProperty("breach").ValueKind, ended.GetProperty("expiry").ValueKind));
        Assert.Equal(PropFactory.Start, ended.GetProperty("endedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task ClosedPositionsComeAPageAtATimeAndAsAFile()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var id = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        using var portal = await factory.LogInAsTraderAsync(id);
        foreach (var (symbol, profit) in new[] { ("EURUSD", 120m), ("GBPUSD", -40m), ("USDJPY", 60.5m) })
        {
            factory.Trading.OpenPosition(Phase1, symbol, volume: 0.5m, commission: 1m);
            factory.Trading.ClosePosition(Phase1, profit, commission: 1m);
        }

        await PerformanceAsync(portal, id, p => p.GetProperty("statistics").GetProperty("trades").GetInt32() == 3);
        var page = await portal.GetFromJsonAsync<JsonElement>(Url($"accounts/{id}/trades?limit=2"), TestContext.Current.CancellationToken);
        var rest = await portal.GetFromJsonAsync<JsonElement>(Url($"accounts/{id}/trades?limit=2&before={page.GetProperty("next").GetInt64()}"), TestContext.Current.CancellationToken);
        using var tooMany = await portal.GetAsync(Url($"accounts/{id}/trades?limit=0"), TestContext.Current.CancellationToken);
        using var file = await portal.GetAsync(Url($"accounts/{id}/trades.csv"), TestContext.Current.CancellationToken);
        var lines = (await file.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(["USDJPY", "GBPUSD"], page.GetProperty("trades").EnumerateArray().Select(t => t.GetProperty("symbol").GetString()));
        Assert.Equal(["EURUSD"], rest.GetProperty("trades").EnumerateArray().Select(t => t.GetProperty("symbol").GetString()));
        Assert.Equal(JsonValueKind.Null, rest.GetProperty("next").ValueKind);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooMany.StatusCode);
        Assert.Equal(("text/csv", "account-1001-phase-1-trades.csv"), (file.Content.Headers.ContentType?.MediaType, file.Content.Headers.ContentDisposition?.FileName));
        Assert.Equal("Position,Symbol,Side,Volume,Opened (UTC),Open price,Closed (UTC),Close price,Profit,Commission,Result,Close reason", lines[0]);
        Assert.Equal(["EURUSD", "GBPUSD", "USDJPY"], lines.Skip(1).Select(l => l.Split(',')[1]));
        Assert.Equal("position-1,EURUSD,Buy,0.5,2026-10-05 08:00:00,1.1,2026-10-05 08:00:00,1.1,120,2,118,Manual", lines[1]);
    }

    [Fact]
    public async Task ATraderSeesOnlyTheHistoryOfTheirOwnAccounts()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var anna = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        var bert = (await factory.StartActiveAccountAsync("bert@test.example")).GetProperty("id").GetGuid();
        using var portal = await factory.LogInAsTraderAsync(anna);

        using var performance = await portal.GetAsync(Url($"accounts/{bert}/performance"), TestContext.Current.CancellationToken);
        using var trades = await portal.GetAsync(Url($"accounts/{bert}/trades"), TestContext.Current.CancellationToken);
        using var file = await portal.GetAsync(Url($"accounts/{bert}/trades.csv"), TestContext.Current.CancellationToken);
        using var anonymous = factory.CreatePortalClient();
        using var notLoggedIn = await anonymous.GetAsync(Url($"accounts/{anna}/performance"), TestContext.Current.CancellationToken);

        Assert.All([performance, trades, file], r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Equal(HttpStatusCode.Unauthorized, notLoggedIn.StatusCode);
    }

    [Fact]
    public async Task TheTraderSeesEveryPayoutWithWhatIsPaidOnItsWayAndReady()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        const string funded = "demo-firm-1001-3";
        var id = (await factory.StartActiveAccountAsync(challengeId: "quick-test-100k")).GetProperty("id").GetGuid();
        using var portal = await factory.LogInAsTraderAsync(id);
        foreach (var stage in new[] { 0, 1 })
        {
            await factory.WaitForAccountAsync(id, a => a.GetProperty("stage").GetInt32() == stage && a.GetProperty("status").GetString() == "Active");
            factory.Trading.OpenPosition($"demo-firm-1001-{stage + 1}");
            factory.Trading.ClosePosition($"demo-firm-1001-{stage + 1}", 100m);
            await factory.WaitForAccountAsync(id, a => a.GetProperty("stage").GetInt32() > stage);
        }

        using var firm = factory.CreateFirmClient();
        await factory.WaitForAccountAsync(id, a => a.GetProperty("status").GetString() == "AwaitingFunding");
        await firm.PostJsonAsync($"accounts/{id}/approve-funding", null);
        await factory.WaitForAccountAsync(id, a => a.GetProperty("funded").GetBoolean() && a.GetProperty("status").GetString() == "Active");
        factory.Trading.OpenPosition(funded);
        factory.Trading.ClosePosition(funded, 8_000m);
        await factory.WaitForAccountAsync(id, a => a.GetProperty("nextPayout").GetProperty("canRequest").GetBoolean());

        var ready = await portal.GetFromJsonAsync<JsonElement>(Url("payouts"), TestContext.Current.CancellationToken);
        using var requested = await portal.PostAsync(Url($"accounts/{id}/payouts"), null, TestContext.Current.CancellationToken);
        var payoutId = (await requested.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
        await factory.WaitForAccountAsync(id, a => a.GetProperty("balance").GetDecimal() == 100_000m);
        var onItsWay = await portal.GetFromJsonAsync<JsonElement>(Url("payouts"), TestContext.Current.CancellationToken);
        await firm.PostJsonAsync($"payouts/{payoutId}/approve", null);
        await firm.PostJsonAsync($"payouts/{payoutId}/mark-paid", new { reference = "wire-1" });
        var paid = await portal.GetFromJsonAsync<JsonElement>(Url("payouts"), TestContext.Current.CancellationToken);
        var account = await portal.GetFromJsonAsync<JsonElement>(Url($"accounts/{id}"), TestContext.Current.CancellationToken);

        Assert.Equal(("USD", 0m, 0m, 6_400m), Totals(ready));
        Assert.Equal(("USD", 0m, 6_400m, 0m), Totals(onItsWay));
        Assert.Equal(("USD", 6_400m, 0m, 0m), Totals(paid));
        Assert.Equal(("Paid", "wire-1"), (paid.GetProperty("payouts")[0].GetProperty("status").GetString(), paid.GetProperty("payouts")[0].GetProperty("reference").GetString()));
        Assert.Equal(6_400m, account.GetProperty("results").GetProperty("paidOut").GetDecimal());
        Assert.Equal(("Passed", "Passed", "Current"), (account.GetProperty("stages")[0].GetProperty("progress").GetString(), account.GetProperty("stages")[1].GetProperty("progress").GetString(), account.GetProperty("stages")[2].GetProperty("progress").GetString()));

        static (string?, decimal, decimal, decimal) Totals(JsonElement payouts)
        {
            var total = Assert.Single(payouts.GetProperty("totals").EnumerateArray());
            return (total.GetProperty("currency").GetString(), total.GetProperty("paid").GetDecimal(), total.GetProperty("onTheWay").GetDecimal(), total.GetProperty("readyToRequest").GetDecimal());
        }
    }

    /// <summary>Asks for the latest stage's performance until the history has caught up with the condition.</summary>
    private static async Task<JsonElement> PerformanceAsync(HttpClient portal, Guid id, Func<JsonElement, bool> condition)
    {
        JsonElement performance = default;
        await Eventually.ThatAsync(
            async () =>
            {
                performance = await portal.GetFromJsonAsync<JsonElement>(Url($"accounts/{id}/performance"));
                return condition(performance);
            },
            "the trading history");
        return performance;
    }

    private static async Task<string?> VersionAsync(HttpClient portal, Guid id) =>
        (await portal.GetFromJsonAsync<JsonElement>(Url($"accounts/{id}"))).GetProperty("historyVersion").GetString();

    /// <summary>Asks for the trader's only account on the dashboard until it is as the condition wants.</summary>
    private static async Task<JsonElement> DashboardAccountAsync(HttpClient portal, Func<JsonElement, bool> condition)
    {
        JsonElement account = default;
        await Eventually.ThatAsync(
            async () =>
            {
                account = Assert.Single((await portal.GetFromJsonAsync<JsonElement>(Url("accounts"))).EnumerateArray());
                return condition(account);
            },
            "the dashboard");
        return account;
    }

    // To the next midnight in Stockholm, then a few hours into the day.
    private static async Task NextTradingDayAsync(PropFactory factory)
    {
        var now = factory.Time.GetUtcNow();
        var midnight = new DateTimeOffset(now.Date.AddHours(22), TimeSpan.Zero);
        await factory.AdvanceAsync((now < midnight ? midnight : midnight.AddDays(1)) - now + TimeSpan.FromHours(10));
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static Uri Url(string path) => new($"/api/portal/{path}", UriKind.Relative);
}
