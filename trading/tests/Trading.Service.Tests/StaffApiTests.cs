using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Trading.Service.Candles;
using Trading.Service.Staff;
using Trading.Service.Tests.Support;

namespace Trading.Service.Tests;

/// <summary>Our staff's panel over the platform (ADR 0057): its login, the figures it shows and what staff can do.</summary>
public sealed class StaffApiTests
{
    private const string Staff = "/api/staff/v1";

    // The factories' clock starts at 2026-10-05 08:00 UTC, a Monday morning when every market is open.
    private static readonly DateTimeOffset Today = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StaffLogInWithASessionOfTheirOwnThatReachesNothingElse()
    {
        using var factory = new ServiceFactory();
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        using (var wrong = await anonymous.PostAsJsonAsync(new Uri($"{Staff}/login", UriKind.Relative), new { email = ServiceFactory.StaffEmail, password = "nope" }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(new Uri($"{Staff}/overview", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode);

        using var staff = await factory.CreateStaffClientAsync();
        Assert.Equal(ServiceFactory.StaffEmail, (await staff.GetJsonAsync($"{Staff}/me")).GetProperty("email").GetString());

        // A staff session is no trader's session, and a trader's session or a firm's key is no staff session.
        Assert.Equal(HttpStatusCode.Unauthorized, (await staff.GetAsync(new Uri("/api/auth/me", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode);
        using var trader = await factory.CreateTraderClientAsync("T1");
        Assert.Equal(HttpStatusCode.Unauthorized, (await trader.GetAsync(new Uri($"{Staff}/overview", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode);
        using var admin = factory.CreateAdminClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync(new Uri($"{Staff}/servers", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode);

        // Logging out ends the session in the browser, which drops the cookie.
        using var logout = await staff.PostAsync(new Uri($"{Staff}/logout", UriKind.Relative), null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains(logout.Headers.GetValues("Set-Cookie"), c => c.StartsWith("trading_staff_session=;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheOverviewCountsWhatTradersDoAcrossThePlatform()
    {
        using var factory = new ServiceFactory();
        using var trader = await factory.CreateTraderClientAsync("T1");
        (await factory.CreateTraderClientAsync("T2")).Dispose();
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        await trader.PostJsonAsync("/api/accounts/T1/orders", new { orderId = "O1", symbol = "EURUSD", side = "Buy", type = "Market", volume = 1.00m });
        using (var refused = await trader.PostAsJsonAsync(
            new Uri("/api/accounts/T1/orders", UriKind.Relative),
            new { orderId = "O2", symbol = "XAUUSD", side = "Buy", type = "Market", volume = 1.00m },
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        }

        using var staff = await factory.CreateStaffClientAsync();
        var overview = await staff.GetJsonAsync($"{Staff}/overview");

        var figures = overview.GetProperty("figures");
        Assert.True(figures.GetProperty("traders").GetInt32() >= 2);
        Assert.Equal(1, figures.GetProperty("openPositions").GetInt32());
        Assert.Equal(1, figures.GetProperty("accountsWithPositions").GetInt32());
        Assert.Equal(1, figures.GetProperty("positionsOpenedToday").GetInt32());
        Assert.Equal(1, figures.GetProperty("refusedToday").GetInt32());
        Assert.True(overview.GetProperty("servers").GetInt32() >= 1);

        // 1 lot of 100,000 EUR at the mid rate 1.08005, long.
        var exposure = Assert.Single(overview.GetProperty("topExposure").EnumerateArray());
        Assert.Equal("EURUSD", exposure.GetProperty("symbol").GetString());
        Assert.Equal(108_005.00m, exposure.GetProperty("netValue").GetDecimal());

        Assert.True(overview.GetProperty("engine").GetProperty("healthy").GetBoolean());
        Assert.Equal(ManualPriceFeed.DefaultName, overview.GetProperty("feed").GetProperty("feed").GetString());
        await Eventually.ThatAsync(
            async () => (await staff.GetJsonAsync($"{Staff}/overview")).GetProperty("latest").EnumerateArray().Any(e => e.GetProperty("kind").GetString() == "ServiceStarted"),
            "the start to be in the platform's log");
    }

    [Fact]
    public async Task SymbolsWithoutPricesWhileTheirMarketIsOpenNeedUs()
    {
        using var factory = new ServiceFactory();
        using var staff = await factory.CreateStaffClientAsync();
        var watcher = factory.Services.GetRequiredService<PlatformWatcher>();
        var symbols = await SymbolsAsync(factory);
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);

        // No price for a minute from any symbol is the whole feed falling silent, one entry rather than one per symbol.
        factory.Time.Advance(TimeSpan.FromMinutes(2));
        await watcher.CheckAsync(TestContext.Current.CancellationToken);
        var needs = (await staff.GetJsonAsync($"{Staff}/overview")).GetProperty("needsUs");
        Assert.Equal("FeedSilent", Assert.Single(needs.EnumerateArray()).GetProperty("kind").GetString());

        // Every symbol but UK100 gets prices again.
        foreach (var symbol in symbols.Where(s => s != "UK100"))
        {
            await factory.PushQuoteAsync(symbol, 100.0m, 100.1m);
        }

        await watcher.CheckAsync(TestContext.Current.CancellationToken);
        var item = Assert.Single((await staff.GetJsonAsync($"{Staff}/overview")).GetProperty("needsUs").EnumerateArray());
        Assert.Equal(("SymbolSilent", "UK100"), (item.GetProperty("kind").GetString(), item.GetProperty("symbol").GetString()));
        Assert.Equal(JsonValueKind.Null, item.GetProperty("since").ValueKind);

        var feed = await staff.GetJsonAsync($"{Staff}/price-feed");
        var uk100 = feed.GetProperty("symbols").EnumerateArray().Single(s => s.GetProperty("symbol").GetString() == "UK100");
        Assert.Equal("Silent", uk100.GetProperty("state").GetString());
        var eurusd = feed.GetProperty("symbols").EnumerateArray().Single(s => s.GetProperty("symbol").GetString() == "EURUSD");
        Assert.Equal(("Live", 100.0m, 100.1m), (eurusd.GetProperty("state").GetString(), eurusd.GetProperty("bid").GetDecimal(), eurusd.GetProperty("ask").GetDecimal()));
        Assert.Equal("Forex", eurusd.GetProperty("category").GetString());

        await factory.PushQuoteAsync("UK100", 8_000.0m, 8_001.0m);
        await watcher.CheckAsync(TestContext.Current.CancellationToken);
        Assert.Empty((await staff.GetJsonAsync($"{Staff}/overview")).GetProperty("needsUs").EnumerateArray());
        Assert.Equal(
            ["FeedSilent", "FeedBack", "SymbolSilent", "SymbolBack"],
            factory.Backend.Log.Entries.Select(e => e.Kind.ToString()).Where(k => k is "FeedSilent" or "FeedBack" or "SymbolSilent" or "SymbolBack"));
    }

    [Fact]
    public async Task AFirmsSystemThatStopsReadingItsEventsNeedsUs()
    {
        using var factory = new ServiceFactory();
        using var admin = factory.CreateAdminClient();
        using var staff = await factory.CreateStaffClientAsync();
        var cursor = (await admin.GetJsonAsync("/api/admin/v1/events?after=0")).GetProperty("cursor").GetInt64();

        // Events come, and the firm's system does not ask for them.
        (await factory.CreateTraderClientAsync("T1")).Dispose();
        factory.Time.Advance(TimeSpan.FromMinutes(6));

        var item = Assert.Single((await staff.GetJsonAsync($"{Staff}/overview")).GetProperty("needsUs").EnumerateArray(), i => i.GetProperty("kind").GetString() == "EventsNotRead");
        Assert.Equal(ServiceFactory.DemoServer, item.GetProperty("serverId").GetString());
        Assert.True(item.GetProperty("count").GetInt32() > 0);
        var server = await staff.GetJsonAsync($"{Staff}/servers/{ServiceFactory.DemoServer}");
        Assert.True(server.GetProperty("events").GetProperty("stale").GetBoolean());
        Assert.Equal(0, server.GetProperty("events").GetProperty("after").GetInt64());
        Assert.Equal(1, (await staff.GetJsonAsync($"{Staff}/servers")).GetProperty("counts").GetProperty("needsUs").GetInt32());

        await admin.GetJsonAsync($"/api/admin/v1/events?after={cursor}");
        Assert.DoesNotContain(
            (await staff.GetJsonAsync($"{Staff}/overview")).GetProperty("needsUs").EnumerateArray(),
            i => i.GetProperty("kind").GetString() == "EventsNotRead");
    }

    [Fact]
    public async Task TheServersShowWhoMadeEachAndCanBeFilteredAndSearched()
    {
        using var factory = new ServiceFactory();
        using var partner = factory.CreatePartnerClient();
        await partner.PostJsonAsync("/api/partner/v1/tenants", new { id = "acme", name = "Acme Prop", currency = "EUR" }, HttpStatusCode.Created);
        using var staff = await factory.CreateStaffClientAsync();

        var servers = await staff.GetJsonAsync($"{Staff}/servers");
        var acme = Row(servers, "acme");
        Assert.Equal(("Partner", "Kronant Prop", false), (acme.GetProperty("madeBy").GetString(), acme.GetProperty("partnerName").GetString(), acme.GetProperty("listed").GetBoolean()));
        Assert.Equal(["EUR"], acme.GetProperty("currencies").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal(factory.Time.GetUtcNow(), acme.GetProperty("createdAt").GetDateTimeOffset());
        var demo = Row(servers, ServiceFactory.DemoServer);
        Assert.Equal(("Configuration", true), (demo.GetProperty("madeBy").GetString(), demo.GetProperty("listed").GetBoolean()));

        Assert.Equal(["acme"], Ids(await staff.GetJsonAsync($"{Staff}/servers?group=notListed")));
        Assert.Equal([ServiceFactory.DemoServer], Ids(await staff.GetJsonAsync($"{Staff}/servers?group=configuration")));
        Assert.Equal(["acme"], Ids(await staff.GetJsonAsync($"{Staff}/servers?search=ACM")));

        var log = Assert.Single(factory.Backend.Log.Entries, e => e.Kind == PlatformEventKind.ServerCreated);
        Assert.Equal(("acme", "Kronant Prop", "Acme Prop", "EUR"), (log.ServerId, log.Detail["partner"], log.Detail["name"], log.Detail["currency"]));
    }

    [Fact]
    public async Task StaffMakeAServerAndSeeItsKeyOnlyOnce()
    {
        using var factory = new ServiceFactory();
        using var staff = await factory.CreateStaffClientAsync();

        Assert.True((await staff.GetJsonAsync($"{Staff}/server-names/helix")).GetProperty("available").GetBoolean());
        await staff.PostJsonAsync($"{Staff}/servers", new { id = "helix", name = "Helix Markets", currency = "GBP" }, HttpStatusCode.UnprocessableEntity);
        var created = await staff.PostJsonAsync($"{Staff}/servers", new { id = "helix", name = "Helix Markets", currency = "GBP", kind = "Practice" }, HttpStatusCode.Created);
        var key = created.GetProperty("adminApiKey").GetString()!;

        // The key reaches the new server's admin API, with a group in pounds.
        using var admin = factory.CreateAdminClient(key);
        Assert.Equal("GBP", Assert.Single((await admin.GetJsonAsync("/api/admin/v1/groups")).EnumerateArray()).GetProperty("currency").GetString());

        var server = await staff.GetJsonAsync($"{Staff}/servers/helix");
        Assert.Equal(("Staff", ServiceFactory.StaffEmail), (server.GetProperty("madeBy").GetString(), server.GetProperty("createdBy").GetString()));
        Assert.DoesNotContain("adminApiKey", server.GetRawText(), StringComparison.Ordinal);
        var made = Assert.Single(factory.Backend.Log.Entries, e => e.Kind == PlatformEventKind.ServerCreated);
        Assert.Equal((ServiceFactory.StaffEmail, "Practice"), (made.StaffEmail, made.Detail["kind"]));

        // A practice server's terminal shows no rules or own limits, and its traders log in with a password.
        var terminal = server.GetProperty("terminal");
        Assert.Equal(("Practice", "Staff", true), (terminal.GetProperty("kind").GetString(), terminal.GetProperty("setBy").GetString(), terminal.GetProperty("passwordLogin").GetBoolean()));
        Assert.False(terminal.GetProperty("modules").GetProperty("rulebook").GetBoolean());
        Assert.Equal("Practice", Row(await staff.GetJsonAsync($"{Staff}/servers"), "helix").GetProperty("kind").GetString());

        await staff.PostJsonAsync($"{Staff}/servers", new { id = "helix", name = "Again", currency = "USD", kind = "Prop" }, HttpStatusCode.Conflict);
        await staff.PostJsonAsync($"{Staff}/servers", new { id = "No Spaces", name = "Bad", currency = "USD", kind = "Prop" }, HttpStatusCode.UnprocessableEntity);
        await staff.PostJsonAsync($"{Staff}/servers", new { id = "sekfirm", name = "Kronor", currency = "SEK", kind = "Prop" }, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task StoppingAKeyAPartnerHoldsShowsNoKeyAndThePartnerGetsANewOne()
    {
        using var factory = new ServiceFactory();
        using var partner = factory.CreatePartnerClient();
        var created = await partner.PostJsonAsync("/api/partner/v1/tenants", new { id = "acme", name = "Acme Prop" }, HttpStatusCode.Created);
        using var oldKey = factory.CreateAdminClient(created.GetProperty("adminApiKey").GetString()!);
        await oldKey.GetJsonAsync("/api/admin/v1/groups");
        using var staff = await factory.CreateStaffClientAsync();

        await staff.PostJsonAsync($"{Staff}/servers/acme/admin-key", new { reason = "" }, HttpStatusCode.UnprocessableEntity);
        var stopped = await staff.PostJsonAsync($"{Staff}/servers/acme/admin-key", new { reason = "The key was in a shared log" });

        Assert.Equal(JsonValueKind.Null, stopped.GetProperty("adminApiKey").ValueKind);
        Assert.Equal("Kronant Prop", stopped.GetProperty("heldBy").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldKey.GetAsync(new Uri("/api/admin/v1/groups", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode);

        // The partner asks for a new key when the old one is turned away.
        var renewed = await partner.PostJsonAsync("/api/partner/v1/tenants/acme/admin-key");
        using var newKey = factory.CreateAdminClient(renewed.GetProperty("adminApiKey").GetString()!);
        await newKey.GetJsonAsync("/api/admin/v1/groups");

        var log = factory.Backend.Log.Entries.Where(e => e.Kind == PlatformEventKind.AdminKeyReplaced).ToList();
        Assert.Equal((ServiceFactory.StaffEmail, "The key was in a shared log"), (log[0].StaffEmail, log[0].Detail["reason"]));
        Assert.Equal("Kronant Prop", log[1].Detail["partner"]);

        // A configured server's key comes from the configuration.
        await staff.PostJsonAsync($"{Staff}/servers/{ServiceFactory.DemoServer}/admin-key", new { reason = "Leaked" }, HttpStatusCode.Conflict);
        await staff.PostJsonAsync($"{Staff}/servers/missing/admin-key", new { reason = "Leaked" }, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StoppingTheKeyOfAServerStaffMadeShowsTheNewOne()
    {
        using var factory = new ServiceFactory();
        using var staff = await factory.CreateStaffClientAsync();
        var created = await staff.PostJsonAsync($"{Staff}/servers", new { id = "helix", name = "Helix Markets", kind = "Desk" }, HttpStatusCode.Created);
        using var oldKey = factory.CreateAdminClient(created.GetProperty("adminApiKey").GetString()!);

        var stopped = await staff.PostJsonAsync($"{Staff}/servers/helix/admin-key", new { reason = "Rotated after a staff change" });

        Assert.Equal(JsonValueKind.Null, stopped.GetProperty("heldBy").ValueKind);
        using var newKey = factory.CreateAdminClient(stopped.GetProperty("adminApiKey").GetString()!);
        await newKey.GetJsonAsync("/api/admin/v1/groups");
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldKey.GetAsync(new Uri("/api/admin/v1/groups", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode);
    }

    // The kind of business decides the terminal's words and parts (ADR 0058). Only a kind our staff set can change here.
    [Fact]
    public async Task StaffChangeTheKindOfAServerTheyMadeButNotOfOnesAPartnerKeeps()
    {
        using var factory = new ServiceFactory();
        using var partner = factory.CreatePartnerClient();
        await partner.PostJsonAsync("/api/partner/v1/tenants", new { id = "acme", name = "Acme Prop" }, HttpStatusCode.Created);
        using var staff = await factory.CreateStaffClientAsync();
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        await staff.PostJsonAsync($"{Staff}/servers", new { id = "helix", name = "Helix Markets", kind = "Practice" }, HttpStatusCode.Created);
        var acme = await staff.GetJsonAsync($"{Staff}/servers/acme");

        var changed = await SendAsync(staff, HttpMethod.Patch, $"{Staff}/servers/helix", new { kind = "Broker" });
        var refused = await staff.SendJsonAsync(HttpMethod.Patch, $"{Staff}/servers/acme", new { kind = "Broker" }, HttpStatusCode.Conflict);

        // A broker's orders ask first and its traders get their own limits, and the terminal gets it at its next login.
        var terminal = changed.GetProperty("terminal");
        Assert.Equal(("Broker", true, true), (terminal.GetProperty("kind").GetString(), terminal.GetProperty("confirmOrders").GetBoolean(), terminal.GetProperty("modules").GetProperty("ownLimits").GetBoolean()));
        Assert.True(terminal.GetProperty("passwordLogin").GetBoolean());
        Assert.Equal("Broker", (await anonymous.GetJsonAsync("/api/servers/helix")).GetProperty("profile").GetProperty("kind").GetString());
        var log = Assert.Single(factory.Backend.Log.Entries, e => e.Kind == PlatformEventKind.TerminalKindChanged);
        Assert.Equal(("helix", ServiceFactory.StaffEmail, "Practice", "Broker"), (log.ServerId, log.StaffEmail, log.Detail["from"], log.Detail["kind"]));

        // Kronant Prop keeps its firms' terminals as prop firms.
        Assert.Equal(("Prop", "Partner"), (acme.GetProperty("terminal").GetProperty("kind").GetString(), acme.GetProperty("terminal").GetProperty("setBy").GetString()));
        Assert.Contains("Kronant Prop", refused.GetProperty("title").GetString(), StringComparison.Ordinal);
        Assert.Equal("Prop", (await staff.GetJsonAsync($"{Staff}/servers/acme")).GetProperty("terminal").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task AConfiguredServerWhoseConfigurationSetsItsTerminalKeepsItsKind()
    {
        using var factory = new ServiceFactory(settings: new Dictionary<string, string> { ["Tenants:0:Terminal:Kind"] = "Desk" });
        using var staff = await factory.CreateStaffClientAsync();

        var demo = await staff.GetJsonAsync($"{Staff}/servers/{ServiceFactory.DemoServer}");
        await staff.SendJsonAsync(HttpMethod.Patch, $"{Staff}/servers/{ServiceFactory.DemoServer}", new { kind = "Practice" }, HttpStatusCode.Conflict);

        Assert.Equal(("Desk", "Configuration"), (demo.GetProperty("terminal").GetProperty("kind").GetString(), demo.GetProperty("terminal").GetProperty("setBy").GetString()));
    }

    [Fact]
    public async Task StaffPutAServerOnTheListAndTakeItOff()
    {
        using var factory = new ServiceFactory();
        using var partner = factory.CreatePartnerClient();
        await partner.PostJsonAsync("/api/partner/v1/tenants", new { id = "acme", name = "Acme Prop" }, HttpStatusCode.Created);
        using var staff = await factory.CreateStaffClientAsync();
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var listed = await SendAsync(staff, HttpMethod.Patch, $"{Staff}/servers/acme", new { listed = true });
        Assert.True(listed.GetProperty("listed").GetBoolean());
        Assert.Equal(factory.Time.GetUtcNow(), listed.GetProperty("listedAt").GetDateTimeOffset());
        Assert.Contains((await anonymous.GetJsonAsync("/api/servers?search=Acme")).EnumerateArray(), s => s.GetProperty("id").GetString() == "acme");

        await SendAsync(staff, HttpMethod.Patch, $"{Staff}/servers/acme", new { listed = false });
        Assert.DoesNotContain((await anonymous.GetJsonAsync("/api/servers?search=Acme")).EnumerateArray(), s => s.GetProperty("id").GetString() == "acme");
        Assert.Equal(
            [PlatformEventKind.ServerListed, PlatformEventKind.ServerUnlisted],
            factory.Backend.Log.Entries.Where(e => e.StaffEmail == ServiceFactory.StaffEmail).Select(e => e.Kind));
    }

    [Fact]
    public async Task AServerShowsItsGroupsFiguresAndEventsAndAnAccountCanBeFound()
    {
        using var factory = new ServiceFactory();
        using var trader = await factory.CreateTraderClientAsync("T1");
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        await trader.PostJsonAsync("/api/accounts/T1/orders", new { orderId = "O1", symbol = "EURUSD", side = "Sell", type = "Market", volume = 2.00m });
        (await factory.CreateTraderClientAsync("T2")).Dispose();
        using var staff = await factory.CreateStaffClientAsync();

        var server = await staff.GetJsonAsync($"{Staff}/servers/{ServiceFactory.DemoServer}");
        var group = Assert.Single(server.GetProperty("groups").EnumerateArray());
        Assert.Equal(("standard", false), (group.GetProperty("id").GetString(), group.GetProperty("changeable").GetBoolean()));
        var eurusd = group.GetProperty("symbols").EnumerateArray().Single(s => s.GetProperty("symbol").GetString() == "EURUSD");
        Assert.Equal(1, eurusd.GetProperty("openPositions").GetInt32());
        Assert.Equal(1, server.GetProperty("figures").GetProperty("openPositions").GetInt32());
        Assert.True(server.GetProperty("figures").GetProperty("accountsTrading").GetInt32() >= 2);

        // The latest first, and only one account's when asked.
        var events = (await staff.GetJsonAsync($"{Staff}/servers/{ServiceFactory.DemoServer}/events")).GetProperty("events").EnumerateArray().ToList();
        Assert.Equal(events.Select(e => e.GetProperty("sequence").GetInt64()).OrderDescending(), events.Select(e => e.GetProperty("sequence").GetInt64()));
        var mine = (await staff.GetJsonAsync($"{Staff}/servers/{ServiceFactory.DemoServer}/events?account=T1")).GetProperty("events");
        Assert.All(mine.EnumerateArray(), e => Assert.Equal("T1", e.GetProperty("event").GetProperty("accountId").GetString()));
        Assert.Contains("PositionOpened", mine.EventKinds());

        var account = await staff.GetJsonAsync($"{Staff}/accounts/T1");
        Assert.Equal((ServiceFactory.DemoServer, "Active", 1), (account.GetProperty("serverId").GetString(), account.GetProperty("status").GetString(), account.GetProperty("openPositions").GetInt32()));
        Assert.DoesNotContain("@", account.GetRawText(), StringComparison.Ordinal);
        await staff.SendJsonAsync(HttpMethod.Get, $"{Staff}/accounts/missing", null, HttpStatusCode.NotFound);
        await staff.SendJsonAsync(HttpMethod.Get, $"{Staff}/servers/missing", null, HttpStatusCode.NotFound);

        var found = await staff.GetJsonAsync($"{Staff}/search?q=demo");
        Assert.Equal(ServiceFactory.DemoServer, Assert.Single(found.GetProperty("servers").EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal("T1", (await staff.GetJsonAsync($"{Staff}/search?q=%23T1")).GetProperty("account").GetProperty("accountId").GetString());
    }

    [Fact]
    public async Task TheExposureSumsWhatTradersHoldInUsd()
    {
        using var factory = new ServiceFactory();
        using var buyer = await factory.CreateTraderClientAsync("T1");
        using var seller = await factory.CreateTraderClientAsync("T2");
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        await buyer.PostJsonAsync("/api/accounts/T1/orders", new { orderId = "O1", symbol = "EURUSD", side = "Buy", type = "Market", volume = 1.00m });
        await seller.PostJsonAsync("/api/accounts/T2/orders", new { orderId = "O1", symbol = "EURUSD", side = "Sell", type = "Market", volume = 2.00m });
        using var staff = await factory.CreateStaffClientAsync();

        var exposure = await staff.GetJsonAsync($"{Staff}/exposure");

        Assert.Equal(("USD", 108_005.00m, 216_010.00m, -108_005.00m), (
            exposure.GetProperty("currency").GetString(),
            exposure.GetProperty("longValue").GetDecimal(),
            exposure.GetProperty("shortValue").GetDecimal(),
            exposure.GetProperty("netValue").GetDecimal()));
        Assert.Equal((1, 1, 2), (exposure.GetProperty("longPositions").GetInt32(), exposure.GetProperty("shortPositions").GetInt32(), exposure.GetProperty("accounts").GetInt32()));
        var symbol = Assert.Single(exposure.GetProperty("symbols").EnumerateArray());
        Assert.Equal((1.00m, 2.00m, -1.00m, 2), (
            symbol.GetProperty("longLots").GetDecimal(),
            symbol.GetProperty("shortLots").GetDecimal(),
            symbol.GetProperty("netLots").GetDecimal(),
            symbol.GetProperty("accounts").GetInt32()));
        Assert.Equal(["T2", "T1"], exposure.GetProperty("largest").EnumerateArray().Select(p => p.GetProperty("accountId").GetString()));
        Assert.Equal(ServiceFactory.DemoServer, Assert.Single(exposure.GetProperty("servers").EnumerateArray()).GetProperty("serverId").GetString());

        Assert.Empty((await staff.GetJsonAsync($"{Staff}/exposure?server=missing")).GetProperty("symbols").EnumerateArray());
    }

    [Fact]
    public async Task AChartGapThatCouldNotBeFilledCanBeTriedAgain()
    {
        var feed = new ManualPriceFeed("Real");
        using var factory = new ServiceFactory(feed: feed);
        using var trader = await factory.CreateTraderClientAsync("T1");
        using var staff = await factory.CreateStaffClientAsync();
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);

        feed.HistoryFailure = new HttpRequestException("Too many requests");
        factory.Time.Advance(TimeSpan.FromMinutes(5));
        await factory.PushQuoteAsync("EURUSD", 1.08400m, 1.08410m);
        JsonElement gap = default;
        await Eventually.ThatAsync(
            async () =>
            {
                factory.Time.Advance(TimeSpan.FromSeconds(1));
                gap = (await staff.GetJsonAsync($"{Staff}/price-feed")).GetProperty("gaps").EnumerateArray().FirstOrDefault();
                return gap.ValueKind == JsonValueKind.Object && gap.GetProperty("state").GetString() == "NotFilled";
            },
            "the gap to be given up on");
        Assert.Equal((4, "Too many requests"), (gap.GetProperty("tries").GetInt32(), gap.GetProperty("problem").GetString()));
        Assert.Contains((await staff.GetJsonAsync($"{Staff}/overview")).GetProperty("needsUs").EnumerateArray(), i => i.GetProperty("kind").GetString() == "ChartGapNotFilled");
        Assert.Equal(2, (await CandlesAsync(trader)).Count);

        feed.HistoryFailure = null;
        feed.History = [new ChartBar("EURUSD", Timeframe.M1, new Candle(Today.AddHours(8).AddMinutes(2), 1.08200m, 1.08200m, 1.08200m, 1.08200m, 0))];
        var id = gap.GetProperty("id").GetGuid();
        await staff.PostJsonAsync($"{Staff}/price-feed/gaps/{id}/retry", expected: HttpStatusCode.Accepted);

        await Eventually.ThatAsync(async () => (await CandlesAsync(trader)).Count == 3, "the gap's bar to be in the charts");
        var filled = (await staff.GetJsonAsync($"{Staff}/price-feed")).GetProperty("gaps").EnumerateArray().Single(g => g.GetProperty("id").GetGuid() == id);
        Assert.Equal(("Filled", 1, 5), (filled.GetProperty("state").GetString(), filled.GetProperty("bars").GetInt32(), filled.GetProperty("tries").GetInt32()));
        Assert.Contains(factory.Backend.Log.Entries, e => e.Kind == PlatformEventKind.ChartGapFilled);

        await staff.PostJsonAsync($"{Staff}/price-feed/gaps/{id}/retry", expected: HttpStatusCode.NotFound);
        await staff.PostJsonAsync($"{Staff}/price-feed/gaps/{Guid.NewGuid()}/retry", expected: HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheHistoryCanBeLoadedAgain()
    {
        var feed = new ManualPriceFeed("Real");
        using var factory = new ServiceFactory(feed: feed);
        using var trader = await factory.CreateTraderClientAsync("T1");
        using var staff = await factory.CreateStaffClientAsync();
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        factory.Time.Advance(TimeSpan.FromMinutes(3));
        await factory.PushQuoteAsync("EURUSD", 1.08100m, 1.08110m);
        Assert.Single(await CandlesAsync(trader, "H1"));

        // A bar from yesterday that the feed did not have when the history was first loaded.
        feed.History = [new ChartBar("EURUSD", Timeframe.M15, new Candle(Today.AddDays(-1).AddHours(9), 1.07000m, 1.07100m, 1.06900m, 1.07050m, 0))];
        await staff.PostJsonAsync($"{Staff}/price-feed/history/reload", expected: HttpStatusCode.Accepted);

        await Eventually.ThatAsync(async () => (await CandlesAsync(trader, "H1")).Count == 2, "yesterday's bar and today's to be in the charts");
        await Eventually.ThatAsync(
            () => factory.Backend.Log.Entries.Any(e => e.Kind == PlatformEventKind.ChartHistoryReloaded && e.StaffEmail == ServiceFactory.StaffEmail),
            "the reload to be in the platform's log");
        var history = (await staff.GetJsonAsync($"{Staff}/price-feed")).GetProperty("history");
        Assert.Equal(Today.AddHours(8).AddMinutes(3), history.GetProperty("loadedAt").GetDateTimeOffset());
        Assert.False(history.GetProperty("reloading").GetBoolean());

        // Today's prices are still there.
        Assert.Equal(1.08100m, (await CandlesAsync(trader, "H1"))[^1].GetProperty("close").GetDecimal() + 0.00001m);
    }

    [Fact]
    public async Task MadeUpPricesHaveNoHistoryToLoad()
    {
        using var factory = new ServiceFactory(feed: new ContinuingPriceFeed("Synthetic"));
        using var staff = await factory.CreateStaffClientAsync();

        Assert.False((await staff.GetJsonAsync($"{Staff}/price-feed")).GetProperty("hasHistory").GetBoolean());
        await staff.PostJsonAsync($"{Staff}/price-feed/history/reload", expected: HttpStatusCode.Conflict);
        await staff.PostJsonAsync($"{Staff}/price-feed/gaps/{Guid.NewGuid()}/retry", expected: HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task TheInstrumentsShowTheirTradingHoursAndTheServersThatTradeThem()
    {
        using var factory = new ServiceFactory();
        using var staff = await factory.CreateStaffClientAsync();

        var instruments = await staff.GetJsonAsync($"{Staff}/instruments");

        Assert.Equal(new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero), instruments.GetProperty("weekStart").GetDateTimeOffset());
        var eurusd = instruments.GetProperty("instruments").EnumerateArray().Single(i => i.GetProperty("symbol").GetString() == "EURUSD");
        Assert.Equal(("Forex", "Forex", 1), (eurusd.GetProperty("category").GetString(), eurusd.GetProperty("hours").GetString(), eurusd.GetProperty("servers").GetInt32()));

        // Sunday 17:00 to Friday 17:00 New York time, in summer time.
        var week = eurusd.GetProperty("thisWeek").EnumerateArray().ToList();
        Assert.Equal(
            (new DateTimeOffset(2026, 10, 4, 21, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 9, 21, 0, 0, TimeSpan.Zero)),
            (week[0].GetProperty("opens").GetDateTimeOffset(), week[^1].GetProperty("closes").GetDateTimeOffset()));
        var btcusd = instruments.GetProperty("instruments").EnumerateArray().Single(i => i.GetProperty("symbol").GetString() == "BTCUSD");
        Assert.Equal(JsonValueKind.Null, btcusd.GetProperty("hours").ValueKind);

        var forex = instruments.GetProperty("hours").EnumerateArray().Single(h => h.GetProperty("name").GetString() == "Forex");
        Assert.Equal("America/New_York", forex.GetProperty("timeZone").GetString());
        Assert.Contains("EURUSD", forex.GetProperty("symbols").EnumerateArray().Select(s => s.GetString()));
        Assert.Contains(instruments.GetProperty("closures").EnumerateArray(), c => c.GetProperty("from").GetDateTime().Date == new DateTime(2026, 12, 25));
    }

    [Fact]
    public async Task TheEngineShowsItsQueueJournalStartAndPartners()
    {
        using var factory = new ServiceFactory();
        using var partner = factory.CreatePartnerClient();
        await partner.GetJsonAsync("/api/partner/v1/price-feed");
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        await factory.PushQuoteAsync("EURUSD", 1.08010m, 1.08020m);
        using var staff = await factory.CreateStaffClientAsync();

        var engine = await staff.GetJsonAsync($"{Staff}/engine");

        Assert.True(engine.GetProperty("healthy").GetBoolean());
        Assert.False(engine.GetProperty("journalFailed").GetBoolean());
        var minutes = engine.GetProperty("minutes").EnumerateArray().ToList();
        Assert.Equal(60, minutes.Count);
        Assert.True(minutes[^1].GetProperty("inputs").GetProperty("Prices").GetInt32() >= 2);
        Assert.True(minutes.Sum(m => m.GetProperty("saves").GetInt32()) > 0);
        Assert.True(engine.GetProperty("lastInput").GetInt64() >= 2);
        Assert.NotEmpty(engine.GetProperty("snapshots").EnumerateArray());
        Assert.Equal(JsonValueKind.Object, engine.GetProperty("start").ValueKind);
        var prop = Assert.Single(engine.GetProperty("partners").EnumerateArray());
        Assert.Equal(("prop-platform", "Kronant Prop"), (prop.GetProperty("id").GetString(), prop.GetProperty("name").GetString()));
        Assert.Equal(factory.Time.GetUtcNow(), prop.GetProperty("lastCallAt").GetDateTimeOffset());
    }

    private static async Task<IReadOnlyList<string>> SymbolsAsync(ServiceFactory factory)
    {
        using var admin = factory.CreateAdminClient();
        return [.. (await admin.GetJsonAsync("/api/admin/v1/instruments")).EnumerateArray().Select(i => i.GetProperty("symbol").GetString()!)];
    }

    private static JsonElement Row(JsonElement servers, string id) =>
        servers.GetProperty("servers").EnumerateArray().Single(s => s.GetProperty("id").GetString() == id);

    private static List<string?> Ids(JsonElement servers) =>
        [.. servers.GetProperty("servers").EnumerateArray().Select(s => s.GetProperty("id").GetString())];

    private static Task<JsonElement> SendAsync(HttpClient client, HttpMethod method, string url, object body) =>
        client.SendJsonAsync(method, url, body, HttpStatusCode.OK);

    private static async Task<List<JsonElement>> CandlesAsync(HttpClient trader, string timeframe = "M1") =>
        [.. (await trader.GetJsonAsync($"/api/accounts/T1/candles/EURUSD?timeframe={timeframe}")).EnumerateArray()];
}
