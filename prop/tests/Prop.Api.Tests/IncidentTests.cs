using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Prop.Api.Tests.Support;
using Prop.Api.Trading;

namespace Prop.Api.Tests;

/// <summary>
/// Incidents (ADR 0053): the platform finds a price feed that stopped, our staff publish what happened, the firms'
/// terminals and status pages show it, and each firm reinstates or credits the accounts it reached.
/// </summary>
public sealed class IncidentTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Anna = "demo-firm-1001-1";
    private const string Bert = "demo-firm-1002-1";
    private const string Title = "Prices stopped for 20 minutes";
    private const string Text = "Our price feed gave no prices from 08:40 to 09:00 UTC. Orders and closes were refused meanwhile.";

    [Fact]
    public async Task AStoppedPriceFeedBecomesADraftForOurStaffThatEndsWhenPricesReturn()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var lastPrice = PropFactory.Start - TimeSpan.FromMinutes(2);
        factory.Trading.PriceFeed = Feed(PropFactory.Start, lastPrice);
        using var ops = await factory.LogInAsStaffAsync();

        await factory.AdvanceUntilAsync(TimeSpan.FromSeconds(15), async () => (await IncidentsAsync(ops)).Count == 1, "the outage to be found", TimeSpan.FromSeconds(15));
        await factory.AdvanceAsync(TimeSpan.FromSeconds(30));
        var draft = Assert.Single(await IncidentsAsync(ops));
        var waiting = await GetAsync(ops, "ops/waiting");

        Assert.Equal(("PriceFeedOutage", "Draft", true, lastPrice), (draft.GetProperty("kind").GetString(), draft.GetProperty("status").GetString(), draft.GetProperty("detected").GetBoolean(), draft.GetProperty("startedAt").GetDateTimeOffset()));
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("endedAt").ValueKind);
        Assert.Equal(1, waiting.GetProperty("incidentDrafts").GetInt32());
        var email = await factory.Emails.WaitForAsync(PropFactory.StaffEmail, "The price feed has stopped");
        Assert.Single(factory.Emails.Sent, e => e.Subject == "The price feed has stopped");
        Assert.Contains($"ops/incidents/{draft.GetProperty("id").GetGuid()}", email.Body, StringComparison.Ordinal);

        var back = factory.Time.GetUtcNow();
        factory.Trading.PriceFeed = Feed(back, back);
        await factory.AdvanceUntilAsync(
            TimeSpan.FromSeconds(15),
            async () => (await IncidentsAsync(ops))[0].GetProperty("endedAt").ValueKind != JsonValueKind.Null,
            "the outage to end",
            TimeSpan.FromSeconds(15));
        var ended = Assert.Single(await IncidentsAsync(ops));
        Assert.Equal(back, ended.GetProperty("endedAt").GetDateTimeOffset());

        using var dismissed = await PostAsync(ops, $"ops/incidents/{draft.GetProperty("id").GetGuid()}/dismiss", null);
        using var again = await PostAsync(ops, $"ops/incidents/{draft.GetProperty("id").GetGuid()}/dismiss", null);

        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NotFound), (dismissed.StatusCode, again.StatusCode));
        Assert.Empty(await IncidentsAsync(ops));
        Assert.Equal(0, (await GetAsync(ops, "ops/waiting")).GetProperty("incidentDrafts").GetInt32());
    }

    [Fact]
    public async Task ADismissedOutageIsNotFoundAgainWhileItGoesOn()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        factory.Trading.PriceFeed = Feed(PropFactory.Start, PropFactory.Start - TimeSpan.FromMinutes(5));
        using var ops = await factory.LogInAsStaffAsync();
        await factory.AdvanceUntilAsync(TimeSpan.FromSeconds(15), async () => (await IncidentsAsync(ops)).Count == 1, "the outage to be found", TimeSpan.FromSeconds(15));
        var id = (await IncidentsAsync(ops))[0].GetProperty("id").GetGuid();

        using var dismissed = await PostAsync(ops, $"ops/incidents/{id}/dismiss", null);
        await factory.AdvanceAsync(TimeSpan.FromSeconds(15));
        await factory.AdvanceAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(HttpStatusCode.NoContent, dismissed.StatusCode);
        Assert.Empty(await IncidentsAsync(ops));
        Assert.Single(factory.Emails.Sent, e => e.Subject == "The price feed has stopped");
    }

    [Fact]
    public async Task APublishedIncidentShowsInTheTerminalsOnTheStatusPageAndInTheAdminPanelUntilItIsResolved()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), PropFactory.WithOtherFirm());
        using var ops = await factory.LogInAsStaffAsync();
        using var admin = await factory.LogInAsAdminAsync();
        using var visitor = factory.CreatePortalClient();
        using var otherVisitor = factory.CreatePortalClient(PropFactory.OtherFirmHost);

        using var invalid = await PostAsync(ops, "ops/incidents", Request(title: ""));
        using var created = await PostAsync(ops, "ops/incidents", Request(firms: ["demo-firm"]));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
        var beforePublishing = await GetAsync(visitor, "status");
        var adminBefore = await GetAsync(admin, "admin/incidents");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.True(beforePublishing.GetProperty("allRunning").GetBoolean());
        Assert.Empty(beforePublishing.GetProperty("incidents").EnumerateArray());
        Assert.Empty(adminBefore.GetProperty("incidents").EnumerateArray());

        using var published = await PostAsync(ops, $"ops/incidents/{id}/publish", null);
        using var publishedAgain = await PostAsync(ops, $"ops/incidents/{id}/publish", null);
        await Eventually.ThatAsync(() => factory.Trading.NoticeOf("demo-firm") is not null, "the terminals' notice");
        var status = await GetAsync(visitor, "status");
        var otherStatus = await GetAsync(otherVisitor, "status");
        var list = await GetAsync(admin, "admin/incidents");

        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        Assert.Equal("Open", (await published.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, publishedAgain.StatusCode);
        var notice = factory.Trading.NoticeOf("demo-firm")!;
        Assert.Equal((Title, Text, true, new Uri("http://localhost:3002/status")), (notice.Title, notice.Text, notice.Warning, notice.Url));
        Assert.Null(factory.Trading.NoticeOf("other-firm"));
        Assert.False(status.GetProperty("allRunning").GetBoolean());
        Assert.Equal(
            [("Trading", false), ("Prices", false), ("Terminal", true), ("Portal", true)],
            status.GetProperty("parts").EnumerateArray().Select(p => (p.GetProperty("part").GetString(), p.GetProperty("running").GetBoolean())));
        Assert.True(otherStatus.GetProperty("allRunning").GetBoolean());
        Assert.Equal(1, list.GetProperty("open").GetInt32());
        var email = await factory.Emails.WaitForAsync(PropFactory.AdminEmail, $"Incident: {Title}");
        Assert.Single(factory.Emails.Sent, e => e.Subject == $"Incident: {Title}");
        Assert.Contains($"http://localhost:3002/admin/incidents/{id}", email.Body, StringComparison.Ordinal);

        using var noted = await PutAsync(admin, $"admin/incidents/{id}/note", new { text = "We reinstate every account the outage ended." });
        await Eventually.ThatAsync(() => factory.Trading.NoticeOf("demo-firm")?.Text.Contains("Demo Firm: We reinstate", StringComparison.Ordinal) == true, "the firm's note in the notice");

        using var resolved = await PostAsync(ops, $"ops/incidents/{id}/updates", new { status = "Resolved", text = "Prices are back." });
        await Eventually.ThatAsync(() => factory.Trading.NoticeOf("demo-firm") is null, "the notice to go");
        var after = await GetAsync(visitor, "status");
        var incident = Assert.Single(after.GetProperty("incidents").EnumerateArray());

        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.OK), (noted.StatusCode, resolved.StatusCode));
        Assert.True(after.GetProperty("allRunning").GetBoolean());
        Assert.Equal(("Resolved", "We reinstate every account the outage ended."), (incident.GetProperty("status").GetString(), incident.GetProperty("firmNote").GetString()));
        Assert.Equal([Text, "Prices are back."], incident.GetProperty("updates").EnumerateArray().Select(u => u.GetProperty("text").GetString()));
        Assert.Equal(JsonValueKind.Null, incident.GetProperty("updates")[0].GetProperty("by").ValueKind);
        Assert.Equal(0, (await GetAsync(admin, "admin/incidents")).GetProperty("open").GetInt32());
    }

    [Fact]
    public async Task TwoOpenIncidentsKeepTheNoticeUntilBothAreResolved()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var ops = await factory.LogInAsStaffAsync();
        var first = await PublishAsync(ops, Request(title: "Slow prices", kind: "SlowPrices", startedAt: PropFactory.Start - TimeSpan.FromHours(1)));
        var second = await PublishAsync(ops, Request());
        await Eventually.ThatAsync(() => factory.Trading.NoticeOf("demo-firm")?.Title == Title, "the latest incident's notice");

        using var resolved = await PostAsync(ops, $"ops/incidents/{second}/updates", new { status = "Resolved", text = "Prices are back." });
        await Eventually.ThatAsync(() => factory.Trading.NoticeOf("demo-firm")?.Title == "Slow prices", "the other incident's notice");
        using var alsoResolved = await PostAsync(ops, $"ops/incidents/{first}/updates", new { status = "Resolved", text = "Prices are on time again." });
        await Eventually.ThatAsync(() => factory.Trading.NoticeOf("demo-firm") is null, "the notice to go");

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (resolved.StatusCode, alsoResolved.StatusCode));
    }

    [Fact]
    public async Task AFirmReinstatesAStageTheOutageEndedAndCreditsAnAccountThatStillTrades()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), PropFactory.WithWebhook());
        var anna = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        var bert = (await factory.StartActiveAccountAsync("bert@test.example")).GetProperty("id").GetGuid();
        factory.Trading.Breach(Anna, "daily", 94_900m);
        await factory.WaitForAccountAsync(anna, a => a.GetProperty("status").GetString() == "Failed");
        var at = factory.Time.GetUtcNow();
        factory.Trading.SetImpact(new TradingImpact(
            at,
            at,
            [
                new TradingAccountImpact(Anna, 1, 100_000m, 99_000m, 0, 2, 0, new TradingImpactBreach(at, "daily", 95_000m, 94_900m), "Disabled", 94_900m, 94_900m),
                new TradingAccountImpact(Bert, 1, 100_000m, 100_000m, 1, 0, 0, null, "Active", 99_500m, 99_500m),
                new TradingAccountImpact("someone-elses-account", 1, 5_000m, 5_000m, 0, 0, 0, null, "Active", 5_000m, 5_000m),
            ]));
        using var ops = await factory.LogInAsStaffAsync();
        var id = await PublishAsync(ops, Request(startedAt: at - TimeSpan.FromMinutes(20), endedAt: at));
        using var admin = await factory.LogInAsAdminAsync();

        var detail = await GetAsync(admin, $"admin/incidents/{id}");
        var accounts = detail.GetProperty("accounts").EnumerateArray().ToList();

        Assert.True(detail.GetProperty("impactKnown").GetBoolean());
        Assert.Equal(
            [(anna, Anna, true, false, 2), (bert, Bert, false, true, 0)],
            accounts.Select(a => (a.GetProperty("accountId").GetGuid(), a.GetProperty("tradingAccountId").GetString(), a.GetProperty("canReinstate").GetBoolean(), a.GetProperty("canCredit").GetBoolean(), a.GetProperty("closesRefused").GetInt32())));
        Assert.Equal(94_900m, accounts[0].GetProperty("breach").GetProperty("equity").GetDecimal());

        using var notReinstatable = await PostAsync(admin, $"admin/incidents/{id}/reinstate", new { accountId = bert, balance = 100_000m, keepTradingDays = true, reason = "The outage" });
        using var tooMuch = await PostAsync(admin, $"admin/incidents/{id}/reinstate", new { accountId = anna, balance = 100_000.01m, keepTradingDays = true, reason = "The outage" });
        using var noReason = await PostAsync(admin, $"admin/incidents/{id}/reinstate", new { accountId = anna, balance = 100_000m, keepTradingDays = true, reason = " " });
        using var reinstated = await PostAsync(admin, $"admin/incidents/{id}/reinstate", new { accountId = anna, balance = 100_000m, keepTradingDays = true, reason = "Closes were refused during the outage." });

        Assert.Equal(HttpStatusCode.Conflict, notReinstatable.StatusCode);
        Assert.Equal((HttpStatusCode.UnprocessableEntity, HttpStatusCode.UnprocessableEntity), (tooMuch.StatusCode, noReason.StatusCode));
        Assert.Equal(HttpStatusCode.NoContent, reinstated.StatusCode);
        await factory.WaitForAccountAsync(anna, a => a.GetProperty("status").GetString() == "Active");
        await Eventually.ThatAsync(() => factory.Trading.AccountOf(Anna) is { Disabled: false, Balance: 100_000m } account && account.Floors.ContainsKey("daily"), "the trading account to open again with its floors");
        Assert.Contains($"reopen {Anna} 100000", factory.Trading.Commands);
        await factory.Emails.WaitForAsync("anna@test.example", "Your Two-step 100K is open again");
        await Eventually.ThatAsync(() => factory.Webhooks.Delivered("account.reinstated").Count == 1, "the reinstated webhook");

        using var creditTooMuch = await PostAsync(admin, $"admin/incidents/{id}/credit", new { accountId = bert, amount = 100_000.01m, reason = "A refused close" });
        using var creditFailed = await PostAsync(admin, $"admin/incidents/{id}/credit", new { accountId = Guid.NewGuid(), amount = 10m, reason = "A refused close" });
        using var credited = await PostAsync(admin, $"admin/incidents/{id}/credit", new { accountId = bert, amount = 250.50m, reason = "A close was refused during the outage." });

        Assert.Equal((HttpStatusCode.UnprocessableEntity, HttpStatusCode.NotFound, HttpStatusCode.NoContent), (creditTooMuch.StatusCode, creditFailed.StatusCode, credited.StatusCode));
        await Eventually.ThatAsync(() => factory.Trading.AccountOf(Bert).Balance == 100_250.50m, "the credit on the trading account");

        var after = await GetAsync(admin, $"admin/incidents/{id}");
        var list = await GetAsync(admin, "admin/incidents");

        Assert.Equal(
            [("Reinstated", 100_000m, PropFactory.AdminEmail), ("Credited", 250.50m, PropFactory.AdminEmail)],
            after.GetProperty("decisions").EnumerateArray().Select(d => (d.GetProperty("kind").GetString(), d.GetProperty("amount").GetDecimal(), d.GetProperty("decidedBy").GetString())));
        Assert.False(after.GetProperty("accounts")[0].GetProperty("canReinstate").GetBoolean());
        Assert.Equal(2, list.GetProperty("incidents")[0].GetProperty("decisions").GetInt32());
    }

    [Fact]
    public async Task AnIncidentOfAnotherFirmOrADraftCannotBeSeenOrActedOn()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), PropFactory.WithOtherFirm());
        using var ops = await factory.LogInAsStaffAsync();
        using var admin = await factory.LogInAsAdminAsync();
        using var draft = await PostAsync(ops, "ops/incidents", Request());
        var draftId = (await draft.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
        var otherId = await PublishAsync(ops, Request(firms: ["other-firm"]));

        using var seeDraft = await admin.GetAsync(Url($"admin/incidents/{draftId}"), TestContext.Current.CancellationToken);
        using var seeOther = await admin.GetAsync(Url($"admin/incidents/{otherId}"), TestContext.Current.CancellationToken);
        using var noteOther = await PutAsync(admin, $"admin/incidents/{otherId}/note", new { text = "Ours" });
        using var traderOnOps = await factory.CreatePortalClient(PropFactory.OpsHost).GetAsync(Url("ops/incidents"), TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound), (seeDraft.StatusCode, seeOther.StatusCode, noteOther.StatusCode));
        Assert.Equal(HttpStatusCode.Unauthorized, traderOnOps.StatusCode);
        Assert.Empty((await GetAsync(admin, "admin/incidents")).GetProperty("incidents").EnumerateArray());
    }

    private static PriceFeedStatus Feed(DateTimeOffset now, DateTimeOffset lastPrice) =>
        new("Synthetic", now, lastPrice, [new SymbolFeedStatus("EURUSD", lastPrice, true), new SymbolFeedStatus("US500", null, false)]);

    private static object Request(
        string title = Title,
        string kind = "PriceFeedOutage",
        DateTimeOffset? startedAt = null,
        DateTimeOffset? endedAt = null,
        string[]? firms = null) =>
        new { kind, title, publicText = Text, internalNote = "Capital.com dropped the stream.", startedAt = startedAt ?? PropFactory.Start - TimeSpan.FromMinutes(20), endedAt, firms };

    private static async Task<Guid> PublishAsync(HttpClient ops, object request)
    {
        using var created = await PostAsync(ops, "ops/incidents", request);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
        using var published = await PostAsync(ops, $"ops/incidents/{id}/publish", null);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        return id;
    }

    private static async Task<List<JsonElement>> IncidentsAsync(HttpClient ops) =>
        [.. (await GetAsync(ops, "ops/incidents")).GetProperty("incidents").EnumerateArray()];

    private static async Task<JsonElement> GetAsync(HttpClient client, string path) =>
        await client.GetFromJsonAsync<JsonElement>(Url(path), TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object? body) =>
        body is null ? client.PostAsync(Url(path), null, TestContext.Current.CancellationToken) : client.PostAsJsonAsync(Url(path), body, TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, string path, object body) =>
        client.PutAsJsonAsync(Url(path), body, TestContext.Current.CancellationToken);

    private static Uri Url(string path) => new($"/api/portal/{path}", UriKind.Relative);
}
