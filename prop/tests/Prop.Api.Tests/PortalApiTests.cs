using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;

using Prop.Api.Tests.Support;

namespace Prop.Api.Tests;

/// <summary>The firm's white label portal: its look, logins, the trader's own accounts and the admin panel.</summary>
public sealed class PortalApiTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Password = PropFactory.TraderPassword;

    private const string Phase1 = "demo-firm-1001-1";

    [Fact]
    public async Task ThePortalLooksLikeTheFirmWhoseAddressItIsOn()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), PropFactory.WithOtherFirm());
        using var demo = factory.CreatePortalClient();
        using var other = factory.CreatePortalClient(PropFactory.OtherFirmHost);
        using var unknown = factory.CreatePortalClient("portal.unknown.test");

        var demoBranding = await demo.GetFromJsonAsync<JsonElement>(Url("branding"), TestContext.Current.CancellationToken);
        var otherBranding = await other.GetFromJsonAsync<JsonElement>(Url("branding"), TestContext.Current.CancellationToken);
        using var nothing = await unknown.GetAsync(Url("branding"), TestContext.Current.CancellationToken);

        Assert.Equal(("Demo Firm", "#8b5cf6"), (demoBranding.GetProperty("name").GetString(), demoBranding.GetProperty("colors").GetProperty("accent").GetString()));
        Assert.Equal("Other Firm", otherBranding.GetProperty("name").GetString());
        Assert.Empty(otherBranding.GetProperty("colors").EnumerateObject());
        Assert.Equal(HttpStatusCode.NotFound, nothing.StatusCode);
    }

    [Fact]
    public async Task ATraderChoosesAPasswordWithTheInvitationAndThenLogsInWithIt()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var id = await StartAsync(factory);
        var token = await factory.InviteAsync(id);
        using var portal = factory.CreatePortalClient();

        using var accepted = await portal.PostAsJsonAsync(Url("invites/accept"), new { token, password = Password }, TestContext.Current.CancellationToken);
        var me = await portal.GetFromJsonAsync<JsonElement>(Url("me"), TestContext.Current.CancellationToken);
        using var loggedOut = await portal.PostAsync(Url("logout"), null, TestContext.Current.CancellationToken);
        using var afterLogout = await portal.GetAsync(Url("me"), TestContext.Current.CancellationToken);
        using var login = await portal.PostAsJsonAsync(Url("login"), new { email = "ANNA@test.example", password = Password }, TestContext.Current.CancellationToken);
        var accounts = await portal.GetFromJsonAsync<JsonElement>(Url("accounts"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(("anna@test.example", "trader", "Demo Firm"), (me.GetProperty("email").GetString(), me.GetProperty("role").GetString(), me.GetProperty("firmName").GetString()));
        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.Unauthorized, HttpStatusCode.OK), (loggedOut.StatusCode, afterLogout.StatusCode, login.StatusCode));
        Assert.Equal(id, Assert.Single(accounts.EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task AnInvitationWorksOnceAndOnlyUntilItExpires()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var anna = await StartAsync(factory);
        var bert = await StartAsync(factory, "bert@test.example");
        var annasToken = await factory.InviteAsync(anna);
        var bertsToken = await factory.InviteAsync(bert);
        using var portal = factory.CreatePortalClient();

        // A password that is too short is refused without using up the invitation.
        using var tooShort = await portal.PostAsJsonAsync(Url("invites/accept"), new { token = annasToken, password = "short" }, TestContext.Current.CancellationToken);
        using var first = await portal.PostAsJsonAsync(Url("invites/accept"), new { token = annasToken, password = Password }, TestContext.Current.CancellationToken);
        using var again = await portal.PostAsJsonAsync(Url("invites/accept"), new { token = annasToken, password = "another-password" }, TestContext.Current.CancellationToken);
        using var made = await portal.PostAsJsonAsync(Url("invites/accept"), new { token = "made-up", password = Password }, TestContext.Current.CancellationToken);
        await factory.AdvanceAsync(TimeSpan.FromDays(7));
        using var expired = await portal.PostAsJsonAsync(Url("invites/accept"), new { token = bertsToken, password = Password }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooShort.StatusCode);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (again.StatusCode, made.StatusCode, expired.StatusCode));
    }

    [Fact]
    public async Task ANewInvitationReplacesTheOlderOneAndResetsAForgottenPassword()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var id = await StartAsync(factory);
        var first = await factory.InviteAsync(id);
        var second = await factory.InviteAsync(id);
        using var portal = factory.CreatePortalClient();

        using var older = await portal.PostAsJsonAsync(Url("invites/accept"), new { token = first, password = Password }, TestContext.Current.CancellationToken);
        using var newest = await portal.PostAsJsonAsync(Url("invites/accept"), new { token = second, password = Password }, TestContext.Current.CancellationToken);
        var reset = await factory.InviteAsync(id);
        using var newPassword = await portal.PostAsJsonAsync(Url("invites/accept"), new { token = reset, password = "a-new-password" }, TestContext.Current.CancellationToken);
        using var oldLogin = await portal.PostAsJsonAsync(Url("login"), new { email = "anna@test.example", password = Password }, TestContext.Current.CancellationToken);
        using var newLogin = await portal.PostAsJsonAsync(Url("login"), new { email = "anna@test.example", password = "a-new-password" }, TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.OK, HttpStatusCode.OK), (older.StatusCode, newest.StatusCode, newPassword.StatusCode));
        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.OK), (oldLogin.StatusCode, newLogin.StatusCode));
    }

    [Fact]
    public async Task OnlyTheRightPasswordLogsIn()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        await factory.StartActiveAccountAsync("bert@test.example");
        using var anna = await factory.LogInAsTraderAsync(await StartAsync(factory));
        using var portal = factory.CreatePortalClient();

        using var wrong = await portal.PostAsJsonAsync(Url("login"), new { email = "anna@test.example", password = "wrong-password" }, TestContext.Current.CancellationToken);
        using var unknown = await portal.PostAsJsonAsync(Url("login"), new { email = "nobody@test.example", password = Password }, TestContext.Current.CancellationToken);

        // Bert has an account but was never invited, so has no password yet.
        using var uninvited = await portal.PostAsJsonAsync(Url("login"), new { email = "bert@test.example", password = "" }, TestContext.Current.CancellationToken);
        using var admin = await portal.PostAsJsonAsync(Url("login"), new { email = PropFactory.AdminEmail, password = PropFactory.AdminPassword }, TestContext.Current.CancellationToken);
        using var me = await portal.GetAsync(Url("me"), TestContext.Current.CancellationToken);

        Assert.All([wrong, unknown, uninvited, admin, me], r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
    }

    [Fact]
    public async Task TheLoginLimitCountsEachBrowserBehindThePortal()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var first = factory.CreatePortalClient();
        first.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.1");
        using var second = factory.CreatePortalClient();
        second.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.2");
        var wrong = new { email = "anna@test.example", password = "wrong-password" };

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 11; attempt++)
        {
            using var response = await first.PostAsJsonAsync(Url("login"), wrong, TestContext.Current.CancellationToken);
            statuses.Add(response.StatusCode);
        }

        using var other = await second.PostAsJsonAsync(Url("login"), wrong, TestContext.Current.CancellationToken);

        Assert.Equal([.. Enumerable.Repeat(HttpStatusCode.Unauthorized, 10), HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);
    }

    [Fact]
    public async Task ATraderSeesAndTradesOnlyTheirOwnAccounts()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var anna = await StartAsync(factory);
        var bert = await StartAsync(factory, "bert@test.example");
        using var portal = await factory.LogInAsTraderAsync(anna);

        var accounts = await portal.GetFromJsonAsync<JsonElement>(Url("accounts"), TestContext.Current.CancellationToken);
        using var bertsAccount = await portal.GetAsync(Url($"accounts/{bert}"), TestContext.Current.CancellationToken);
        using var bertsLink = await portal.PostAsync(Url($"accounts/{bert}/terminal-link"), null, TestContext.Current.CancellationToken);
        using var ownLink = await portal.PostAsync(Url($"accounts/{anna}/terminal-link"), null, TestContext.Current.CancellationToken);

        Assert.Equal(anna, Assert.Single(accounts.EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (bertsAccount.StatusCode, bertsLink.StatusCode));
        var link = await ownLink.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.EndsWith($"account={Phase1}", link.GetProperty("url").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDashboardValuesTheAccountNowAndShowsTheBreachThatFailedIt()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var id = await StartAsync(factory);
        using var portal = await factory.LogInAsTraderAsync(id);
        factory.Trading.SetEquity(Phase1, 98_500m);

        var active = await portal.GetFromJsonAsync<JsonElement>(Url($"accounts/{id}"), TestContext.Current.CancellationToken);
        factory.Trading.FailNext(1);
        var unreachable = await portal.GetFromJsonAsync<JsonElement>(Url($"accounts/{id}"), TestContext.Current.CancellationToken);
        factory.Trading.Breach(Phase1, "daily", 94_900m);
        await factory.WaitForAccountAsync(id, a => a.GetProperty("status").GetString() == "Failed");
        var failed = await portal.GetFromJsonAsync<JsonElement>(Url($"accounts/{id}"), TestContext.Current.CancellationToken);

        var live = active.GetProperty("live");
        Assert.Equal((100_000m, 98_500m), (live.GetProperty("balance").GetDecimal(), live.GetProperty("equity").GetDecimal()));
        var floors = live.GetProperty("floors").EnumerateArray().ToDictionary(f => f.GetProperty("floorId").GetString()!, f => f.GetProperty("headroom").GetDecimal());
        Assert.Equal(new Dictionary<string, decimal> { ["daily"] = 3_500m, ["max-loss"] = 8_500m }, floors);
        Assert.Equal(JsonValueKind.Null, unreachable.GetProperty("live").ValueKind);
        Assert.Equal(100_000m, unreachable.GetProperty("account").GetProperty("balance").GetDecimal());

        var breach = failed.GetProperty("breach");
        Assert.Equal(JsonValueKind.Null, failed.GetProperty("live").ValueKind);
        Assert.Equal(("daily", 95_000m, 94_900m, "DailyLoss"), (breach.GetProperty("floorId").GetString(), breach.GetProperty("level").GetDecimal(), breach.GetProperty("equity").GetDecimal(), breach.GetProperty("reason").GetString()));
    }

    [Fact]
    public async Task ASessionAndAnInvitationWorkOnlyOnTheirOwnFirmsPortal()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), PropFactory.WithOtherFirm());
        using var portal = await factory.LogInAsTraderAsync(await StartAsync(factory));
        var bert = await StartAsync(factory, "bert@test.example");
        var token = await factory.InviteAsync(bert);

        // The same browser, with the session cookie, on the other firm's portal.
        portal.DefaultRequestHeaders.Add("X-Forwarded-Host", PropFactory.OtherFirmHost);
        using var me = await portal.GetAsync(Url("me"), TestContext.Current.CancellationToken);
        using var other = factory.CreatePortalClient(PropFactory.OtherFirmHost);
        using var accepted = await other.PostAsJsonAsync(Url("invites/accept"), new { token, password = Password }, TestContext.Current.CancellationToken);
        using var demo = factory.CreatePortalClient();
        using var acceptedAtHome = await demo.PostAsJsonAsync(Url("invites/accept"), new { token, password = Password }, TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (me.StatusCode, accepted.StatusCode));
        Assert.Equal(HttpStatusCode.OK, acceptedAtHome.StatusCode);
    }

    [Fact]
    public async Task TheAdminPanelIsForTheFirmsAdministratorsOnly()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var anonymous = factory.CreatePortalClient();
        using var trader = await factory.LogInAsTraderAsync(await StartAsync(factory));
        using var admin = await factory.LogInAsAdminAsync();

        using var asAnonymous = await anonymous.GetAsync(Url("admin/accounts"), TestContext.Current.CancellationToken);
        using var asTrader = await trader.GetAsync(Url("admin/accounts"), TestContext.Current.CancellationToken);
        using var asAdmin = await admin.GetAsync(Url("admin/accounts"), TestContext.Current.CancellationToken);
        using var adminAsTrader = await admin.GetAsync(Url("accounts"), TestContext.Current.CancellationToken);
        var me = await admin.GetFromJsonAsync<JsonElement>(Url("admin/me"), TestContext.Current.CancellationToken);

        // Each role has its own session, so a trader's is no admin session and the other way around.
        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (asAnonymous.StatusCode, asTrader.StatusCode));
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.Unauthorized), (asAdmin.StatusCode, adminAsTrader.StatusCode));
        Assert.Equal((PropFactory.AdminEmail, "admin"), (me.GetProperty("email").GetString(), me.GetProperty("role").GetString()));
    }

    [Fact]
    public async Task AnAdministratorAndATraderAreLoggedInAtOnceInOneBrowser()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var token = await factory.InviteAsync(await StartAsync(factory));
        using var browser = await factory.LogInAsAdminAsync();

        using var accepted = await browser.PostAsJsonAsync(Url("invites/accept"), new { token, password = Password }, TestContext.Current.CancellationToken);
        var trader = await browser.GetFromJsonAsync<JsonElement>(Url("me"), TestContext.Current.CancellationToken);
        var admin = await browser.GetFromJsonAsync<JsonElement>(Url("admin/me"), TestContext.Current.CancellationToken);
        using var traderLogout = await browser.PostAsync(Url("logout"), null, TestContext.Current.CancellationToken);
        using var traderAfter = await browser.GetAsync(Url("me"), TestContext.Current.CancellationToken);
        using var adminAfter = await browser.GetAsync(Url("admin/me"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(("anna@test.example", PropFactory.AdminEmail), (trader.GetProperty("email").GetString(), admin.GetProperty("email").GetString()));
        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.OK), (traderAfter.StatusCode, adminAfter.StatusCode));
    }

    [Fact]
    public async Task ConfiguredTradersLogInWithTheConfiguredPasswordAfterEveryStart()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var id = await StartAsync(factory, "anna@test.com");
        using var portal = factory.CreatePortalClient();

        // The development configuration has anna@test.com with the password anna.
        using var configured = await portal.PostAsJsonAsync(Url("login"), new { email = "anna@test.com", password = "anna" }, TestContext.Current.CancellationToken);
        var accounts = await portal.GetFromJsonAsync<JsonElement>(Url("accounts"), TestContext.Current.CancellationToken);
        using var changed = await portal.PostAsJsonAsync(Url("invites/accept"), new { token = await factory.InviteAsync(id), password = Password }, TestContext.Current.CancellationToken);
        await using var restarted = factory.Restart();
        using var afterRestart = restarted.CreatePortalClient();
        using var again = await afterRestart.PostAsJsonAsync(Url("login"), new { email = "anna@test.com", password = "anna" }, TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK), (configured.StatusCode, changed.StatusCode, again.StatusCode));
        Assert.Equal(id, Assert.Single(accounts.EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task TheLoginRulesCanBeTurnedOffForDevelopment()
    {
        var noRules = new Dictionary<string, string> { ["Login:MinimumPasswordLength"] = "1", ["Login:AttemptsPerMinute"] = "0" };
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), noRules);
        var token = await factory.InviteAsync(await StartAsync(factory));
        using var portal = factory.CreatePortalClient();

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 15; attempt++)
        {
            using var response = await portal.PostAsJsonAsync(Url("login"), new { email = "anna@test.example", password = "wrong" }, TestContext.Current.CancellationToken);
            statuses.Add(response.StatusCode);
        }

        using var empty = await portal.PostAsJsonAsync(Url("invites/accept"), new { token, password = "" }, TestContext.Current.CancellationToken);
        using var oneCharacter = await portal.PostAsJsonAsync(Url("invites/accept"), new { token, password = "a" }, TestContext.Current.CancellationToken);

        Assert.All(statuses, s => Assert.Equal(HttpStatusCode.Unauthorized, s));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, HttpStatusCode.OK), (empty.StatusCode, oneCharacter.StatusCode));
    }

    [Fact]
    public async Task AnAdministratorRunsTheFirmsAccounts()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.LogInAsAdminAsync();

        var challenges = await admin.GetFromJsonAsync<JsonElement>(Url("admin/challenges"), TestContext.Current.CancellationToken);
        using var started = await admin.PostAsJsonAsync(Url("admin/accounts"), new { email = "anna@test.example", challengeId = "two-step-100k" }, TestContext.Current.CancellationToken);
        var id = (await started.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
        await factory.WaitForAccountAsync(id, a => a.GetProperty("status").GetString() == "Active" && a.GetProperty("dailyFloor").ValueKind == JsonValueKind.Number);
        await factory.StartActiveAccountAsync("bert@test.example");

        var all = await admin.GetFromJsonAsync<JsonElement>(Url("admin/accounts"), TestContext.Current.CancellationToken);
        var annas = await admin.GetFromJsonAsync<JsonElement>(Url("admin/accounts?email=Anna@test.example"), TestContext.Current.CancellationToken);
        var failed = await admin.GetFromJsonAsync<JsonElement>(Url("admin/accounts?status=Failed"), TestContext.Current.CancellationToken);
        using var badLimit = await admin.GetAsync(Url("admin/accounts?limit=0"), TestContext.Current.CancellationToken);
        var details = await admin.GetFromJsonAsync<JsonElement>(Url($"admin/accounts/{id}"), TestContext.Current.CancellationToken);
        var history = await admin.GetFromJsonAsync<JsonElement>(Url($"admin/accounts/{id}/history"), TestContext.Current.CancellationToken);
        using var approve = await admin.PostAsync(Url($"admin/accounts/{id}/approve-funding"), null, TestContext.Current.CancellationToken);
        using var invite = await admin.PostAsync(Url($"admin/accounts/{id}/invite"), null, TestContext.Current.CancellationToken);
        using var cancel = await admin.PostAsJsonAsync(Url($"admin/accounts/{id}/cancel"), new { reason = "Refunded." }, TestContext.Current.CancellationToken);

        Assert.Equal(["quick-test-100k", "two-step-100k"], challenges.EnumerateArray().Select(c => c.GetProperty("id").GetString()));
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        Assert.Equal($"/api/portal/admin/accounts/{id}", started.Headers.Location?.OriginalString);
        Assert.Equal(["bert@test.example", "anna@test.example"], all.EnumerateArray().Select(a => a.GetProperty("email").GetString()));
        Assert.Equal(id, Assert.Single(annas.EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Empty(failed.EnumerateArray());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badLimit.StatusCode);
        Assert.Equal(100_000m, details.GetProperty("live").GetProperty("equity").GetDecimal());
        Assert.True(history.GetArrayLength() > 0);
        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
        Assert.StartsWith("http://localhost:3002/invite?token=", (await invite.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("url").GetString(), StringComparison.Ordinal);
        Assert.Equal("Cancelled", (await cancel.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task AnAdministratorSeesOnlyTheirOwnFirm()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), PropFactory.WithOtherFirm());
        var id = await StartAsync(factory);
        using var portal = factory.CreatePortalClient(PropFactory.OtherFirmHost);

        // The same email is a different administrator at each firm, with its own password.
        using var demoPassword = await portal.PostAsJsonAsync(Url("admin/login"), new { email = PropFactory.AdminEmail, password = PropFactory.AdminPassword }, TestContext.Current.CancellationToken);
        using var login = await portal.PostAsJsonAsync(Url("admin/login"), new { email = PropFactory.AdminEmail, password = "other-admin-password" }, TestContext.Current.CancellationToken);
        var accounts = await portal.GetFromJsonAsync<JsonElement>(Url("admin/accounts"), TestContext.Current.CancellationToken);
        using var demoAccount = await portal.GetAsync(Url($"admin/accounts/{id}"), TestContext.Current.CancellationToken);
        using var demoInvite = await portal.PostAsync(Url($"admin/accounts/{id}/invite"), null, TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.OK), (demoPassword.StatusCode, login.StatusCode));
        Assert.Empty(accounts.EnumerateArray());
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (demoAccount.StatusCode, demoInvite.StatusCode));
    }

    [Fact]
    public async Task ASessionSurvivesARestart()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var login = await browser.PostAsJsonAsync(Url("admin/login"), new { email = PropFactory.AdminEmail, password = PropFactory.AdminPassword }, TestContext.Current.CancellationToken);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';')[0];

        await using var restarted = factory.Restart();
        using var afterRestart = restarted.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        afterRestart.DefaultRequestHeaders.Add("Cookie", cookie);
        using var me = await afterRestart.GetAsync(Url("admin/me"), TestContext.Current.CancellationToken);

        Assert.StartsWith("prop_admin=", cookie, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    private static Uri Url(string path) => new($"/api/portal/{path}", UriKind.Relative);

    private static async Task<Guid> StartAsync(PropFactory factory, string email = "anna@test.example") =>
        (await factory.StartActiveAccountAsync(email)).GetProperty("id").GetGuid();
}
