using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Prop.Api.Tests.Support;

namespace Prop.Api.Tests;

/// <summary>Firms that sign up themselves, get a trading server in the background and start in the sandbox (ADR 0017).</summary>
public sealed class SignupTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly Dictionary<string, string> WithEmailConfirmation = new() { ["Signup:RequireEmailVerification"] = "true" };

    [Fact]
    public async Task AFirmSignsUpAndIsInTheSandboxWithAChallengeAtOnce()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());

        using var admin = await factory.SignUpAsync("acme");
        await PropFactory.WaitUntilProvisionedAsync(admin);

        var me = await admin.GetFromJsonAsync<JsonElement>(Url("admin/me"), TestContext.Current.CancellationToken);
        var firm = await admin.GetFromJsonAsync<JsonElement>(Url("admin/firm"), TestContext.Current.CancellationToken);
        var branding = await admin.GetFromJsonAsync<JsonElement>(Url("branding"), TestContext.Current.CancellationToken);
        var challenges = await admin.GetFromJsonAsync<JsonElement>(Url("admin/challenges"), TestContext.Current.CancellationToken);

        Assert.Equal(("owner@firm.test", "admin", "Firm acme"), (me.GetProperty("email").GetString(), me.GetProperty("role").GetString(), me.GetProperty("firmName").GetString()));
        Assert.Equal(
            ("acme", "Sandbox", "http://acme.localhost:3002/", "acme", "USD", 10),
            (firm.GetProperty("id").GetString(), firm.GetProperty("status").GetString(), firm.GetProperty("portalUrl").GetString(),
                firm.GetProperty("tradingServer").GetString(), firm.GetProperty("currency").GetString(), firm.GetProperty("sandboxMaxOpenAccounts").GetInt32()));
        Assert.Equal("Sandbox", branding.GetProperty("status").GetString());
        Assert.Equal(["two-step-100k"], challenges.EnumerateArray().Select(c => c.GetProperty("id").GetString()));
        Assert.Contains("create server acme USD", factory.Trading.Commands);
    }

    [Fact]
    public async Task ANewFirmRunsTheWholeChainOnItsOwnServer()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");
        await PropFactory.WaitUntilProvisionedAsync(admin);

        using var started = await admin.PostAsJsonAsync(
            Url("admin/accounts"), new { email = "anna@test.example", challengeId = "two-step-100k" }, TestContext.Current.CancellationToken);
        var id = (await started.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
        await Eventually.ThatAsync(
            async () => (await admin.GetFromJsonAsync<JsonElement>(Url($"admin/accounts/{id}"), TestContext.Current.CancellationToken))
                .GetProperty("account").GetProperty("status").GetString() == "Active",
            "the new firm's account to open");

        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var account = factory.Trading.AccountOf("acme-1001-1");
        Assert.Equal(("acme-standard", 100_000m), (account.Group, account.Balance));
    }

    [Fact]
    public async Task WithEmailConfirmationTheFirmIsCreatedWhenTheLinkIsOpened()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), WithEmailConfirmation);
        using var platform = factory.CreatePlatformClient();
        using var acme = factory.CreatePortalClient(PropFactory.HostOf("acme"));

        using var signedUp = await SignUpAsync(platform, "acme", "owner@firm.test");
        using var before = await acme.GetAsync(Url("branding"), TestContext.Current.CancellationToken);
        var email = Assert.Single(factory.Emails.Sent);
        var token = factory.Emails.TokenFor("owner@firm.test");
        using var verified = await platform.PostAsJsonAsync(Url("signup/verify"), new { token }, TestContext.Current.CancellationToken);
        var adminUrl = new Uri((await verified.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("adminUrl").GetString()!);
        using var again = await platform.PostAsJsonAsync(Url("signup/verify"), new { token }, TestContext.Current.CancellationToken);
        using var admin = await factory.WelcomeAsync(adminUrl);

        Assert.Equal(HttpStatusCode.Accepted, signedUp.StatusCode);
        Assert.True((await signedUp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("verificationRequired").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, before.StatusCode);
        Assert.Contains("http://app.localhost:3002/verify?token=", email.Body, StringComparison.Ordinal);
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.Unauthorized), (verified.StatusCode, again.StatusCode));
        Assert.Equal("acme.localhost", adminUrl.Host);
        Assert.Equal("Firm acme", (await admin.GetFromJsonAsync<JsonElement>(Url("admin/me"), TestContext.Current.CancellationToken)).GetProperty("firmName").GetString());
    }

    [Fact]
    public async Task AConfirmationLinkWorksForADay()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), WithEmailConfirmation);
        using var platform = factory.CreatePlatformClient();
        (await SignUpAsync(platform, "acme", "owner@firm.test")).Dispose();

        await factory.AdvanceAsync(TimeSpan.FromHours(24));
        using var expired = await platform.PostAsJsonAsync(Url("signup/verify"), new { token = factory.Emails.TokenFor("owner@firm.test") }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Theory]
    [InlineData("a", false)]
    [InlineData("Acme", false)]
    [InlineData("acme-", false)]
    [InlineData("-acme", false)]
    [InlineData("acme_prop", false)]
    [InlineData("app", false)]
    [InlineData("www", false)]
    [InlineData("demo-firm", false)]
    [InlineData("acme", true)]
    [InlineData("acme-prop-2", true)]
    public async Task ShortNamesMustBeValidFreeAndNotReserved(string firmId, bool available)
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var platform = factory.CreatePlatformClient();

        var answer = await platform.GetFromJsonAsync<JsonElement>(Url($"signup/availability?firmId={firmId}"), TestContext.Current.CancellationToken);

        Assert.Equal(available, answer.GetProperty("available").GetBoolean());
        Assert.Equal(available, answer.GetProperty("reason").ValueKind == JsonValueKind.Null);
    }

    // A name that is taken or reserved comes with free names like it, so the firm need not guess.
    [Fact]
    public async Task ANameThatIsTakenComesWithFreeOnesLikeIt()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var acme = await factory.SignUpAsync("acme");
        using var acmeCapital = await factory.SignUpAsync("acme-capital", "other@firm.test");
        using var platform = factory.CreatePlatformClient();

        var taken = await platform.GetFromJsonAsync<JsonElement>(Url("signup/availability?firmId=acme"), TestContext.Current.CancellationToken);
        var reserved = await platform.GetFromJsonAsync<JsonElement>(Url("signup/availability?firmId=www"), TestContext.Current.CancellationToken);
        var free = await platform.GetFromJsonAsync<JsonElement>(Url("signup/availability?firmId=beta"), TestContext.Current.CancellationToken);

        Assert.Equal((false, "That name is taken."), (taken.GetProperty("available").GetBoolean(), taken.GetProperty("reason").GetString()));
        Assert.Equal(["acme-fx", "acme-trading", "acme-funded"], taken.GetProperty("suggestions").EnumerateArray().Select(s => s.GetString()));
        Assert.Equal(["www-capital", "www-fx", "www-trading"], reserved.GetProperty("suggestions").EnumerateArray().Select(s => s.GetString()));
        Assert.Empty(free.GetProperty("suggestions").EnumerateArray());
    }

    // Its trading server, its accounts and its first challenge are in the currency the firm chose.
    [Fact]
    public async Task AFirmChoosesTheCurrencyOfItsAccounts()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var platform = factory.CreatePlatformClient();

        var about = await platform.GetFromJsonAsync<JsonElement>(Url("platform"), TestContext.Current.CancellationToken);
        using var refused = await platform.PostAsJsonAsync(
            Url("signup"),
            new { firmName = "Firm beta", firmId = "beta", email = "owner@beta.test", password = PropFactory.SignupPassword, acceptTerms = true, currency = "SEK" },
            TestContext.Current.CancellationToken);
        using var signedUp = await platform.PostAsJsonAsync(
            Url("signup"),
            new { firmName = "Firm acme", firmId = "acme", email = "owner@firm.test", password = PropFactory.SignupPassword, acceptTerms = true, currency = "EUR" },
            TestContext.Current.CancellationToken);
        using var admin = await factory.WelcomeAsync(new Uri((await signedUp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("adminUrl").GetString()!));
        await PropFactory.WaitUntilProvisionedAsync(admin);
        var firm = await admin.GetFromJsonAsync<JsonElement>(Url("admin/firm"), TestContext.Current.CancellationToken);
        var challenge = Assert.Single((await admin.GetFromJsonAsync<JsonElement>(Url("admin/challenges"), TestContext.Current.CancellationToken)).EnumerateArray());

        Assert.Equal(["USD", "EUR", "GBP"], about.GetProperty("currencies").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Contains("create server acme EUR", factory.Trading.Commands);
        Assert.Equal(("EUR", "EUR"), (firm.GetProperty("currency").GetString(), challenge.GetProperty("currency").GetString()));
    }

    // So the administrator finds the admin panel again, with the first steps.
    [Fact]
    public async Task ANewFirmIsWelcomedByEmail()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());

        using var admin = await factory.SignUpAsync("acme");
        var welcome = await factory.Emails.WaitForAsync("owner@firm.test", "Welcome to Kronant Prop");

        Assert.Equal("Welcome to Kronant Prop: Firm acme is ready to try", welcome.Subject);
        Assert.Contains("http://acme.localhost:3002/admin/login", welcome.Body, StringComparison.Ordinal);
        Assert.Contains("Give your first challenge a price", welcome.Body, StringComparison.Ordinal);
    }

    // The name waits for whoever signed up with it, but that person may sign up again.
    [Fact]
    public async Task ANameWaitingForConfirmationIsTakenForOthers()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), WithEmailConfirmation);
        using var platform = factory.CreatePlatformClient();
        (await SignUpAsync(platform, "acme", "owner@firm.test")).Dispose();

        var availability = await platform.GetFromJsonAsync<JsonElement>(Url("signup/availability?firmId=acme"), TestContext.Current.CancellationToken);
        using var someoneElse = await SignUpAsync(platform, "acme", "other@firm.test");
        using var sameOwner = await SignUpAsync(platform, "acme", "OWNER@firm.test");
        using var firstLink = await platform.PostAsJsonAsync(Url("signup/verify"), new { token = factory.Emails.Sent[0].Body.Split("token=")[1].Split('\n')[0].Trim() }, TestContext.Current.CancellationToken);

        Assert.False(availability.GetProperty("available").GetBoolean());
        Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.Accepted), (someoneElse.StatusCode, sameOwner.StatusCode));
        Assert.Equal(HttpStatusCode.Unauthorized, firstLink.StatusCode);
    }

    [Fact]
    public async Task ANameTakenOnTheTradingPlatformIsRefused()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        factory.Trading.TakenServers.Add("acme");
        using var platform = factory.CreatePlatformClient();

        using var response = await SignUpAsync(platform, "acme", "owner@firm.test");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("firmId", (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("field").GetString());
    }

    [Theory]
    [InlineData("A", "acme", "owner@firm.test", "a-signup-password", true, "firmName")]
    [InlineData("Acme", "acme", "not-an-email", "a-signup-password", true, "email")]
    [InlineData("Acme", "acme", "owner@firm.test", "short", true, "password")]
    [InlineData("Acme", "acme", "owner@firm.test", "a-signup-password", false, "acceptTerms")]
    [InlineData("Acme", "Acme Prop", "owner@firm.test", "a-signup-password", true, "firmId")]
    public async Task TheSignupFormIsChecked(string firmName, string firmId, string email, string password, bool acceptTerms, string field)
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var platform = factory.CreatePlatformClient();

        using var response = await platform.PostAsJsonAsync(Url("signup"), new { firmName, firmId, email, password, acceptTerms }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(field, (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("field").GetString());
    }

    [Fact]
    public async Task SignupIsOnlyOnThePlatformsAddress()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var platform = factory.CreatePlatformClient();
        using var firmPortal = factory.CreatePortalClient();

        var about = await platform.GetFromJsonAsync<JsonElement>(Url("platform"), TestContext.Current.CancellationToken);
        using var onFirmPortal = await SignUpAsync(firmPortal, "acme", "owner@firm.test");
        using var platformOnFirmPortal = await firmPortal.GetAsync(Url("platform"), TestContext.Current.CancellationToken);
        using var brandingOnPlatform = await platform.GetAsync(Url("branding"), TestContext.Current.CancellationToken);

        Assert.Equal(("Kronant Prop", "http://{firm}.localhost:3002/", 10), (about.GetProperty("name").GetString(), about.GetProperty("firmPortalUrl").GetString(), about.GetProperty("minimumPasswordLength").GetInt32()));

        // The front page tells what firms pay, before they sign up. The tests take no deposit.
        var prices = about.GetProperty("prices");
        Assert.Equal(
            ("USD", 700m, 0m, 500m, 25),
            (prices.GetProperty("currency").GetString(), prices.GetProperty("startupFee").GetDecimal(), prices.GetProperty("reviewDeposit").GetDecimal(), prices.GetProperty("packagePrice").GetDecimal(), prices.GetProperty("packageSlots").GetInt32()));
        Assert.Equal(10, about.GetProperty("sandboxMaxOpenAccounts").GetInt32());
        Assert.Equal(
            (HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound),
            (onFirmPortal.StatusCode, platformOnFirmPortal.StatusCode, brandingOnPlatform.StatusCode));
    }

    [Fact]
    public async Task AConfirmationEmailThatFailsCanBeTriedAgain()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), WithEmailConfirmation);
        using var platform = factory.CreatePlatformClient();
        factory.Emails.FailNext(1);

        using var failed = await SignUpAsync(platform, "acme", "owner@firm.test");
        var availability = await platform.GetFromJsonAsync<JsonElement>(Url("signup/availability?firmId=acme"), TestContext.Current.CancellationToken);
        using var again = await SignUpAsync(platform, "acme", "owner@firm.test");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.True(availability.GetProperty("available").GetBoolean());
        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
    }

    [Fact]
    public async Task TheServerIsCreatedWhenTheTradingPlatformComesBack()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());

        // The check of the name at sign-up, and the first attempt to create the server.
        factory.Trading.FailNext(2);
        using var admin = await factory.SignUpAsync("acme");
        await Eventually.ThatAsync(() => factory.Trading.RemainingFailures == 0, "the first attempt");
        var waiting = await admin.GetFromJsonAsync<JsonElement>(Url("admin/firm"), TestContext.Current.CancellationToken);

        await factory.AdvanceUntilProvisionedAsync(admin);

        Assert.Equal("Provisioning", waiting.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, waiting.GetProperty("tradingServer").ValueKind);
    }

    // The server was made, but the answer with its key was lost. The next attempt asks for a new key.
    [Fact]
    public async Task ALostAnswerFromTheTradingPlatformEndsWithANewKey()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        factory.Trading.LoseNextCreateAnswer();
        using var admin = await factory.SignUpAsync("acme");
        await factory.AdvanceUntilProvisionedAsync(admin);
        using var started = await admin.PostAsJsonAsync(
            Url("admin/accounts"), new { email = "anna@test.example", challengeId = "two-step-100k" }, TestContext.Current.CancellationToken);
        await Eventually.ThatAsync(() => factory.Trading.HasAccount("acme-1001-1"), "the account to open with the new key");

        Assert.Equal("key-acme-2", factory.Trading.KeyOf("acme"));
        Assert.Contains("replace key acme", factory.Trading.Commands);
    }

    [Fact]
    public async Task TheWelcomeLinkWorksOnceAndOnlyOnItsFirmsPortal()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var platform = factory.CreatePlatformClient();
        using var signedUp = await SignUpAsync(platform, "acme", "owner@firm.test");
        var adminUrl = new Uri((await signedUp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("adminUrl").GetString()!);
        var token = System.Web.HttpUtility.ParseQueryString(adminUrl.Query)["token"];
        using var demo = factory.CreatePortalClient();
        using var acme = factory.CreatePortalClient(PropFactory.HostOf("acme"));

        using var onOtherFirm = await demo.PostAsJsonAsync(Url("admin/welcome"), new { token }, TestContext.Current.CancellationToken);
        using var first = await acme.PostAsJsonAsync(Url("admin/welcome"), new { token }, TestContext.Current.CancellationToken);
        using var again = await acme.PostAsJsonAsync(Url("admin/welcome"), new { token }, TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.OK, HttpStatusCode.Unauthorized), (onOtherFirm.StatusCode, first.StatusCode, again.StatusCode));
    }

    [Fact]
    public async Task TheAdministratorLogsInWithThePasswordChosenAtSignup()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        (await factory.SignUpAsync("acme")).Dispose();
        using var browser = factory.CreatePortalClient(PropFactory.HostOf("acme"));

        using var login = await browser.PostAsJsonAsync(
            Url("admin/login"), new { email = "owner@firm.test", password = PropFactory.SignupPassword }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task TheSandboxHasRoomForALimitedNumberOfOpenAccounts()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), new Dictionary<string, string> { ["Sandbox:MaxOpenAccounts"] = "2" });
        using var admin = await factory.SignUpAsync("acme");
        await PropFactory.WaitUntilProvisionedAsync(admin);

        var first = await StartAsync(admin, "a@test.example");
        await StartAsync(admin, "b@test.example");
        using var full = await admin.PostAsJsonAsync(Url("admin/accounts"), new { email = "c@test.example", challengeId = "two-step-100k" }, TestContext.Current.CancellationToken);
        using var cancelled = await admin.PostAsJsonAsync(Url($"admin/accounts/{first}/cancel"), new { reason = "Testing" }, TestContext.Current.CancellationToken);
        using var room = await admin.PostAsJsonAsync(Url("admin/accounts"), new { email = "c@test.example", challengeId = "two-step-100k" }, TestContext.Current.CancellationToken);

        // A live firm has no such limit.
        for (var i = 0; i < 3; i++)
        {
            await factory.StartActiveAccountAsync($"live{i}@test.example");
        }

        Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.OK, HttpStatusCode.Created), (full.StatusCode, cancelled.StatusCode, room.StatusCode));
    }

    [Fact]
    public async Task SignedUpFirmsSurviveARestart()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using (var admin = await factory.SignUpAsync("acme"))
        {
            await PropFactory.WaitUntilProvisionedAsync(admin);
        }

        await using var restarted = factory.Restart();
        using var browser = restarted.CreatePortalClient(PropFactory.HostOf("acme"));
        using var login = await browser.PostAsJsonAsync(
            Url("admin/login"), new { email = "owner@firm.test", password = PropFactory.SignupPassword }, TestContext.Current.CancellationToken);
        var firm = await browser.GetFromJsonAsync<JsonElement>(Url("admin/firm"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(("Sandbox", "acme"), (firm.GetProperty("status").GetString(), firm.GetProperty("tradingServer").GetString()));
    }

    [Fact]
    public async Task AConfiguredFirmCannotTakeOverOneThatSignedUp()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        (await factory.SignUpAsync("acme")).Dispose();
        var settings = PropFactory.WithOtherFirm();
        settings["Firms:1:Id"] = "acme";
        settings["Firms:1:Portal:Hosts:0"] = "portal.acme.test";
        await using var configured = PropFactory.Create(factory.ConnectionString, settings);

        var exception = Assert.ThrowsAny<Exception>(() => configured.CreateClient());

        Assert.Contains("signed up and cannot be configured", exception.ToString(), StringComparison.Ordinal);
    }

    private static Uri Url(string path) => new($"/api/portal/{path}", UriKind.Relative);

    private static Task<HttpResponseMessage> SignUpAsync(HttpClient platform, string firmId, string email) =>
        platform.PostAsJsonAsync(
            Url("signup"),
            new { firmName = $"Firm {firmId}", firmId, email, password = PropFactory.SignupPassword, acceptTerms = true },
            TestContext.Current.CancellationToken);

    private static async Task<Guid> StartAsync(HttpClient admin, string email)
    {
        using var response = await admin.PostAsJsonAsync(Url("admin/accounts"), new { email, challengeId = "two-step-100k" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
    }
}
