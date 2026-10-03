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
    [Fact]
    public async Task TheFirmChangesItsLook()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");
        using var trader = factory.CreatePortalClient(PropFactory.HostOf("acme"));

        using var saved = await admin.PutAsJsonAsync(
            Url("admin/firm/branding"),
            new { logoUrl = "https://cdn.acme.test/logo.png", colors = new Dictionary<string, string> { ["accent"] = "#112233" } },
            TestContext.Current.CancellationToken);
        var branding = await trader.GetFromJsonAsync<JsonElement>(Url("branding"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(("https://cdn.acme.test/logo.png", "#112233"), (branding.GetProperty("logoUrl").GetString(), branding.GetProperty("colors").GetProperty("accent").GetString()));
    }

    [Theory]
    [InlineData("http://cdn.acme.test/logo.png", "accent", "#112233")]
    [InlineData("", "accent", "red")]
    [InlineData("", "accent", "#112233; background: url(x)")]
    [InlineData("", "button", "#112233")]
    public async Task OnlyAnHttpsLogoAndThePortalsColorsAreAccepted(string logoUrl, string color, string value)
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");

        using var refused = await admin.PutAsJsonAsync(
            Url("admin/firm/branding"), new { logoUrl, colors = new Dictionary<string, string> { [color] = value } }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
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

    [Fact]
    public async Task AdministratorsInviteEachOther()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var owner = await factory.SignUpAsync("acme");

        using var invited = await owner.PostAsJsonAsync(Url("admin/admins/invites"), new { email = "Second@Firm.test" }, TestContext.Current.CancellationToken);
        var pending = await owner.GetFromJsonAsync<JsonElement>(Url("admin/admins"), TestContext.Current.CancellationToken);
        var email = Assert.Single(factory.Emails.Sent);
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

        var template = Assert.Single((await admin.GetFromJsonAsync<JsonElement>(Url("admin/challenge-templates"), TestContext.Current.CancellationToken)).EnumerateArray());
        var definition = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(template.GetProperty("definition").GetRawText())!;
        definition["id"] = JsonSerializer.SerializeToElement("two-step-50k");
        definition["name"] = JsonSerializer.SerializeToElement("Two-step 50k");
        definition["initialBalance"] = JsonSerializer.SerializeToElement(50_000m);
        using var saved = await admin.PutAsJsonAsync(Url("admin/challenges/two-step-50k"), definition, TestContext.Current.CancellationToken);
        definition["currency"] = JsonSerializer.SerializeToElement("EUR");
        using var otherCurrency = await admin.PutAsJsonAsync(Url("admin/challenges/two-step-50k"), definition, TestContext.Current.CancellationToken);
        var challenges = await admin.GetFromJsonAsync<JsonElement>(Url("admin/challenges"), TestContext.Current.CancellationToken);

        Assert.Equal("two-step", template.GetProperty("id").GetString());
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity), (saved.StatusCode, otherCurrency.StatusCode));
        Assert.Equal(["two-step-100k", "two-step-50k"], challenges.EnumerateArray().Select(c => c.GetProperty("id").GetString()));
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

    private static async Task<string> NewApiKeyAsync(HttpClient admin)
    {
        using var response = await admin.PostAsync(Url("admin/firm/api-key"), null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("apiKey").GetString()!;
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
