using System.Net;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Trading.Engine;
using Trading.Engine.Inputs;
using Trading.Service.Tenancy;
using Trading.Service.Tests.Support;

namespace Trading.Service.Tests;

/// <summary>The partner API that creates firms while the platform runs, such as for a firm that signs up (ADR 0016).</summary>
public sealed class PartnerApiTests
{
    private const string OtherPartnerKey = "other-partner-key";

    private static readonly Dictionary<string, string> OtherPartner = new()
    {
        ["Partners:1:Id"] = "other-partner",
        ["Partners:1:Name"] = "Other partner",
        ["Partners:1:ApiKeySha256"] = TenantCatalog.HashApiKey(OtherPartnerKey),
    };

    [Fact]
    public async Task ANewFirmCanTradeAtOnceInItsOwnGroup()
    {
        using var factory = new ServiceFactory();
        using var partner = factory.CreatePartnerClient();

        var created = await partner.PostJsonAsync("/api/partner/v1/tenants", new { id = "acme", name = " Acme Prop " }, HttpStatusCode.Created);

        Assert.Equal(("acme", "Acme Prop", false), (created.GetProperty("id").GetString(), created.GetProperty("name").GetString(), created.GetProperty("listed").GetBoolean()));
        var group = Assert.Single(created.GetProperty("groups").EnumerateArray());
        Assert.Equal(("acme-standard", "USD"), (group.GetProperty("id").GetString(), group.GetProperty("currency").GetString()));

        using var trader = await CreateTraderAsync(factory, created.GetProperty("adminApiKey").GetString()!, "acme", "ACME1");
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        var order = await trader.PostJsonAsync("/api/accounts/ACME1/orders", new { orderId = "O1", symbol = "EURUSD", side = "Buy", type = "Market", volume = 1.00m });

        Assert.Equal(["PositionOpened"], order.GetProperty("events").EventKinds());
    }

    // Gold has no pair with EUR, so the engine converts through USD.
    [Fact]
    public async Task ANewFirmChoosesTheCurrencyOfItsAccounts()
    {
        using var factory = new ServiceFactory();
        using var partner = factory.CreatePartnerClient();

        var created = await partner.PostJsonAsync("/api/partner/v1/tenants", new { id = "acme", name = "Acme", currency = "EUR" }, HttpStatusCode.Created);
        var refused = await partner.PostJsonAsync("/api/partner/v1/tenants", new { id = "beta", name = "Beta", currency = "SEK" }, HttpStatusCode.UnprocessableEntity);
        using var trader = await CreateTraderAsync(factory, created.GetProperty("adminApiKey").GetString()!, "acme", "ACME1");
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        await factory.PushQuoteAsync("XAUUSD", 2650.00m, 2650.30m);
        var order = await trader.PostJsonAsync("/api/accounts/ACME1/orders", new { orderId = "O1", symbol = "XAUUSD", side = "Buy", type = "Market", volume = 0.10m });

        Assert.Equal("EUR", Assert.Single(created.GetProperty("groups").EnumerateArray()).GetProperty("currency").GetString());
        Assert.Equal("InvalidCurrency", refused.GetProperty("reason").GetString());
        Assert.Equal(["PositionOpened"], order.GetProperty("events").EventKinds());
    }

    // What the terminal loads first. The new firm's group was created while the engine ran, not configured.
    [Fact]
    public async Task ANewFirmsTraderSeesTheInstrumentsPricesAndChartsOfItsGroup()
    {
        using var factory = new ServiceFactory();
        using var partner = factory.CreatePartnerClient();
        var apiKey = (await partner.PostJsonAsync("/api/partner/v1/tenants", new { id = "acme", name = "Acme" }, HttpStatusCode.Created)).GetProperty("adminApiKey").GetString()!;
        using var trader = await CreateTraderAsync(factory, apiKey, "acme", "ACME1");
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);

        var instruments = await trader.GetJsonAsync("/api/accounts/ACME1/instruments");
        var price = await trader.PriceAsync("ACME1", "EURUSD");
        var candles = await trader.GetJsonAsync("/api/accounts/ACME1/candles/EURUSD?timeframe=M1&count=10");

        Assert.Equal(
            ["AUDUSD", "EURGBP", "EURJPY", "EURUSD", "GBPJPY", "GBPUSD", "NZDUSD", "USDCAD", "USDCHF", "USDJPY", "XAGUSD", "XAUUSD"],
            instruments.EnumerateArray().Select(i => i.GetProperty("symbol").GetString()));
        Assert.Equal(2, instruments.EnumerateArray().Single(i => i.GetProperty("symbol").GetString() == "EURUSD").GetProperty("spreadMarkupPoints").GetInt32());
        Assert.Equal((1.07999m, 1.08011m), (price.GetProperty("bid").GetDecimal(), price.GetProperty("ask").GetDecimal()));
        Assert.Equal(1.07999m, candles.EnumerateArray().Last().GetProperty("close").GetDecimal());
    }

    [Fact]
    public async Task ANewFirmSeesOnlyItsOwnEventsAndAccounts()
    {
        using var factory = new ServiceFactory();
        (await factory.CreateTraderClientAsync("T1")).Dispose();
        using var partner = factory.CreatePartnerClient();
        var apiKey = (await partner.PostJsonAsync("/api/partner/v1/tenants", new { id = "acme", name = "Acme" }, HttpStatusCode.Created)).GetProperty("adminApiKey").GetString()!;
        (await CreateTraderAsync(factory, apiKey, "acme", "ACME1")).Dispose();
        using var acme = factory.CreateAdminClient(apiKey);

        var events = await acme.GetJsonAsync("/api/admin/v1/events");
        using var demoAccount = await acme.GetAsync(new Uri("/api/admin/v1/accounts/T1", UriKind.Relative), TestContext.Current.CancellationToken);
        await acme.PostJsonAsync(
            "/api/admin/v1/accounts",
            new { accountId = "X1", groupId = "standard", initialBalance = 1_000m, ownerUserId = await UserIdAsync(acme, "acme1") },
            HttpStatusCode.NotFound);

        Assert.Equal(["AccountCreated"], events.GetProperty("events").EventKinds());
        Assert.Equal(HttpStatusCode.NotFound, demoAccount.StatusCode);
    }

    [Fact]
    public async Task ServerNamesAreUniqueAndValid()
    {
        using var factory = new ServiceFactory();
        using var partner = factory.CreatePartnerClient();
        await partner.PostJsonAsync("/api/partner/v1/tenants", new { id = "acme", name = "Acme" }, HttpStatusCode.Created);

        var taken = await partner.PostJsonAsync("/api/partner/v1/tenants", new { id = "acme", name = "Another Acme" }, HttpStatusCode.Conflict);
        await partner.PostJsonAsync("/api/partner/v1/tenants", new { id = ServiceFactory.DemoServer, name = "Demo" }, HttpStatusCode.Conflict);
        await partner.PostJsonAsync("/api/partner/v1/tenants", new { id = "Acme_Prop", name = "Acme" }, HttpStatusCode.UnprocessableEntity);
        await partner.PostJsonAsync("/api/partner/v1/tenants", new { id = "acme-two", name = " " }, HttpStatusCode.UnprocessableEntity);

        Assert.Equal("DuplicateId", taken.GetProperty("reason").GetString());
        Assert.False((await partner.GetJsonAsync("/api/partner/v1/server-names/acme")).GetProperty("available").GetBoolean());
        Assert.False((await partner.GetJsonAsync($"/api/partner/v1/server-names/{ServiceFactory.DemoServer}")).GetProperty("available").GetBoolean());
        Assert.False((await partner.GetJsonAsync("/api/partner/v1/server-names/Acme_Prop")).GetProperty("available").GetBoolean());
        Assert.True((await partner.GetJsonAsync("/api/partner/v1/server-names/acme-two")).GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task OnlyPartnersCanCreateFirms()
    {
        using var factory = new ServiceFactory();
        using var anonymous = factory.CreateClient();
        using var firm = factory.CreateAdminClient();
        using var partner = factory.CreatePartnerClient();

        await anonymous.PostJsonAsync("/api/partner/v1/tenants", new { id = "acme", name = "Acme" }, HttpStatusCode.Unauthorized);
        await firm.PostJsonAsync("/api/partner/v1/tenants", new { id = "acme", name = "Acme" }, HttpStatusCode.Unauthorized);
        await partner.PostJsonAsync("/api/admin/v1/users", new { email = "a@test.example", password = ServiceFactory.TraderPassword }, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task APartnerSeesOnlyTheFirmsItCreated()
    {
        using var factory = new ServiceFactory(settings: OtherPartner);
        using var partner = factory.CreatePartnerClient();
        using var other = factory.CreatePartnerClient(OtherPartnerKey);
        await partner.PostJsonAsync("/api/partner/v1/tenants", new { id = "acme", name = "Acme" }, HttpStatusCode.Created);

        var acme = await partner.GetJsonAsync("/api/partner/v1/tenants/acme");
        await other.SendJsonAsync(HttpMethod.Get, "/api/partner/v1/tenants/acme", null, HttpStatusCode.NotFound);
        await other.PostJsonAsync("/api/partner/v1/tenants/acme/admin-key", null, HttpStatusCode.NotFound);
        await partner.SendJsonAsync(HttpMethod.Get, $"/api/partner/v1/tenants/{ServiceFactory.DemoServer}", null, HttpStatusCode.NotFound);

        Assert.Equal(["acme-standard"], acme.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("id").GetString()));
        Assert.False(acme.TryGetProperty("adminApiKey", out _));
    }

    [Fact]
    public async Task ANewAdminKeyReplacesTheOldOne()
    {
        using var factory = new ServiceFactory();
        using var partner = factory.CreatePartnerClient();
        var firstKey = (await partner.PostJsonAsync("/api/partner/v1/tenants", new { id = "acme", name = "Acme" }, HttpStatusCode.Created)).GetProperty("adminApiKey").GetString()!;

        var secondKey = (await partner.PostJsonAsync("/api/partner/v1/tenants/acme/admin-key")).GetProperty("adminApiKey").GetString()!;

        Assert.NotEqual(firstKey, secondKey);
        using var withFirstKey = factory.CreateAdminClient(firstKey);
        using var withSecondKey = factory.CreateAdminClient(secondKey);
        await withFirstKey.SendJsonAsync(HttpMethod.Get, "/api/admin/v1/events", null, HttpStatusCode.Unauthorized);
        await withSecondKey.GetJsonAsync("/api/admin/v1/events");
    }

    // Traders of a new firm come in through login links from its portal until it goes live.
    [Fact]
    public async Task NewFirmsAreNotListedButTheirTradersCanLogIn()
    {
        using var factory = new ServiceFactory();
        using var partner = factory.CreatePartnerClient();
        var apiKey = (await partner.PostJsonAsync("/api/partner/v1/tenants", new { id = "acme", name = "Acme" }, HttpStatusCode.Created)).GetProperty("adminApiKey").GetString()!;

        using var trader = await CreateTraderAsync(factory, apiKey, "acme", "ACME1");
        using var anonymous = factory.CreateClient();
        var servers = await anonymous.GetJsonAsync("/api/servers");

        Assert.Equal([ServiceFactory.DemoServer], servers.EnumerateArray().Select(s => s.GetProperty("id").GetString()));
        Assert.Equal("acme", (await trader.GetJsonAsync("/api/auth/me")).GetProperty("server").GetProperty("id").GetString());
    }

    // When the firm goes live, its portal lists the server and tells the terminal where its traders log in.
    [Fact]
    public async Task APartnerListsItsFirmAndSetsWhereItsTradersLogIn()
    {
        using var factory = new ServiceFactory();
        using var partner = factory.CreatePartnerClient();
        await partner.PostJsonAsync("/api/partner/v1/tenants", new { id = "acme", name = "Acme" }, HttpStatusCode.Created);
        using var anonymous = factory.CreateClient();

        var unlisted = await anonymous.GetJsonAsync("/api/servers/acme");
        var changed = await partner.SendJsonAsync(HttpMethod.Patch, "/api/partner/v1/tenants/acme", new { listed = true, loginUrl = "https://acme.example.com/terminal" }, HttpStatusCode.OK);
        var servers = await anonymous.GetJsonAsync("/api/servers");
        var onlyListing = await partner.SendJsonAsync(HttpMethod.Patch, "/api/partner/v1/tenants/acme", new { listed = false }, HttpStatusCode.OK);
        var cleared = await partner.SendJsonAsync(HttpMethod.Patch, "/api/partner/v1/tenants/acme", new { loginUrl = "" }, HttpStatusCode.OK);

        Assert.Equal(("Acme", JsonValueKind.Null), (unlisted.GetProperty("name").GetString(), unlisted.GetProperty("loginUrl").ValueKind));
        Assert.Equal((true, "https://acme.example.com/terminal"), (changed.GetProperty("listed").GetBoolean(), changed.GetProperty("loginUrl").GetString()));
        var acme = servers.EnumerateArray().Single(s => s.GetProperty("id").GetString() == "acme");
        Assert.Equal("https://acme.example.com/terminal", acme.GetProperty("loginUrl").GetString());
        Assert.Equal((false, "https://acme.example.com/terminal"), (onlyListing.GetProperty("listed").GetBoolean(), onlyListing.GetProperty("loginUrl").GetString()));
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("loginUrl").ValueKind);
        await anonymous.SendJsonAsync(HttpMethod.Get, "/api/servers/no-such-firm", null, HttpStatusCode.NotFound);
    }

    // The terminal shows the firm's logo in its account bar (ADR 0035).
    [Fact]
    public async Task APartnerSetsTheFirmsLogo()
    {
        using var factory = new ServiceFactory();
        using var partner = factory.CreatePartnerClient();
        await partner.PostJsonAsync("/api/partner/v1/tenants", new { id = "acme", name = "Acme" }, HttpStatusCode.Created);
        using var anonymous = factory.CreateClient();

        var changed = await partner.SendJsonAsync(HttpMethod.Patch, "/api/partner/v1/tenants/acme", new { logoUrl = "https://acme.example.com/api/portal/logo/abc" }, HttpStatusCode.OK);
        var server = await anonymous.GetJsonAsync("/api/servers/acme");
        await partner.SendJsonAsync(HttpMethod.Patch, "/api/partner/v1/tenants/acme", new { logoUrl = "javascript:alert(1)" }, HttpStatusCode.UnprocessableEntity);
        var removed = await partner.SendJsonAsync(HttpMethod.Patch, "/api/partner/v1/tenants/acme", new { logoUrl = "" }, HttpStatusCode.OK);

        Assert.Equal("https://acme.example.com/api/portal/logo/abc", changed.GetProperty("logoUrl").GetString());
        Assert.Equal("https://acme.example.com/api/portal/logo/abc", server.GetProperty("logoUrl").GetString());
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("logoUrl").ValueKind);
    }

    [Fact]
    public async Task ALoginAddressMustBeAWebAddressAndOnlyTheFirmsPartnerSetsIt()
    {
        using var factory = new ServiceFactory(settings: OtherPartner);
        using var partner = factory.CreatePartnerClient();
        using var other = factory.CreatePartnerClient(OtherPartnerKey);
        await partner.PostJsonAsync("/api/partner/v1/tenants", new { id = "acme", name = "Acme" }, HttpStatusCode.Created);

        await partner.SendJsonAsync(HttpMethod.Patch, "/api/partner/v1/tenants/acme", new { loginUrl = "javascript:alert(1)" }, HttpStatusCode.UnprocessableEntity);
        await partner.SendJsonAsync(HttpMethod.Patch, "/api/partner/v1/tenants/acme", new { loginUrl = "/terminal" }, HttpStatusCode.UnprocessableEntity);
        await other.SendJsonAsync(HttpMethod.Patch, "/api/partner/v1/tenants/acme", new { listed = true }, HttpStatusCode.NotFound);
        await partner.SendJsonAsync(HttpMethod.Patch, $"/api/partner/v1/tenants/{ServiceFactory.DemoServer}", new { listed = false }, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListingSurvivesARestart()
    {
        var backend = new InMemoryBackend();
        using (var first = new ServiceFactory(backend))
        {
            using var partner = first.CreatePartnerClient();
            await partner.PostJsonAsync("/api/partner/v1/tenants", new { id = "acme", name = "Acme" }, HttpStatusCode.Created);
            await partner.SendJsonAsync(HttpMethod.Patch, "/api/partner/v1/tenants/acme", new { listed = true, loginUrl = "https://acme.example.com/terminal" }, HttpStatusCode.OK);
        }

        using var second = new ServiceFactory(backend);
        using var anonymous = second.CreateClient();
        var acme = (await anonymous.GetJsonAsync("/api/servers")).EnumerateArray().Single(s => s.GetProperty("id").GetString() == "acme");

        Assert.Equal("https://acme.example.com/terminal", acme.GetProperty("loginUrl").GetString());
    }

    [Fact]
    public async Task FirmsAndTheirGroupsSurviveACrash()
    {
        using var first = new ServiceFactory();
        using var partner = first.CreatePartnerClient();
        var apiKey = (await partner.PostJsonAsync("/api/partner/v1/tenants", new { id = "acme", name = "Acme" }, HttpStatusCode.Created)).GetProperty("adminApiKey").GetString()!;
        (await CreateTraderAsync(first, apiKey, "acme", "ACME1")).Dispose();

        using var second = new ServiceFactory(first.Backend.Crashed());
        using var acme = second.CreateAdminClient(apiKey);

        var account = await acme.GetJsonAsync("/api/admin/v1/accounts/ACME1");
        Assert.Equal("acme-standard", account.GetProperty("groupId").GetString());
        using var restartedPartner = second.CreatePartnerClient();
        await restartedPartner.PostJsonAsync("/api/partner/v1/tenants", new { id = "acme", name = "Acme" }, HttpStatusCode.Conflict);
    }

    // A restart after a normal stop starts from the snapshot, which must hold the created groups.
    [Fact]
    public async Task FirmsAndTheirGroupsSurviveARestart()
    {
        var backend = new InMemoryBackend();
        string apiKey;
        using (var first = new ServiceFactory(backend))
        {
            using var partner = first.CreatePartnerClient();
            apiKey = (await partner.PostJsonAsync("/api/partner/v1/tenants", new { id = "acme", name = "Acme" }, HttpStatusCode.Created)).GetProperty("adminApiKey").GetString()!;
            (await CreateTraderAsync(first, apiKey, "acme", "ACME1")).Dispose();
        }

        using var second = new ServiceFactory(backend);
        using var trader = await second.LoginAsync(ServiceFactory.EmailOf("ACME1"), ServiceFactory.TraderPassword, "acme");
        await second.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);

        var order = await trader.PostJsonAsync("/api/accounts/ACME1/orders", new { orderId = "O1", symbol = "EURUSD", side = "Buy", type = "Market", volume = 1.00m });
        Assert.Equal(["PositionOpened"], order.GetProperty("events").EventKinds());
    }

    // The engine created the group, but the firm was never saved, for example because the service stopped in between.
    [Fact]
    public async Task AGroupLeftByAFailedAttemptIsTakenOver()
    {
        using var factory = new ServiceFactory();
        using var partner = factory.CreatePartnerClient();
        var template = factory.Services.GetRequiredService<EngineConfiguration>().Groups.Single(g => g.Id == "standard");
        await factory.Engine.SendAsync(t => new CreateGroup(t, template with { Id = "acme-standard" }), TestContext.Current.CancellationToken);

        var created = await partner.PostJsonAsync("/api/partner/v1/tenants", new { id = "acme", name = "Acme" }, HttpStatusCode.Created);

        Assert.Equal(["acme-standard"], created.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("id").GetString()));
    }

    private static async Task<HttpClient> CreateTraderAsync(ServiceFactory factory, string apiKey, string server, string accountId)
    {
        using var admin = factory.CreateAdminClient(apiKey);
        var user = await admin.PostJsonAsync("/api/admin/v1/users", new { email = ServiceFactory.EmailOf(accountId), password = ServiceFactory.TraderPassword });
        await admin.PostJsonAsync(
            "/api/admin/v1/accounts",
            new { accountId, groupId = $"{server}-standard", initialBalance = 100_000m, ownerUserId = user.GetProperty("userId").GetGuid() });
        return await factory.LoginAsync(ServiceFactory.EmailOf(accountId), ServiceFactory.TraderPassword, server);
    }

    private static async Task<Guid> UserIdAsync(HttpClient admin, string accountId)
    {
        var user = await admin.GetJsonAsync($"/api/admin/v1/users?email={ServiceFactory.EmailOf(accountId)}");
        return user.GetProperty("userId").GetGuid();
    }
}
