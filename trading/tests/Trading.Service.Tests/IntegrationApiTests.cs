using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;

using Trading.Service.Tests.Support;

namespace Trading.Service.Tests;

/// <summary>The admin API that firms' own systems, such as the prop platform, build on.</summary>
public sealed class IntegrationApiTests
{
    [Fact]
    public async Task TheAdminApiIsVersioned()
    {
        using var factory = new ServiceFactory();
        using var admin = factory.CreateAdminClient();

        using var unversioned = await admin.PostAsJsonAsync(
            new Uri("/api/admin/users", UriKind.Relative),
            new { email = "a@test.example", password = ServiceFactory.TraderPassword },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, unversioned.StatusCode);
    }

    [Fact]
    public async Task UsersAreFoundByEmailWithinTheFirm()
    {
        using var factory = new ServiceFactory(settings: SecondFirm.Settings);
        (await factory.CreateTraderClientAsync("T1")).Dispose();
        using var admin = factory.CreateAdminClient();
        using var other = factory.CreateAdminClient(SecondFirm.ApiKey);

        var found = await admin.GetJsonAsync($"/api/admin/v1/users?email={ServiceFactory.EmailOf("T1").ToUpperInvariant()}");
        using var notAtOtherFirm = await other.GetAsync(new Uri($"/api/admin/v1/users?email={ServiceFactory.EmailOf("T1")}", UriKind.Relative), TestContext.Current.CancellationToken);
        using var withoutEmail = await admin.GetAsync(new Uri("/api/admin/v1/users", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(ServiceFactory.EmailOf("T1"), found.GetProperty("email").GetString());
        Assert.Equal(HttpStatusCode.NotFound, notAtOtherFirm.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, withoutEmail.StatusCode);
    }

    [Fact]
    public async Task TheFirmCanReplaceATradersPassword()
    {
        using var factory = new ServiceFactory(settings: SecondFirm.Settings);
        var userId = await CreateTraderAsync(factory, "T1");
        using var admin = factory.CreateAdminClient();
        using var other = factory.CreateAdminClient(SecondFirm.ApiKey);
        var url = $"/api/admin/v1/users/{userId}/password";

        await admin.PutJsonAsync(url, new { password = "short" }, HttpStatusCode.UnprocessableEntity);
        await other.PutJsonAsync(url, new { password = "other-firm-password" }, HttpStatusCode.NotFound);
        await admin.PutJsonAsync(url, new { password = "a-new-password" }, HttpStatusCode.NoContent);

        using var client = factory.CreateClient();
        var login = new { server = ServiceFactory.DemoServer, email = ServiceFactory.EmailOf("T1") };
        await client.PostJsonAsync("/api/auth/login", new { login.server, login.email, password = ServiceFactory.TraderPassword }, HttpStatusCode.Unauthorized);
        await client.PostJsonAsync("/api/auth/login", new { login.server, login.email, password = "a-new-password" });
    }

    [Fact]
    public async Task TheFirmReadsItsOwnAccountsOnly()
    {
        using var factory = new ServiceFactory(settings: SecondFirm.Settings);
        (await factory.CreateTraderClientAsync("T1")).Dispose();
        using var admin = factory.CreateAdminClient();
        using var other = factory.CreateAdminClient(SecondFirm.ApiKey);

        var account = await admin.GetJsonAsync("/api/admin/v1/accounts/T1");
        using var fromOtherFirm = await other.GetAsync(new Uri("/api/admin/v1/accounts/T1", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(100_000m, account.GetProperty("balance").GetDecimal());
        Assert.Equal(HttpStatusCode.NotFound, fromOtherFirm.StatusCode);
    }

    // The prop platform names the account as its portal does, so the terminal shows it the same way (ADR 0035).
    [Fact]
    public async Task TheFirmTellsTheTerminalHowToShowAnAccount()
    {
        using var factory = new ServiceFactory(settings: SecondFirm.Settings);
        using var trader = await factory.CreateTraderClientAsync("T1");
        using var admin = factory.CreateAdminClient();
        using var other = factory.CreateAdminClient(SecondFirm.ApiKey);

        var before = await trader.GetJsonAsync("/api/auth/me");
        var saved = await admin.PutJsonAsync(
            "/api/admin/v1/accounts/T1/details",
            new { label = "#1001 Two-step 100K \u00b7 Phase 1", profitTarget = 110_000m, timeZone = "Europe/Stockholm", detailsUrl = "https://acme.example.com/accounts/1" });
        var after = await trader.GetJsonAsync("/api/auth/me");
        var cleared = await admin.PutJsonAsync("/api/admin/v1/accounts/T1/details", new { label = "", profitTarget = (decimal?)null, timeZone = "", detailsUrl = "" });

        Assert.Equal(("T1", JsonValueKind.Null), (before.GetProperty("accountDetails")[0].GetProperty("accountId").GetString(), before.GetProperty("accountDetails")[0].GetProperty("label").ValueKind));
        Assert.Equal("#1001 Two-step 100K \u00b7 Phase 1", saved.GetProperty("label").GetString());
        var details = Assert.Single(after.GetProperty("accountDetails").EnumerateArray());
        Assert.Equal(
            ("#1001 Two-step 100K \u00b7 Phase 1", 110_000m, "Europe/Stockholm", "https://acme.example.com/accounts/1"),
            (details.GetProperty("label").GetString(), details.GetProperty("profitTarget").GetDecimal(), details.GetProperty("timeZone").GetString(), details.GetProperty("detailsUrl").GetString()));
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null), (cleared.GetProperty("label").ValueKind, cleared.GetProperty("detailsUrl").ValueKind));
        await other.PutJsonAsync("/api/admin/v1/accounts/T1/details", new { label = "Not theirs" }, HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("label", "A label far too long for the account bar, over one hundred characters, which no portal should ever need to send")]
    [InlineData("timeZone", "Mars/Olympus")]
    [InlineData("detailsUrl", "/accounts/1")]
    [InlineData("detailsUrl", "javascript:alert(1)")]
    public async Task AccountDetailsAreChecked(string field, string value)
    {
        using var factory = new ServiceFactory();
        (await factory.CreateTraderClientAsync("T1")).Dispose();
        using var admin = factory.CreateAdminClient();

        await admin.PutJsonAsync("/api/admin/v1/accounts/T1/details", new Dictionary<string, object> { [field] = value }, HttpStatusCode.UnprocessableEntity);
        await admin.PutJsonAsync("/api/admin/v1/accounts/T1/details", new { profitTarget = 0m }, HttpStatusCode.UnprocessableEntity);
    }

    // The prop platform resets this floor at the start of every trading day.
    [Fact]
    public async Task AnAnchoredFloorIsMeasuredWhenItIsSet()
    {
        using var factory = new ServiceFactory();
        (await factory.CreateTraderClientAsync("T1")).Dispose();
        using var admin = factory.CreateAdminClient();

        var response = await admin.PutJsonAsync(
            "/api/admin/v1/accounts/T1/floors/daily",
            new { rule = new { kind = "AnchoredFloor", distance = 5_000m, anchor = "Balance" } });

        var set = response.GetProperty("events")[0].GetProperty("event");
        Assert.Equal(("EquityFloorSet", 95_000m), (set.GetProperty("kind").GetString(), set.GetProperty("level").GetDecimal()));
    }

    // The prop platform withdraws a trader's profit like this when the trader asks for a payout.
    [Fact]
    public async Task TheFirmWithdrawsFromAnAccountOnceAndSeesItInTheEvents()
    {
        using var factory = new ServiceFactory(settings: SecondFirm.Settings);
        (await factory.CreateTraderClientAsync("T1")).Dispose();
        using var admin = factory.CreateAdminClient();
        using var other = factory.CreateAdminClient(SecondFirm.ApiKey);
        var cursor = (await admin.GetJsonAsync("/api/admin/v1/events?limit=1000")).GetProperty("cursor").GetInt64();
        const string url = "/api/admin/v1/accounts/T1/balance-operations";
        var withdrawal = new { operationId = "payout-1", amount = -5_000m, minBalance = 90_000m };

        var response = await admin.PostJsonAsync(url, withdrawal);
        var retry = await admin.PostJsonAsync(url, withdrawal, HttpStatusCode.Conflict);
        var tooMuch = await admin.PostJsonAsync(url, new { operationId = "payout-2", amount = -5_000.01m, minBalance = 90_000m }, HttpStatusCode.UnprocessableEntity);
        await other.PostJsonAsync(url, new { operationId = "payout-3", amount = 1_000m }, HttpStatusCode.NotFound);

        var adjusted = response.GetProperty("events")[0].GetProperty("event");
        Assert.Equal(("BalanceAdjusted", -5_000m, 95_000m), (adjusted.GetProperty("kind").GetString(), adjusted.GetProperty("amount").GetDecimal(), adjusted.GetProperty("balanceAfter").GetDecimal()));
        Assert.Equal(("DuplicateId", "InsufficientFunds"), (retry.GetProperty("reason").GetString(), tooMuch.GetProperty("reason").GetString()));
        Assert.Equal(95_000m, (await admin.GetJsonAsync("/api/admin/v1/accounts/T1")).GetProperty("balance").GetDecimal());
        var events = (await admin.GetJsonAsync($"/api/admin/v1/events?after={cursor}")).GetProperty("events");
        Assert.Equal(["BalanceAdjusted", "InputRejected", "InputRejected"], events.EventKinds());
    }

    // The prop platform suspends a firm's accounts like this while the firm's month is unpaid.
    [Fact]
    public async Task TheFirmSuspendsAndResumesAnAccountAndCanRepeatIt()
    {
        using var factory = new ServiceFactory(settings: SecondFirm.Settings);
        using var trader = await factory.CreateTraderClientAsync("T1");
        using var admin = factory.CreateAdminClient();
        using var other = factory.CreateAdminClient(SecondFirm.ApiKey);

        var suspended = await admin.PostJsonAsync("/api/admin/v1/accounts/T1/suspend");
        var again = await admin.PostJsonAsync("/api/admin/v1/accounts/T1/suspend");
        var order = await trader.PostJsonAsync(
            "/api/accounts/T1/orders",
            new { orderId = "O1", symbol = "EURUSD", side = "Buy", type = "Market", volume = 1.00m },
            HttpStatusCode.UnprocessableEntity);
        var whileSuspended = await admin.GetJsonAsync("/api/admin/v1/accounts/T1");
        await other.PostJsonAsync("/api/admin/v1/accounts/T1/resume", null, HttpStatusCode.NotFound);
        var resumed = await admin.PostJsonAsync("/api/admin/v1/accounts/T1/resume");
        var resumedAgain = await admin.PostJsonAsync("/api/admin/v1/accounts/T1/resume");

        Assert.Equal(["AccountSuspended"], suspended.GetProperty("events").EventKinds());
        Assert.Empty(again.GetProperty("events").EnumerateArray());
        Assert.Equal("AccountSuspended", order.GetProperty("reason").GetString());
        Assert.Equal("Suspended", whileSuspended.GetProperty("status").GetString());
        Assert.Equal(["AccountResumed"], resumed.GetProperty("events").EventKinds());
        Assert.Empty(resumedAgain.GetProperty("events").EnumerateArray());
        Assert.Equal("Active", (await admin.GetJsonAsync("/api/admin/v1/accounts/T1")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task AClosedAccountCannotBeSuspended()
    {
        using var factory = new ServiceFactory();
        (await factory.CreateTraderClientAsync("T1")).Dispose();
        using var admin = factory.CreateAdminClient();
        await admin.PostJsonAsync("/api/admin/v1/accounts/T1/close");

        var refused = await admin.PostJsonAsync("/api/admin/v1/accounts/T1/suspend", null, HttpStatusCode.UnprocessableEntity);

        Assert.Equal("AccountDisabled", refused.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task EachFirmReadsOnlyItsOwnEventsInOrder()
    {
        using var factory = new ServiceFactory(settings: SecondFirm.Settings);
        using var admin = factory.CreateAdminClient();
        using var other = factory.CreateAdminClient(SecondFirm.ApiKey);

        // After the development accounts, which belong to the development firm too.
        var start = (await admin.GetJsonAsync("/api/admin/v1/events?limit=1000")).GetProperty("cursor").GetInt64();
        (await factory.CreateTraderClientAsync("T1")).Dispose();
        await CreateTraderAsync(factory, "X1", SecondFirm.ApiKey, SecondFirm.Group);
        (await factory.CreateTraderClientAsync("T2")).Dispose();

        var first = await admin.GetJsonAsync($"/api/admin/v1/events?after={start}&limit=1");
        var rest = await admin.GetJsonAsync($"/api/admin/v1/events?after={first.GetProperty("cursor").GetInt64()}");
        var otherFirm = await other.GetJsonAsync("/api/admin/v1/events");

        Assert.Equal(["T1"], AccountsOf(first));
        Assert.Equal(["T2"], AccountsOf(rest));
        Assert.Equal(["X1"], AccountsOf(otherFirm));
        Assert.True(rest.GetProperty("cursor").GetInt64() > first.GetProperty("cursor").GetInt64());
    }

    [Fact]
    public async Task AWaitingRequestGetsTheNextEventAtOnce()
    {
        using var factory = new ServiceFactory();
        (await factory.CreateTraderClientAsync("T1")).Dispose();
        using var admin = factory.CreateAdminClient();
        var cursor = (await admin.GetJsonAsync("/api/admin/v1/events")).GetProperty("cursor").GetInt64();

        var waiting = admin.GetJsonAsync($"/api/admin/v1/events?after={cursor}&wait=30");
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(waiting.IsCompleted);
        await admin.PostJsonAsync("/api/admin/v1/accounts/T1/close");

        var events = await waiting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Contains("AccountDisabled", events.GetProperty("events").EventKinds());
    }

    [Fact]
    public async Task AWaitingRequestEndsEmptyWhenNothingHappens()
    {
        using var factory = new ServiceFactory();
        (await factory.CreateTraderClientAsync("T1")).Dispose();
        using var admin = factory.CreateAdminClient();
        var cursor = (await admin.GetJsonAsync("/api/admin/v1/events")).GetProperty("cursor").GetInt64();

        var waiting = admin.GetJsonAsync($"/api/admin/v1/events?after={cursor}&wait=5");
        await Eventually.ThatAsync(
            () =>
            {
                factory.Time.Advance(TimeSpan.FromSeconds(1));
                return waiting.IsCompleted;
            },
            "the wait to end");

        var response = await waiting;
        Assert.Empty(response.GetProperty("events").EnumerateArray());
        Assert.Equal(cursor, response.GetProperty("cursor").GetInt64());
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=1001")]
    [InlineData("wait=31")]
    [InlineData("after=-1")]
    public async Task EventRequestsOutsideTheLimitsAreRefused(string query)
    {
        using var factory = new ServiceFactory();
        using var admin = factory.CreateAdminClient();

        using var response = await admin.GetAsync(new Uri($"/api/admin/v1/events?{query}", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task ALoginLinkLogsTheTraderInOnce()
    {
        using var factory = new ServiceFactory();
        var userId = await CreateTraderAsync(factory, "T1");
        using var admin = factory.CreateAdminClient();

        var link = await admin.PostJsonAsync($"/api/admin/v1/users/{userId}/login-links", new { accountId = "T1" });
        var url = new Uri(link.GetProperty("url").GetString()!);
        var token = Query(url, "token");

        Assert.Equal("http://localhost:3001/login/link", url.GetLeftPart(UriPartial.Path));
        Assert.Equal("T1", Query(url, "account"));
        Assert.Equal(factory.Time.GetUtcNow().AddMinutes(2), link.GetProperty("expiresAt").GetDateTimeOffset());

        using var browser = factory.CreateClient();
        var me = await browser.PostJsonAsync("/api/auth/link", new { token });
        var session = await browser.GetJsonAsync("/api/auth/me");
        var again = await factory.CreateClient().PostJsonAsync("/api/auth/link", new { token }, HttpStatusCode.Unauthorized);

        Assert.Equal(["T1"], me.GetProperty("accounts").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal(ServiceFactory.EmailOf("T1"), session.GetProperty("email").GetString());
        Assert.Equal("The link has expired or was already used.", again.GetProperty("title").GetString());
    }

    [Fact]
    public async Task ALoginLinkExpiresAfterTwoMinutes()
    {
        using var factory = new ServiceFactory();
        var userId = await CreateTraderAsync(factory, "T1");
        using var admin = factory.CreateAdminClient();
        var link = await admin.PostJsonAsync($"/api/admin/v1/users/{userId}/login-links");

        factory.Time.Advance(TimeSpan.FromMinutes(2));

        await factory.CreateClient().PostJsonAsync(
            "/api/auth/link",
            new { token = Query(new Uri(link.GetProperty("url").GetString()!), "token") },
            HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LoginLinksAreOnlyForTheFirmsTradersAndTheirAccounts()
    {
        using var factory = new ServiceFactory(settings: SecondFirm.Settings);
        var userId = await CreateTraderAsync(factory, "T1");
        (await factory.CreateTraderClientAsync("T2")).Dispose();
        using var admin = factory.CreateAdminClient();
        using var other = factory.CreateAdminClient(SecondFirm.ApiKey);

        await other.PostJsonAsync($"/api/admin/v1/users/{userId}/login-links", null, HttpStatusCode.NotFound);
        await admin.PostJsonAsync($"/api/admin/v1/users/{userId}/login-links", new { accountId = "T2" }, HttpStatusCode.NotFound);
    }

    /// <summary>Creates a trader with an account at the firm, and returns the trader's user id.</summary>
    private static async Task<Guid> CreateTraderAsync(ServiceFactory factory, string accountId, string apiKey = ServiceFactory.AdminApiKey, string groupId = "standard")
    {
        using var admin = factory.CreateAdminClient(apiKey);
        var user = await admin.PostJsonAsync("/api/admin/v1/users", new { email = ServiceFactory.EmailOf(accountId), password = ServiceFactory.TraderPassword });
        var userId = user.GetProperty("userId").GetGuid();
        await admin.PostJsonAsync("/api/admin/v1/accounts", new { accountId, groupId, initialBalance = 100_000m, ownerUserId = userId });
        return userId;
    }

    private static List<string?> AccountsOf(JsonElement response) =>
        [.. response.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("event").GetProperty("accountId").GetString()).Distinct()];

    private static string Query(Uri url, string name) =>
        Uri.UnescapeDataString(url.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).Single(p => p[0] == name)[1]);
}
