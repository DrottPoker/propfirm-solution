using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Prop.Api.Tests.Support;

namespace Prop.Api.Tests;

/// <summary>The admin panel's settings for a firm that runs itself: its look, integration, administrators and challenges.</summary>
public sealed class AdminSettingsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];

    [Fact]
    public async Task TheFirmChangesItsColors()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");
        using var trader = factory.CreatePortalClient(PropFactory.HostOf("acme"));

        using var saved = await admin.PutAsJsonAsync(
            Url("admin/firm/branding"),
            new { colors = new Dictionary<string, string> { ["accent"] = "#112233", ["accent-foreground"] = "#0b0e14" } },
            TestContext.Current.CancellationToken);
        var branding = await trader.GetFromJsonAsync<JsonElement>(Url("branding"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var colors = branding.GetProperty("colors");
        Assert.Equal(("#112233", "#0b0e14"), (colors.GetProperty("accent").GetString(), colors.GetProperty("accent-foreground").GetString()));
    }

    [Theory]
    [InlineData("accent", "red")]
    [InlineData("accent", "#112233; background: url(x)")]
    [InlineData("button", "#112233")]
    public async Task OnlyThePortalsColorsAreAccepted(string color, string value)
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");

        using var refused = await admin.PutAsJsonAsync(
            Url("admin/firm/branding"), new { colors = new Dictionary<string, string> { [color] = value } }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
    }

    [Fact]
    public async Task TheFirmUploadsItsLogoAndThePortalServesIt()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");
        using var trader = factory.CreatePortalClient(PropFactory.HostOf("acme"));

        var saved = await UploadLogoAsync(admin, Png, "logo.png", HttpStatusCode.OK);
        var logoUrl = (await trader.GetFromJsonAsync<JsonElement>(Url("branding"), TestContext.Current.CancellationToken)).GetProperty("logoUrl").GetString()!;
        using var logo = await trader.GetAsync(new Uri(logoUrl, UriKind.Relative), TestContext.Current.CancellationToken);
        using var oldAddress = await trader.GetAsync(Url($"logo/{new string('0', 64)}"), TestContext.Current.CancellationToken);
        using var otherFirm = await factory.CreatePortalClient().GetAsync(new Uri(logoUrl, UriKind.Relative), TestContext.Current.CancellationToken);
        using var removed = await admin.DeleteAsync(Url("admin/firm/logo"), TestContext.Current.CancellationToken);
        var afterwards = await trader.GetFromJsonAsync<JsonElement>(Url("branding"), TestContext.Current.CancellationToken);

        Assert.Equal($"/api/portal/logo/{Convert.ToHexStringLower(SHA256.HashData(Png))}", logoUrl);
        Assert.Equal(logoUrl, saved.GetProperty("logoUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
        Assert.Equal(Png, await logo.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=31536000, immutable", logo.Headers.CacheControl?.ToString());
        Assert.Contains("sandbox", logo.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (oldAddress.StatusCode, otherFirm.StatusCode));
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(JsonValueKind.Null, afterwards.GetProperty("logoUrl").ValueKind);
    }

    [Theory]
    [InlineData("Just some text.")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><a href=\"javascript:alert(1)\"><rect width=\"1\" height=\"1\"/></a></svg>")]
    [InlineData("<?xml version=\"1.0\"?><!DOCTYPE svg [<!ENTITY a \"a\">]><svg xmlns=\"http://www.w3.org/2000/svg\"></svg>")]
    public async Task OnlyAnImageWithoutScriptsIsALogo(string content)
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");

        var refused = await UploadLogoAsync(admin, Encoding.UTF8.GetBytes(content), "logo.svg", HttpStatusCode.UnprocessableEntity);

        Assert.Contains("logo", refused.GetProperty("title").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnSvgLogoIsAcceptedAndOneOverAMegabyteIsNot()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");

        await UploadLogoAsync(admin, [.. Png, .. new byte[1024 * 1024]], "big.png", HttpStatusCode.RequestEntityTooLarge);
        var svg = await UploadLogoAsync(
            admin,
            Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><rect width=\"10\" height=\"10\" fill=\"#123456\"/></svg>"),
            "logo.svg",
            HttpStatusCode.OK);
        using var served = await admin.GetAsync(new Uri(svg.GetProperty("logoUrl").GetString()!, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal("image/svg+xml", served.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task TheFirmMakesAKeyForItsOwnSystems()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");
        await PropFactory.WaitUntilProvisionedAsync(admin);

        var firstKey = await NewApiKeyAsync(admin);
        using var withFirstKey = factory.CreateFirmClient(firstKey);
        var challenges = await withFirstKey.GetFromJsonAsync<JsonElement>(new Uri("/api/firm/v1/challenges", UriKind.Relative), TestContext.Current.CancellationToken);
        var secondKey = await NewApiKeyAsync(admin);
        using var oldKey = await withFirstKey.GetAsync(new Uri("/api/firm/v1/challenges", UriKind.Relative), TestContext.Current.CancellationToken);
        var settings = await admin.GetFromJsonAsync<JsonElement>(Url("admin/firm"), TestContext.Current.CancellationToken);

        Assert.Equal(["two-step-100k"], challenges.EnumerateArray().Select(c => c.GetProperty("id").GetString()));
        Assert.NotEqual(firstKey, secondKey);
        Assert.Equal(HttpStatusCode.Unauthorized, oldKey.StatusCode);
        Assert.True(settings.GetProperty("hasApiKey").GetBoolean());
    }

    [Fact]
    public async Task WebhooksGoWhereTheFirmSaysSignedWithItsSecret()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");
        await PropFactory.WaitUntilProvisionedAsync(admin);

        var webhook = await SetWebhookAsync(admin, "https://hooks.acme.test/prop");
        var secret = webhook.GetProperty("secret").GetString()!;
        var again = await SetWebhookAsync(admin, "https://hooks.acme.test/prop-v2");
        using var started = await admin.PostAsJsonAsync(
            Url("admin/accounts"), new { email = "anna@test.example", challengeId = "two-step-100k" }, TestContext.Current.CancellationToken);
        await Eventually.ThatAsync(() => factory.Webhooks.Delivered("account.stage_started").Count == 1, "the webhook");
        var delivered = factory.Webhooks.Delivered("account.stage_started")[0];

        Assert.Equal(JsonValueKind.Null, again.GetProperty("secret").ValueKind);
        Assert.True(IsSignedWith(delivered, secret));
        Assert.Equal("https://hooks.acme.test/prop-v2", (await admin.GetFromJsonAsync<JsonElement>(Url("admin/firm"), TestContext.Current.CancellationToken)).GetProperty("webhookUrl").GetString());
    }

    // The firm sees every event, sends a test and sees how its webhooks went.
    [Fact]
    public async Task TheFirmSendsATestWebhookAndSeesTheDeliveries()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");
        await PropFactory.WaitUntilProvisionedAsync(admin);

        using var withoutAddress = await admin.PostAsync(Url("admin/firm/webhook/test"), null, TestContext.Current.CancellationToken);
        var secret = (await SetWebhookAsync(admin, "https://hooks.acme.test/prop")).GetProperty("secret").GetString()!;
        using var sent = await admin.PostAsync(Url("admin/firm/webhook/test"), null, TestContext.Current.CancellationToken);
        await Eventually.ThatAsync(() => factory.Webhooks.Delivered("webhook.test").Count == 1, "the test webhook");
        JsonElement overview = default;
        await Eventually.ThatAsync(
            async () =>
            {
                overview = await admin.GetFromJsonAsync<JsonElement>(Url("admin/firm/webhook"), TestContext.Current.CancellationToken);
                return overview.GetProperty("deliveries").EnumerateArray().Any(d => d.GetProperty("status").GetString() == "Delivered");
            },
            "the delivery to be recorded");

        Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.Accepted), (withoutAddress.StatusCode, sent.StatusCode));
        Assert.True(IsSignedWith(factory.Webhooks.Delivered("webhook.test")[0], secret));
        Assert.Contains("payout.requested", overview.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("type").GetString()));
        var delivery = Assert.Single(overview.GetProperty("deliveries").EnumerateArray());
        Assert.Equal(("webhook.test", 200), (delivery.GetProperty("eventType").GetString(), delivery.GetProperty("lastStatus").GetInt32()));
        Assert.Equal("https://hooks.acme.test/prop", overview.GetProperty("url").GetString());
    }

    // So the firm's developers find the API without asking us.
    [Fact]
    public async Task TheSettingsSayWhereTheFirmApiIs()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.LogInAsAdminAsync();

        var firm = await admin.GetFromJsonAsync<JsonElement>(Url("admin/firm"), TestContext.Current.CancellationToken);

        Assert.Equal(
            ("http://localhost:5201/api/firm/v1/", "http://localhost:5201/openapi/v1.json"),
            (firm.GetProperty("firmApiUrl").GetString(), firm.GetProperty("openApiUrl").GetString()));
    }

    [Fact]
    public async Task ANewWebhookSecretSignsFromNowOn()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");
        await PropFactory.WaitUntilProvisionedAsync(admin);
        var first = (await SetWebhookAsync(admin, "https://hooks.acme.test/prop")).GetProperty("secret").GetString()!;

        using var rotated = await admin.PostAsync(Url("admin/firm/webhook/secret"), null, TestContext.Current.CancellationToken);
        var second = (await rotated.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("secret").GetString()!;
        using var started = await admin.PostAsJsonAsync(
            Url("admin/accounts"), new { email = "anna@test.example", challengeId = "two-step-100k" }, TestContext.Current.CancellationToken);
        await Eventually.ThatAsync(() => factory.Webhooks.Delivered("account.stage_started").Count == 1, "the webhook");
        var delivered = factory.Webhooks.Delivered("account.stage_started")[0];

        Assert.NotEqual(first, second);
        Assert.False(IsSignedWith(delivered, first));
        Assert.True(IsSignedWith(delivered, second));
    }

    [Theory]
    [InlineData("http://hooks.acme.test/prop")]
    [InlineData("hooks.acme.test/prop")]
    public async Task WebhooksOnlyGoToHttpsAddresses(string url)
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");

        using var refused = await admin.PutAsJsonAsync(Url("admin/firm/webhook"), new { url }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
    }

    // Applied by the trading platform to every account of the firm, so only instruments without open trades can be left out.
    [Fact]
    public async Task TheFirmChoosesItsInstrumentsAndTheirConditions()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");
        await PropFactory.WaitUntilProvisionedAsync(admin);

        var before = await admin.GetFromJsonAsync<JsonElement>(Url("admin/trading-conditions"), TestContext.Current.CancellationToken);
        using var none = await SetConditionsAsync(admin);
        using var twice = await SetConditionsAsync(admin, ("EURUSD", 100, 2, 3.5m), ("EURUSD", 50, 2, 3.5m));
        using var tooMuchLeverage = await SetConditionsAsync(admin, ("EURUSD", 1_001, 2, 3.5m));
        using var halfCents = await SetConditionsAsync(admin, ("EURUSD", 100, 2, 3.555m));
        using var unknown = await SetConditionsAsync(admin, ("DOGEUSD", 10, 0, 0m));
        using var saved = await SetConditionsAsync(admin, ("EURUSD", 50, 5, 2.5m), ("XAGUSD", 20, 0, 0m));
        var after = await saved.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        factory.Trading.RefuseNextSymbols = "SymbolInUse";
        using var inUse = await SetConditionsAsync(admin, ("EURUSD", 50, 5, 2.5m));

        Assert.Equal(("USD", true), (before.GetProperty("currency").GetString(), before.GetProperty("changeable").GetBoolean()));
        Assert.Equal(
            [("EURUSD", true), ("GBPUSD", true), ("USDJPY", true), ("XAGUSD", false), ("XAUUSD", true)],
            before.GetProperty("symbols").EnumerateArray().Select(s => (s.GetProperty("symbol").GetString(), s.GetProperty("enabled").GetBoolean())));
        Assert.All(
            [none, twice, tooMuchLeverage, halfCents, unknown],
            r => Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var eurusd = after.GetProperty("symbols").EnumerateArray().Single(s => s.GetProperty("symbol").GetString() == "EURUSD");
        Assert.Equal((50, 5, 2.5m), (eurusd.GetProperty("leverage").GetInt32(), eurusd.GetProperty("spreadMarkupPoints").GetInt32(), eurusd.GetProperty("commissionPerLotPerSide").GetDecimal()));
        Assert.Equal(
            ["EURUSD", "XAGUSD"],
            after.GetProperty("symbols").EnumerateArray().Where(s => s.GetProperty("enabled").GetBoolean()).Select(s => s.GetProperty("symbol").GetString()));
        Assert.Equal(JsonValueKind.Null, after.GetProperty("symbols").EnumerateArray().Single(s => s.GetProperty("symbol").GetString() == "GBPUSD").GetProperty("leverage").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, inUse.StatusCode);
        Assert.Equal(["EURUSD", "XAGUSD"], factory.Trading.GroupSymbols.Values.Single().Select(s => s.Symbol));
    }

    // A firm we set up has the conditions in our configuration, which the trading platform does not let it change.
    [Fact]
    public async Task AConfiguredFirmSeesItsConditionsButCannotChangeThem()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.LogInAsAdminAsync();

        var conditions = await admin.GetFromJsonAsync<JsonElement>(Url("admin/trading-conditions"), TestContext.Current.CancellationToken);
        factory.Trading.RefuseNextSymbols = "GroupNotChangeable";
        using var refused = await SetConditionsAsync(admin, ("EURUSD", 50, 5, 2.5m));
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.False(conditions.GetProperty("changeable").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("Your trading conditions are set by us, so they cannot be changed here.", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task AdministratorsInviteEachOther()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var owner = await factory.SignUpAsync("acme");

        using var invited = await owner.PostAsJsonAsync(Url("admin/admins/invites"), new { email = "Second@Firm.test" }, TestContext.Current.CancellationToken);
        var pending = await owner.GetFromJsonAsync<JsonElement>(Url("admin/admins"), TestContext.Current.CancellationToken);
        var email = Assert.Single(factory.Emails.Sent, e => e.Subject.StartsWith("You are invited", StringComparison.Ordinal));
        using var second = factory.CreatePortalClient(PropFactory.HostOf("acme"));
        using var accepted = await second.PostAsJsonAsync(
            Url("admin/invites/accept"), new { token = FakeEmailSender.TokenIn(email), password = "a-second-password" }, TestContext.Current.CancellationToken);
        var me = await second.GetFromJsonAsync<JsonElement>(Url("admin/me"), TestContext.Current.CancellationToken);
        var admins = await owner.GetFromJsonAsync<JsonElement>(Url("admin/admins"), TestContext.Current.CancellationToken);
        using var again = await owner.PostAsJsonAsync(Url("admin/admins/invites"), new { email = "second@firm.test" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        Assert.Equal("Second@Firm.test", Assert.Single(pending.GetProperty("invites").EnumerateArray()).GetProperty("email").GetString());
        Assert.Contains("http://acme.localhost:3002/admin/invite?token=", email.Body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(("Second@Firm.test", "admin"), (me.GetProperty("email").GetString(), me.GetProperty("role").GetString()));
        Assert.Equal(
            [("owner@firm.test", true), ("Second@Firm.test", false)],
            admins.GetProperty("admins").EnumerateArray().Select(a => (a.GetProperty("email").GetString(), a.GetProperty("isYou").GetBoolean())));
        Assert.Empty(admins.GetProperty("invites").EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task AnInvitationIsSentAgainOrTakenBack()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var owner = await factory.SignUpAsync("acme");
        (await owner.PostAsJsonAsync(Url("admin/admins/invites"), new { email = "second@firm.test" }, TestContext.Current.CancellationToken)).Dispose();
        var first = factory.Emails.TokenFor("second@firm.test");

        using var resent = await owner.PostAsJsonAsync(Url("admin/admins/invites"), new { email = "second@firm.test" }, TestContext.Current.CancellationToken);
        var second = factory.Emails.TokenFor("second@firm.test");
        using var withdrawn = await owner.PostAsJsonAsync(Url("admin/admins/invites/withdraw"), new { email = "Second@Firm.test" }, TestContext.Current.CancellationToken);
        using var again = await owner.PostAsJsonAsync(Url("admin/admins/invites/withdraw"), new { email = "second@firm.test" }, TestContext.Current.CancellationToken);
        var admins = await owner.GetFromJsonAsync<JsonElement>(Url("admin/admins"), TestContext.Current.CancellationToken);
        using var acme = factory.CreatePortalClient(PropFactory.HostOf("acme"));
        using var accepted = await acme.PostAsJsonAsync(Url("admin/invites/accept"), new { token = second, password = "a-second-password" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, resent.StatusCode);
        Assert.NotEqual(first, second);
        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NotFound), (withdrawn.StatusCode, again.StatusCode));
        Assert.Empty(admins.GetProperty("invites").EnumerateArray());
        Assert.Equal(HttpStatusCode.Unauthorized, accepted.StatusCode);
    }

    [Fact]
    public async Task AnInvitationWorksOnceOnItsFirmsPortal()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var owner = await factory.SignUpAsync("acme");
        (await owner.PostAsJsonAsync(Url("admin/admins/invites"), new { email = "second@firm.test" }, TestContext.Current.CancellationToken)).Dispose();
        var token = factory.Emails.TokenFor("second@firm.test");
        using var demo = factory.CreatePortalClient();
        using var acme = factory.CreatePortalClient(PropFactory.HostOf("acme"));

        using var tooShort = await acme.PostAsJsonAsync(Url("admin/invites/accept"), new { token, password = "short" }, TestContext.Current.CancellationToken);
        using var otherFirm = await demo.PostAsJsonAsync(Url("admin/invites/accept"), new { token, password = "a-second-password" }, TestContext.Current.CancellationToken);
        using var first = await acme.PostAsJsonAsync(Url("admin/invites/accept"), new { token, password = "a-second-password" }, TestContext.Current.CancellationToken);
        using var again = await acme.PostAsJsonAsync(Url("admin/invites/accept"), new { token, password = "a-third-password" }, TestContext.Current.CancellationToken);

        Assert.Equal(
            (HttpStatusCode.UnprocessableEntity, HttpStatusCode.Unauthorized, HttpStatusCode.OK, HttpStatusCode.Unauthorized),
            (tooShort.StatusCode, otherFirm.StatusCode, first.StatusCode, again.StatusCode));
    }

    [Fact]
    public async Task AnInvitationThatCannotBeSentIsNotKept()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var owner = await factory.SignUpAsync("acme");
        factory.Emails.FailNext(1);

        using var failed = await owner.PostAsJsonAsync(Url("admin/admins/invites"), new { email = "second@firm.test" }, TestContext.Current.CancellationToken);
        var admins = await owner.GetFromJsonAsync<JsonElement>(Url("admin/admins"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Empty(admins.GetProperty("invites").EnumerateArray());
    }

    [Fact]
    public async Task ARemovedAdministratorIsLoggedOutAtOnce()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var owner = await factory.SignUpAsync("acme");
        (await owner.PostAsJsonAsync(Url("admin/admins/invites"), new { email = "second@firm.test" }, TestContext.Current.CancellationToken)).Dispose();
        using var second = factory.CreatePortalClient(PropFactory.HostOf("acme"));
        (await second.PostAsJsonAsync(
            Url("admin/invites/accept"), new { token = factory.Emails.TokenFor("second@firm.test"), password = "a-second-password" }, TestContext.Current.CancellationToken)).Dispose();
        var admins = await owner.GetFromJsonAsync<JsonElement>(Url("admin/admins"), TestContext.Current.CancellationToken);
        var ids = admins.GetProperty("admins").EnumerateArray().ToDictionary(a => a.GetProperty("email").GetString()!, a => a.GetProperty("id").GetGuid());

        using var removedSelf = await owner.DeleteAsync(Url($"admin/admins/{ids["owner@firm.test"]}"), TestContext.Current.CancellationToken);
        using var removed = await owner.DeleteAsync(Url($"admin/admins/{ids["second@firm.test"]}"), TestContext.Current.CancellationToken);
        using var afterwards = await second.GetAsync(Url("admin/accounts"), TestContext.Current.CancellationToken);
        using var login = await second.PostAsJsonAsync(
            Url("admin/login"), new { email = "second@firm.test", password = "a-second-password" }, TestContext.Current.CancellationToken);

        Assert.Equal(
            (HttpStatusCode.Conflict, HttpStatusCode.NoContent, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized),
            (removedSelf.StatusCode, removed.StatusCode, afterwards.StatusCode, login.StatusCode));
    }

    [Fact]
    public async Task AdministratorsOfOneFirmCannotTouchAnother()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var acme = await factory.SignUpAsync("acme");
        using var demo = await factory.LogInAsAdminAsync();
        var demoAdmin = (await demo.GetFromJsonAsync<JsonElement>(Url("admin/me"), TestContext.Current.CancellationToken)).GetProperty("userId").GetGuid();

        using var removed = await acme.DeleteAsync(Url($"admin/admins/{demoAdmin}"), TestContext.Current.CancellationToken);
        using var stillThere = await demo.GetAsync(Url("admin/me"), TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.OK), (removed.StatusCode, stillThere.StatusCode));
    }

    [Fact]
    public async Task TheFirmMakesChallengesFromTheTemplate()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");
        await PropFactory.WaitUntilProvisionedAsync(admin);

        var templates = (await admin.GetFromJsonAsync<JsonElement>(Url("admin/challenge-templates"), TestContext.Current.CancellationToken)).EnumerateArray().ToList();
        var template = templates.Single(t => t.GetProperty("id").GetString() == "two-step");
        var instant = templates.Single(t => t.GetProperty("id").GetString() == "instant-funded").GetProperty("definition");
        using var savedInstant = await admin.PutAsJsonAsync(Url("admin/challenges/instant-funded-100k"), instant, TestContext.Current.CancellationToken);
        var definition = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(template.GetProperty("definition").GetRawText())!;
        definition["id"] = JsonSerializer.SerializeToElement("two-step-50k");
        definition["name"] = JsonSerializer.SerializeToElement("Two-step 50k");
        definition["initialBalance"] = JsonSerializer.SerializeToElement(50_000m);
        using var saved = await admin.PutAsJsonAsync(Url("admin/challenges/two-step-50k"), definition, TestContext.Current.CancellationToken);
        definition["currency"] = JsonSerializer.SerializeToElement("EUR");
        using var otherCurrency = await admin.PutAsJsonAsync(Url("admin/challenges/two-step-50k"), definition, TestContext.Current.CancellationToken);
        var challenges = await admin.GetFromJsonAsync<JsonElement>(Url("admin/challenges"), TestContext.Current.CancellationToken);

        Assert.Equal(["one-step", "two-step", "three-step", "instant-funded"], templates.Select(t => t.GetProperty("id").GetString()));
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity), (savedInstant.StatusCode, saved.StatusCode, otherCurrency.StatusCode));
        Assert.Equal(0, instant.GetProperty("evaluation").GetArrayLength());
        Assert.Equal(
            ["instant-funded-100k", "two-step-100k", "two-step-50k"],
            challenges.EnumerateArray().Select(c => c.GetProperty("id").GetString()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheSettingsAreForAdministratorsOnly()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var accountId = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        using var trader = await factory.LogInAsTraderAsync(accountId);

        using var firm = await trader.GetAsync(Url("admin/firm"), TestContext.Current.CancellationToken);
        using var key = await trader.PostAsync(Url("admin/firm/api-key"), null, TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (firm.StatusCode, key.StatusCode));
    }

    private static Uri Url(string path) => new($"/api/portal/{path}", UriKind.Relative);

    private static Task<HttpResponseMessage> SetConditionsAsync(HttpClient admin, params (string Symbol, int Leverage, int SpreadMarkupPoints, decimal CommissionPerLotPerSide)[] symbols) =>
        admin.PutAsJsonAsync(
            Url("admin/trading-conditions"),
            new { symbols = symbols.Select(s => new { s.Symbol, s.Leverage, s.SpreadMarkupPoints, s.CommissionPerLotPerSide }) },
            TestContext.Current.CancellationToken);

    private static async Task<string> NewApiKeyAsync(HttpClient admin)
    {
        using var response = await admin.PostAsync(Url("admin/firm/api-key"), null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("apiKey").GetString()!;
    }

    /// <summary>Uploads the logo as the admin panel does, in the form field file, and checks the status. Returns the body.</summary>
    private static async Task<JsonElement> UploadLogoAsync(HttpClient admin, byte[] content, string fileName, HttpStatusCode expected)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(content);
        form.Add(file, "file", fileName);
        using var response = await admin.PutAsync(Url("admin/firm/logo"), form, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == expected, $"Expected {expected} but got {response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private static async Task<JsonElement> SetWebhookAsync(HttpClient admin, string url)
    {
        using var response = await admin.PutAsJsonAsync(Url("admin/firm/webhook"), new { url }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    // Prop-Signature is t={unix seconds},v1={hex of HMAC-SHA256 over "{t}.{body}"}.
    private static bool IsSignedWith(Received webhook, string secret)
    {
        var parts = webhook.Signature.Split(',').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{parts["t"]}.{webhook.Body}")));
        return Convert.ToHexStringLower(expected) == parts["v1"];
    }
}
