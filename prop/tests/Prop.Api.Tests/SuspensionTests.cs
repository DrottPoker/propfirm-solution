using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Prop.Api.Tests.Support;

namespace Prop.Api.Tests;

/// <summary>
/// Our suspension of a firm (ADR 0021): no challenge can start, the shop closes and the firm's challenges are paused
/// on the trading platform until we lift it.
/// </summary>
public sealed class SuspensionTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Reason = "Traders report payouts that were never paid.";

    [Fact]
    public async Task ASuspendedFirmStartsNothingAndItsChallengesArePausedUntilWeLiftIt()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), PropFactory.WithWebhook());
        var account = await factory.StartActiveAccountAsync();
        var accountId = account.GetProperty("id").GetGuid();
        using var ops = await factory.LogInAsStaffAsync();
        using var admin = await factory.LogInAsAdminAsync();
        using var firmApi = factory.CreateFirmClient();
        using var buyer = factory.CreatePortalClient();

        using var withoutReason = await PostAsync(ops, "ops/firms/demo-firm/suspend", new { reason = "" });
        using var suspended = await PostAsync(ops, "ops/firms/demo-firm/suspend", new { reason = Reason });
        using var again = await PostAsync(ops, "ops/firms/demo-firm/suspend", new { reason = Reason });
        await factory.WaitForAccountAsync(accountId, a => a.GetProperty("paused").GetBoolean());
        await Eventually.ThatAsync(() => factory.Trading.AccountOf(account.GetProperty("tradingAccountId").GetString()!).Suspended, "the trading account to be suspended");
        using var refused = await firmApi.PostAsJsonAsync(new Uri("/api/firm/v1/accounts", UriKind.Relative), new { email = "bert@test.example", challengeId = "two-step-100k" }, TestContext.Current.CancellationToken);
        var shop = await GetAsync(buyer, "shop");
        var billing = await GetAsync(admin, "admin/billing");
        var slots = await firmApi.GetFromJsonAsync<JsonElement>(new Uri("/api/firm/v1/slots", UriKind.Relative), TestContext.Current.CancellationToken);
        var suspendedList = await GetAsync(ops, "ops/firms?filter=Suspended");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, withoutReason.StatusCode);
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.Conflict), (suspended.StatusCode, again.StatusCode));
        Assert.Equal(Reason, (await suspended.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("suspension").GetProperty("reason").GetString());
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.False(shop.GetProperty("open").GetBoolean());
        Assert.Equal(Reason, billing.GetProperty("suspension").GetProperty("reason").GetString());
        Assert.True(slots.GetProperty("suspended").GetBoolean());
        Assert.Equal(["demo-firm"], suspendedList.EnumerateArray().Select(f => f.GetProperty("id").GetString()));
        Assert.Contains(factory.Emails.Sent, e => e.To == PropFactory.AdminEmail && e.Subject == "Demo Firm is suspended" && e.Body.Contains(Reason, StringComparison.Ordinal));
        await Eventually.ThatAsync(() => factory.Webhooks.Delivered("account.paused").Count == 1, "the paused webhook");

        using var lifted = await PostAsync(ops, "ops/firms/demo-firm/unsuspend", null);
        await factory.WaitForAccountAsync(accountId, a => !a.GetProperty("paused").GetBoolean());
        await Eventually.ThatAsync(() => !factory.Trading.AccountOf(account.GetProperty("tradingAccountId").GetString()!).Suspended, "the trading account to be resumed");
        using var started = await firmApi.PostAsJsonAsync(new Uri("/api/firm/v1/accounts", UriKind.Relative), new { email = "bert@test.example", challengeId = "two-step-100k" }, TestContext.Current.CancellationToken);
        var firm = await GetAsync(ops, "ops/firms/demo-firm");

        Assert.Equal(HttpStatusCode.OK, lifted.StatusCode);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        Assert.Equal(JsonValueKind.Null, firm.GetProperty("suspension").ValueKind);
        Assert.Equal(["suspension_lifted", "suspended"], firm.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("type").GetString()));
        Assert.Contains(factory.Emails.Sent, e => e.To == PropFactory.AdminEmail && e.Subject == "Demo Firm is no longer suspended");
        await Eventually.ThatAsync(() => factory.Webhooks.Delivered("account.resumed").Count == 1, "the resumed webhook");
    }

    [Fact]
    public async Task ASuspendedFirmInTheSandboxCannotGoLiveOrSendItsApplication()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");
        await PropFactory.WaitUntilProvisionedAsync(admin);
        await factory.ApproveAsync(admin, "acme");
        using var ops = await factory.LogInAsStaffAsync();

        using var suspended = await PostAsync(ops, "ops/firms/acme/suspend", new { reason = Reason });
        using var activate = await PostAsync(admin, "admin/billing/activate", new { slots = 20 });
        using var start = await PostAsync(admin, "admin/accounts", new { email = "anna@test.example", challengeId = "two-step-100k" });

        Assert.Equal(HttpStatusCode.OK, suspended.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, activate.StatusCode);
        Assert.Equal("The firm is suspended, so it cannot go live.", (await activate.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.Conflict, start.StatusCode);
    }

    [Fact]
    public async Task AnUnknownFirmCannotBeSuspended()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var ops = await factory.LogInAsStaffAsync();

        using var suspended = await PostAsync(ops, "ops/firms/nobody/suspend", new { reason = Reason });
        using var firm = await ops.GetAsync(Url("ops/firms/nobody"), TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (suspended.StatusCode, firm.StatusCode));
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string path) =>
        await client.GetFromJsonAsync<JsonElement>(Url(path), TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object? body) =>
        body is null ? client.PostAsync(Url(path), null, TestContext.Current.CancellationToken) : client.PostAsJsonAsync(Url(path), body, TestContext.Current.CancellationToken);

    private static Uri Url(string path) => new($"/api/portal/{path}", UriKind.Relative);
}
