using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.SignalR.Client;

using Trading.Service.Tests.Support;

namespace Trading.Service.Tests;

/// <summary>Receipts, breach reports, what an outage did to a firm's accounts and the firm's notice (ADR 0053).</summary>
public sealed class ReportsTests
{
    private const string AccountId = "T1";
    private const string Orders = $"/api/accounts/{AccountId}/orders";

    // The standard group lowers EURUSD's bid by a point and raises its ask by one, and charges 3.50 per lot and side.
    [Fact]
    public async Task AReceiptShowsTheFeedsPriceBehindEachFillAndThePricesAround()
    {
        using var factory = new ServiceFactory();
        using var client = await factory.CreateTraderClientAsync(AccountId);
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        await client.PostJsonAsync(Orders, new { orderId = "P1", symbol = "EURUSD", side = "Buy", type = "Market", volume = 1.00m, stopLoss = 1.07950m });
        factory.Time.Advance(TimeSpan.FromSeconds(20));
        await factory.PushQuoteAsync("EURUSD", 1.08100m, 1.08110m);
        factory.Time.Advance(TimeSpan.FromSeconds(20));
        await factory.PushQuoteAsync("EURUSD", 1.07940m, 1.07950m);

        var receipt = await client.GetJsonAsync($"/api/accounts/{AccountId}/positions/P1/receipt");

        var opened = receipt.GetProperty("opened");
        Assert.Equal((1.08011m, 1.08010m, 1), (opened.GetProperty("price").GetDecimal(), opened.GetProperty("feed").GetProperty("ask").GetDecimal(), opened.GetProperty("markupPoints").GetInt32()));
        Assert.Equal(3, opened.GetProperty("prices").GetArrayLength());
        var close = Assert.Single(receipt.GetProperty("closes").EnumerateArray());
        var closeFill = close.GetProperty("fill");
        Assert.Equal(("StopLoss", 1.07950m, 1.07939m), (close.GetProperty("reason").GetString(), close.GetProperty("level").GetDecimal(), closeFill.GetProperty("price").GetDecimal()));
        Assert.Equal((1.07940m, 1), (closeFill.GetProperty("feed").GetProperty("bid").GetDecimal(), closeFill.GetProperty("markupPoints").GetInt32()));
        Assert.Equal((-72.00m, 7.00m, -79.00m), (receipt.GetProperty("profit").GetDecimal(), receipt.GetProperty("commission").GetDecimal(), receipt.GetProperty("result").GetDecimal()));
        Assert.Equal(JsonValueKind.Null, receipt.GetProperty("order").ValueKind);
    }

    [Fact]
    public async Task AReceiptHasThePendingOrderItCameFromAndEveryPart()
    {
        using var factory = new ServiceFactory();
        using var client = await factory.CreateTraderClientAsync(AccountId);
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        await client.PostJsonAsync(Orders, new { orderId = "O1", symbol = "EURUSD", side = "Buy", type = "Limit", volume = 1.00m, price = 1.07500m });
        factory.Time.Advance(TimeSpan.FromMinutes(5));
        await factory.PushQuoteAsync("EURUSD", 1.07490m, 1.07499m);
        await client.PostJsonAsync($"/api/accounts/{AccountId}/positions/O1/close", new { volume = 0.40m });
        await client.PostJsonAsync($"/api/accounts/{AccountId}/positions/O1/close");

        var receipt = await client.GetJsonAsync($"/api/accounts/{AccountId}/positions/O1/receipt");

        Assert.Equal(("Limit", 1.07500m), (receipt.GetProperty("order").GetProperty("type").GetString(), receipt.GetProperty("order").GetProperty("price").GetDecimal()));
        Assert.Equal((1.07500m, 1), (receipt.GetProperty("opened").GetProperty("price").GetDecimal(), receipt.GetProperty("opened").GetProperty("markupPoints").GetInt32()));
        Assert.Equal([0.40m, 0.60m], receipt.GetProperty("closes").EnumerateArray().Select(c => c.GetProperty("fill").GetProperty("volume").GetDecimal()));
        Assert.Equal(("Manual", 1.07489m), (receipt.GetProperty("closes")[1].GetProperty("reason").GetString(), receipt.GetProperty("closes")[1].GetProperty("fill").GetProperty("price").GetDecimal()));
        Assert.Equal(-11.00m - 7.00m, receipt.GetProperty("result").GetDecimal());
    }

    [Fact]
    public async Task ReceiptsAndReportsAreOnlyForTheAccountsOwnerAndItsFirm()
    {
        using var factory = new ServiceFactory(settings: SecondFirm.Settings);
        using var client = await factory.CreateTraderClientAsync(AccountId);
        using var other = await factory.CreateTraderClientAsync("T2");
        using var otherFirm = factory.CreateAdminClient(SecondFirm.ApiKey);
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        await client.PostJsonAsync(Orders, new { orderId = "P1", symbol = "EURUSD", side = "Buy", type = "Market", volume = 1.00m });

        await client.SendJsonAsync(HttpMethod.Get, $"/api/accounts/{AccountId}/positions/P9/receipt", null, HttpStatusCode.NotFound);
        await other.SendJsonAsync(HttpMethod.Get, $"/api/accounts/{AccountId}/positions/P1/receipt", null, HttpStatusCode.NotFound);
        await otherFirm.SendJsonAsync(HttpMethod.Get, $"/api/admin/v1/accounts/{AccountId}/positions/P1/receipt", null, HttpStatusCode.NotFound);
        await client.SendJsonAsync(HttpMethod.Get, $"/api/accounts/{AccountId}/breach-report", null, HttpStatusCode.NotFound);
        var firms = await client.GetJsonAsync($"/api/admin/v1/accounts/{AccountId}/positions/P1/receipt");

        Assert.Equal(1.08011m, firms.GetProperty("opened").GetProperty("price").GetDecimal());
    }

    // Equity 99,984.50 after the commission and the spread, 99,884.50 half a minute later, and 98,884.50 on the first price
    // after five minutes without.
    [Fact]
    public async Task ABreachReportShowsEquityPriceByPriceTheGapAndThePricesThatBrokeIt()
    {
        using var factory = new ServiceFactory();
        using var client = await factory.CreateTraderClientAsync(AccountId);
        await client.PutJsonAsync($"/api/admin/v1/accounts/{AccountId}/floors/daily", new { rule = new { kind = "FixedFloor", level = 99_000m } });
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        await client.PostJsonAsync(Orders, new { orderId = "P1", symbol = "EURUSD", side = "Buy", type = "Market", volume = 1.00m });
        factory.Time.Advance(TimeSpan.FromSeconds(30));
        await factory.PushQuoteAsync("EURUSD", 1.07900m, 1.07910m);
        factory.Time.Advance(TimeSpan.FromMinutes(5));
        var broken = factory.Time.GetUtcNow();
        await factory.PushQuoteAsync("EURUSD", 1.06900m, 1.06910m);

        var report = await client.GetJsonAsync($"/api/accounts/{AccountId}/breach-report");
        var admin = await client.GetJsonAsync($"/api/admin/v1/accounts/{AccountId}/breach-report");

        Assert.Equal((99_000m, 98_884.50m, 99_996.50m, 98_881.00m), (report.GetProperty("level").GetDecimal(), report.GetProperty("equity").GetDecimal(), report.GetProperty("balance").GetDecimal(), report.GetProperty("balanceAfter").GetDecimal()));
        var price = Assert.Single(report.GetProperty("prices").EnumerateArray());
        Assert.Equal((1.06899m, 1.06900m, 1, 1), (price.GetProperty("bid").GetDecimal(), price.GetProperty("feed").GetProperty("bid").GetDecimal(), price.GetProperty("bidMarkupPoints").GetInt32(), price.GetProperty("askMarkupPoints").GetInt32()));
        Assert.Equal(
            [99_984.50m, 99_884.50m, 98_884.50m],
            report.GetProperty("equityCurve").EnumerateArray().Select(p => p.GetProperty("equity").GetDecimal()));
        var gap = Assert.Single(report.GetProperty("gaps").EnumerateArray());
        Assert.Equal(broken, gap.GetProperty("to").GetDateTimeOffset());
        Assert.Equal(
            ["AccountCreated", "EquityFloorSet", "PositionOpened", "EquityFloorBreached", "PositionClosed", "AccountDisabled"],
            report.GetProperty("events").EventKinds());
        Assert.Equal(report.GetRawText(), admin.GetRawText());
    }

    [Fact]
    public async Task AFirmSeesWhatAnOutageDidToItsAccounts()
    {
        using var factory = new ServiceFactory(settings: SecondFirm.Settings);
        using var trader = await factory.CreateTraderClientAsync(AccountId);
        using var second = await factory.CreateTraderClientAsync("T2");
        using var quiet = await factory.CreateTraderClientAsync("T3");
        using var otherFirm = factory.CreateAdminClient(SecondFirm.ApiKey);
        await trader.PutJsonAsync($"/api/admin/v1/accounts/{AccountId}/floors/daily", new { rule = new { kind = "FixedFloor", level = 99_000m } });
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        await trader.PostJsonAsync(Orders, new { orderId = "P1", symbol = "EURUSD", side = "Buy", type = "Market", volume = 1.00m });

        // The last price before the outage, then commands refused for lack of a fresh one.
        factory.Time.Advance(TimeSpan.FromSeconds(10));
        await factory.PushQuoteAsync("EURUSD", 1.07900m, 1.07910m);
        var from = factory.Time.GetUtcNow();
        factory.Time.Advance(TimeSpan.FromMinutes(2));
        await trader.PostJsonAsync($"/api/accounts/{AccountId}/positions/P1/close", expected: HttpStatusCode.UnprocessableEntity);
        await second.PostJsonAsync("/api/accounts/T2/orders", new { orderId = "Q1", symbol = "EURUSD", side = "Sell", type = "Market", volume = 1.00m }, HttpStatusCode.UnprocessableEntity);
        factory.Time.Advance(TimeSpan.FromMinutes(10));
        var to = factory.Time.GetUtcNow();
        await factory.PushQuoteAsync("EURUSD", 1.06900m, 1.06910m);

        var impact = await trader.GetJsonAsync($"/api/admin/v1/impact?from={Escape(from)}&to={Escape(to)}");
        var others = await otherFirm.GetJsonAsync($"/api/admin/v1/impact?from={Escape(from)}&to={Escape(to)}");
        await trader.SendJsonAsync(HttpMethod.Get, $"/api/admin/v1/impact?from={Escape(to)}&to={Escape(from)}", null, HttpStatusCode.UnprocessableEntity);
        await trader.SendJsonAsync(HttpMethod.Get, $"/api/admin/v1/impact?from={Escape(from)}&to={Escape(from.AddDays(2))}", null, HttpStatusCode.UnprocessableEntity);

        var accounts = impact.GetProperty("accounts").EnumerateArray().ToDictionary(a => a.GetProperty("accountId").GetString()!);
        Assert.Equal(["T1", "T2"], accounts.Keys);
        var broken = accounts["T1"];
        Assert.Equal((1, 99_884.50m, 1, 0), (broken.GetProperty("openPositions").GetInt32(), broken.GetProperty("equityAtStart").GetDecimal(), broken.GetProperty("closesRefused").GetInt32(), broken.GetProperty("ordersRefused").GetInt32()));
        Assert.Equal((99_996.50m, 98_884.50m, "Disabled"), (broken.GetProperty("balanceAtStart").GetDecimal(), broken.GetProperty("breach").GetProperty("equity").GetDecimal(), broken.GetProperty("status").GetString()));
        Assert.Equal((0, 1, JsonValueKind.Null), (accounts["T2"].GetProperty("openPositions").GetInt32(), accounts["T2"].GetProperty("ordersRefused").GetInt32(), accounts["T2"].GetProperty("breach").ValueKind));
        Assert.Empty(others.GetProperty("accounts").EnumerateArray());
    }

    [Fact]
    public async Task TheFirmsNoticeReachesItsTerminalsAtOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new ServiceFactory(settings: SecondFirm.Settings);
        using var client = await factory.CreateTraderClientAsync(AccountId);
        using var otherFirm = factory.CreateAdminClient(SecondFirm.ApiKey);
        await using var connection = factory.CreateHubConnection(await factory.LoginCookieAsync(ServiceFactory.EmailOf(AccountId), ServiceFactory.TraderPassword));
        var notices = new List<JsonElement>();
        var pushed = new SemaphoreSlim(0);
        connection.On<JsonElement>("Notice", notice =>
        {
            lock (notices)
            {
                notices.Add(notice);
            }

            pushed.Release();
        });
        await connection.StartAsync(cancellationToken);
        await connection.InvokeAsync("Subscribe", AccountId, cancellationToken);
        await pushed.WaitAsync(cancellationToken);

        var set = await client.PutJsonAsync("/api/admin/v1/notice", new { title = "Prices have stopped", text = "No prices since 15:12. Orders are refused until they return.", level = "Warning", url = "https://aurora.example/status" });
        await pushed.WaitAsync(cancellationToken);
        var read = await client.GetJsonAsync($"/api/accounts/{AccountId}/notice");
        await otherFirm.PutJsonAsync("/api/admin/v1/notice", new { title = "Not yours", text = "Another firm's notice." });
        var problem = await client.PutJsonAsync("/api/admin/v1/notice", new { title = "", text = "x" }, HttpStatusCode.UnprocessableEntity);
        await client.SendJsonAsync(HttpMethod.Delete, "/api/admin/v1/notice", null, HttpStatusCode.NoContent);
        await pushed.WaitAsync(cancellationToken);
        var removed = await client.GetJsonAsync($"/api/accounts/{AccountId}/notice");

        Assert.Equal(("Prices have stopped", "Warning"), (set.GetProperty("title").GetString(), set.GetProperty("level").GetString()));
        Assert.Equal("Prices have stopped", read.GetProperty("notice").GetProperty("title").GetString());
        Assert.Contains("title", problem.GetProperty("title").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("notice").ValueKind);
        lock (notices)
        {
            Assert.Equal([JsonValueKind.Null, JsonValueKind.Object, JsonValueKind.Null], notices.Select(n => n.ValueKind));
        }
    }

    [Fact]
    public async Task APartnerSeesWhenTheLastPriceCameAndWhichMarketsAreOpen()
    {
        using var factory = new ServiceFactory();
        using var trader = await factory.CreateTraderClientAsync(AccountId);
        using var partner = factory.CreatePartnerClient();
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        var priced = factory.Time.GetUtcNow();
        factory.Time.Advance(TimeSpan.FromSeconds(30));

        var status = await partner.GetJsonAsync("/api/partner/v1/price-feed");
        var me = await trader.GetJsonAsync("/api/auth/me");

        Assert.Equal((ManualPriceFeed.DefaultName, priced), (status.GetProperty("feed").GetString(), status.GetProperty("lastPriceAt").GetDateTimeOffset()));
        var eurusd = status.GetProperty("symbols").EnumerateArray().Single(s => s.GetProperty("symbol").GetString() == "EURUSD");
        Assert.True(eurusd.GetProperty("marketOpen").GetBoolean());
        Assert.Equal(5d, me.GetProperty("maxPriceAgeSeconds").GetDouble());
    }

    [Fact]
    public async Task TheFirmReopensAnAccountALimitEndedWithANewBalance()
    {
        using var factory = new ServiceFactory();
        using var client = await factory.CreateTraderClientAsync(AccountId);
        await client.PutJsonAsync($"/api/admin/v1/accounts/{AccountId}/floors/daily", new { rule = new { kind = "FixedFloor", level = 100_001m } });

        var reopened = await client.PostJsonAsync($"/api/admin/v1/accounts/{AccountId}/reopen", new { balance = 98_212.50m });
        var again = await client.PostJsonAsync($"/api/admin/v1/accounts/{AccountId}/reopen", new { balance = 98_212.50m });
        var account = await client.GetJsonAsync($"/api/accounts/{AccountId}");

        Assert.Equal(["AccountReopened", "EquityFloorRemoved"], reopened.GetProperty("events").EventKinds());
        Assert.Empty(again.GetProperty("events").EnumerateArray());
        Assert.Equal(("Active", 98_212.50m), (account.GetProperty("status").GetString(), account.GetProperty("balance").GetDecimal()));
    }

    private static string Escape(DateTimeOffset time) => Uri.EscapeDataString(time.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
}
