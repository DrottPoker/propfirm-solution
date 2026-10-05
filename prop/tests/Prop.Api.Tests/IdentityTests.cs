using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Prop.Api.Tests.Support;

namespace Prop.Api.Tests;

/// <summary>
/// ID checks of traders (ADR 0042): the test check for the sandbox and development, Didit through its API and webhooks,
/// the firm's own service through the firm API, what waits for the check, and the built-in checks on the firm's bill.
/// </summary>
public sealed class IdentityTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Owner = "owner@firm.test";

    [Fact]
    public async Task TheTestCheckApprovesOrDeclinesAndTheFirmSeesTheOutcome()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), PropFactory.WithWebhook());
        var annasAccount = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        using var anna = await factory.LogInAsTraderAsync(annasAccount);
        using var bert = await factory.LogInAsTraderAsync((await factory.StartActiveAccountAsync("bert@test.example")).GetProperty("id").GetGuid());
        using var admin = await factory.LogInAsAdminAsync();

        // Until the firm chooses how, nothing waits for a check and none can be started. Checking by hand is gone.
        var before = await MineAsync(anna);
        Assert.Equal((JsonValueKind.Null, "NotStarted", false), (before.GetProperty("mode").ValueKind, before.GetProperty("status").GetString(), before.GetProperty("canStart").GetBoolean()));
        using (var refused = await anna.PostAsync(Url("identity/start"), null, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        }

        using (var manual = await admin.PutAsJsonAsync(Url("admin/identity"), new { mode = "Manual", requiredBefore = "FirstPayout", checkAddress = false, checkSanctions = false }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.BadRequest, manual.StatusCode);
        }

        var settings = await SaveAsync(admin, new { mode = "BuiltIn", requiredBefore = "FirstPayout", checkAddress = true, checkSanctions = false });
        Assert.Equal(("BuiltIn", true), (settings.GetProperty("mode").GetString(), settings.GetProperty("testChecks").GetBoolean()));
        Assert.Equal((15m, 25, 0.8m), (settings.GetProperty("prices").GetProperty("monthlyPrice").GetDecimal(), settings.GetProperty("prices").GetProperty("included").GetInt32(), settings.GetProperty("prices").GetProperty("perCheck").GetDecimal()));

        // The test check opens the portal's test page, and the same check opens again a moment later.
        var url = await StartAsync(anna);
        Assert.Contains("/identity/test?session=", url.ToString(), StringComparison.Ordinal);
        Assert.Equal(url, await StartAsync(anna));
        Assert.Equal("Pending", (await MineAsync(anna)).GetProperty("status").GetString());
        var annasSession = SessionOf(url);
        using (var others = await bert.PostAsJsonAsync(Url($"identity/test/{annasSession}"), new { approve = true }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, others.StatusCode);
        }

        using (var approved = await anna.PostAsJsonAsync(Url($"identity/test/{annasSession}"), new { approve = true }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, approved.StatusCode);
        }

        var mine = await MineAsync(anna);
        Assert.Equal(("Approved", true, false), (mine.GetProperty("status").GetString(), mine.GetProperty("verified").GetBoolean(), mine.GetProperty("canStart").GetBoolean()));
        await factory.Emails.WaitForAsync("anna@test.example", "Your identity is verified with Demo Firm");
        var card = await admin.GetFromJsonAsync<JsonElement>(Url($"admin/accounts/{annasAccount}/trader"), TestContext.Current.CancellationToken);
        var identity = card.GetProperty("identity");
        Assert.Equal(("Approved", "Test", "Test Trader", true), (identity.GetProperty("status").GetString(), identity.GetProperty("provider").GetString(), identity.GetProperty("fullName").GetString(), identity.GetProperty("addressChecked").GetBoolean()));
        Assert.All(card.GetProperty("checks").EnumerateArray(), c => Assert.Equal("Test check", c.GetProperty("checkedBy").GetString()));
        await Eventually.ThatAsync(() => factory.Webhooks.Delivered("trader.identity_verified").Count > 0, "the trader.identity_verified webhook");
        Assert.Equal("anna@test.example", factory.Webhooks.Delivered("trader.identity_verified")[0].Json.GetProperty("data").GetProperty("trader").GetProperty("email").GetString());
        using (var again = await anna.PostAsync(Url("identity/start"), null, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        }

        // A declined check says why, and the trader starts a new one.
        var bertsUrl = await StartAsync(bert);
        using (var declined = await bert.PostAsJsonAsync(Url($"identity/test/{SessionOf(bertsUrl)}"), new { approve = false }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, declined.StatusCode);
        }

        var bertsCheck = await MineAsync(bert);
        Assert.Equal(("Declined", true, false), (bertsCheck.GetProperty("status").GetString(), bertsCheck.GetProperty("canStart").GetBoolean(), bertsCheck.GetProperty("verified").GetBoolean()));
        Assert.Equal("The test check was declined on the test page.", bertsCheck.GetProperty("reason").GetString());
        var email = await factory.Emails.WaitForAsync("bert@test.example", "Your ID check with Demo Firm did not pass");
        Assert.Contains("The test check was declined on the test page.", email.Body, StringComparison.Ordinal);
        Assert.NotEqual(bertsUrl, await StartAsync(bert));
    }

    [Fact]
    public async Task PayoutsAndFundingWaitForTheCheckWhenTheFirmSaysSo()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var funded = await TestAccounts.FundedAsync(factory, "anna@test.example", 1001, 8_000m);
        using var anna = await factory.LogInAsTraderAsync(funded);
        using var admin = await factory.LogInAsAdminAsync();
        using (var method = await anna.PutAsJsonAsync(Url("payout-method"), new { kind = "Bank", accountHolder = "Anna Andersson", accountNumber = "SE3550000000054910000003" }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, method.StatusCode);
        }

        await SaveAsync(admin, new { mode = "BuiltIn", requiredBefore = "FirstPayout", checkAddress = false, checkSanctions = false });
        using (var waits = await anna.PostAsync(Url($"accounts/{funded}/payouts"), null, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Conflict, waits.StatusCode);
            Assert.Equal("Verify your identity first, under Payouts.", (await waits.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("title").GetString());
        }

        // A check the firm ticks by hand lets the trader through.
        using (var ticked = await admin.PutAsJsonAsync(Url($"admin/accounts/{funded}/trader/checks/identity"), new { @checked = true }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, ticked.StatusCode);
        }

        Assert.True((await MineAsync(anna)).GetProperty("verified").GetBoolean());
        using (var requested = await anna.PostAsync(Url($"accounts/{funded}/payouts"), null, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Created, requested.StatusCode);
        }

        // The funded account waits for the check, in the admin panel and through the firm API.
        await SaveAsync(admin, new { mode = "BuiltIn", requiredBefore = "Funding", checkAddress = false, checkSanctions = false });
        var passed = await PassedAsync(factory, "bert@test.example", 1002);
        using var bert = await factory.LogInAsTraderAsync(passed);
        using var firm = factory.CreateFirmClient();
        using (var panel = await admin.PostAsync(Url($"admin/accounts/{passed}/approve-funding"), null, TestContext.Current.CancellationToken))
        using (var api = await firm.PostAsync(new Uri($"/api/firm/v1/accounts/{passed}/approve-funding", UriKind.Relative), null, TestContext.Current.CancellationToken))
        {
            Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.Conflict), (panel.StatusCode, api.StatusCode));
            Assert.StartsWith("The trader's identity is not verified yet.", (await panel.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("title").GetString(), StringComparison.Ordinal);
        }

        using (var verified = await bert.PostAsJsonAsync(Url($"identity/test/{SessionOf(await StartAsync(bert))}"), new { approve = true }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);
        }

        using var approved = await admin.PostAsync(Url($"admin/accounts/{passed}/approve-funding"), null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        // The payout queue shows the name from the ID, to compare with where the money goes.
        var queue = await admin.GetFromJsonAsync<JsonElement>(Url("admin/payouts"), TestContext.Current.CancellationToken);
        Assert.Equal(JsonValueKind.Null, Assert.Single(queue.EnumerateArray()).GetProperty("identityName").ValueKind);
    }

    [Fact]
    public async Task DiditStartsAndDecidesChecksThroughItsApi()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), FakeDidit.Settings());
        var annasAccount = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        using var anna = await factory.LogInAsTraderAsync(annasAccount);
        using var bert = await factory.LogInAsTraderAsync((await factory.StartActiveAccountAsync("bert@test.example")).GetProperty("id").GetGuid());
        using var admin = await factory.LogInAsAdminAsync();
        var settings = await SaveAsync(admin, new { mode = "BuiltIn", requiredBefore = "FirstPayout", checkAddress = false, checkSanctions = true });
        Assert.False(settings.GetProperty("testChecks").GetBoolean());
        var annasId = (await anna.GetFromJsonAsync<JsonElement>(Url("me"), TestContext.Current.CancellationToken)).GetProperty("userId").GetString();

        // The check is started with the workflow for the firm's checks, and opened again while it is not sent in.
        var url = await StartAsync(anna);
        Assert.Equal(new Uri("https://verify.didit.test/session/didit-session-1"), url);
        Assert.Equal(url, await StartAsync(anna));
        var created = Assert.Single(factory.Didit.Created);
        Assert.Equal((FakeDidit.WorkflowWithSanctions, annasId), (created["workflow_id"]!.GetValue<string>(), created["vendor_data"]!.GetValue<string>()));
        Assert.EndsWith("/identity?returned=1", created["callback"]!.GetValue<string>(), StringComparison.Ordinal);

        // Only a webhook signed with our secret is heard, and the decision is read from Didit's API.
        using var client = factory.CreateClient();
        using (var forged = await client.SendAsync(FakeDidit.Webhook("didit-session-1", "Approved", "not-the-secret"), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
        }

        factory.Didit.Decide("didit-session-1", new JsonObject { ["session_id"] = "didit-session-1", ["status"] = "In Review" });
        using (var review = await client.SendAsync(FakeDidit.Webhook("didit-session-1", "In Review"), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        }

        var inReview = await MineAsync(anna);
        Assert.Equal(("InReview", false), (inReview.GetProperty("status").GetString(), inReview.GetProperty("canStart").GetBoolean()));

        factory.Didit.Decide("didit-session-1", FakeDidit.Approved("didit-session-1", "Anna", "Andersson", "1990-04-01", "Sweden", screened: true));
        using (var decided = await client.SendAsync(FakeDidit.Webhook("didit-session-1", "Approved"), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
        }

        Assert.Equal("Approved", (await MineAsync(anna)).GetProperty("status").GetString());
        var card = await admin.GetFromJsonAsync<JsonElement>(Url($"admin/accounts/{annasAccount}/trader"), TestContext.Current.CancellationToken);
        var identity = card.GetProperty("identity");
        Assert.Equal(("Didit", "Anna Andersson", "1990-04-01", "Sweden"), (identity.GetProperty("provider").GetString(), identity.GetProperty("fullName").GetString(), identity.GetProperty("dateOfBirth").GetString(), identity.GetProperty("country").GetString()));
        Assert.Equal((true, false), (identity.GetProperty("sanctionsChecked").GetBoolean(), identity.GetProperty("addressChecked").GetBoolean()));
        var checks = card.GetProperty("checks").EnumerateArray().ToDictionary(c => c.GetProperty("item").GetString()!);
        Assert.Equal(("Didit", JsonValueKind.Null), (checks["identity"].GetProperty("checkedBy").GetString(), checks["address"].GetProperty("checkedBy").ValueKind));
        Assert.Equal(1L, await factory.ScalarAsync("select count(*) from identity_sessions where submitted_at is not null"));

        // A webhook about a check that is not ours is let be, and one Didit's API cannot tell about is asked for again.
        using (var unknown = await client.SendAsync(FakeDidit.Webhook("someone-elses", "Approved"), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        }

        await StartAsync(bert);
        factory.Didit.SetDown(true);
        using (var down = await client.SendAsync(FakeDidit.Webhook("didit-session-2", "Declined"), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, down.StatusCode);
        }

        // Without a webhook, the trader's page asks Didit itself, at most every ten seconds.
        factory.Didit.SetDown(false);
        factory.Didit.Decide("didit-session-2", FakeDidit.Declined("didit-session-2", "The document has expired"));
        await factory.AdvanceAsync(TimeSpan.FromSeconds(11));
        var bertsCheck = await MineAsync(bert);
        Assert.Equal(("Declined", "The document has expired."), (bertsCheck.GetProperty("status").GetString(), bertsCheck.GetProperty("reason").GetString()));
        await factory.Emails.WaitForAsync("bert@test.example", "did not pass");
    }

    [Fact]
    public async Task TheFirmsOwnServiceReportsThroughTheFirmApi()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var accountId = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        using var anna = await factory.LogInAsTraderAsync(accountId);
        using var admin = await factory.LogInAsAdminAsync();
        using var firm = factory.CreateFirmClient();
        var report = new { email = "ANNA@test.example", status = "Approved", fullName = "Anna Andersson", dateOfBirth = "1990-04-01", country = "Sweden", addressChecked = true };

        using (var notChosen = await firm.PutAsJsonAsync(FirmUrl("traders/identity"), report, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Conflict, notChosen.StatusCode);
        }

        Assert.Equal((422, "externalUrl"), await RefusedAsync(admin, new { mode = "External", requiredBefore = "FirstPayout", checkAddress = false, checkSanctions = false }));
        Assert.Equal(
            (422, "externalUrl"),
            await RefusedAsync(admin, new { mode = "External", requiredBefore = "FirstPayout", checkAddress = false, checkSanctions = false, externalUrl = "http://kyc.firm.test/start" }));
        using var bert = await factory.LogInAsTraderAsync((await factory.StartActiveAccountAsync("bert@test.example")).GetProperty("id").GetGuid());
        var chosen = await SaveAsync(admin, new { mode = "External", requiredBefore = "FirstPayout", checkAddress = false, checkSanctions = false, externalUrl = "https://kyc.firm.test/start?trader={traderId}&email={email}" });
        Assert.Equal(("NotTested", JsonValueKind.Null), (chosen.GetProperty("readiness").GetString(), chosen.GetProperty("externalTestedAt").ValueKind));

        // A report about a trader the portal never sent to the firm's page does not show that the whole flow works.
        using (var unsent = await firm.PutAsJsonAsync(FirmUrl("traders/identity"), report with { email = "bert@test.example", status = "Declined" }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, unsent.StatusCode);
        }

        Assert.Equal("NotTested", (await SettingsAsync(admin)).GetProperty("readiness").GetString());

        // The trader is sent to the firm's own page with the trader's id and email.
        var traderId = (await anna.GetFromJsonAsync<JsonElement>(Url("me"), TestContext.Current.CancellationToken)).GetProperty("userId").GetString();
        Assert.Equal(new Uri($"https://kyc.firm.test/start?trader={traderId}&email=anna%40test.example"), await StartAsync(anna));

        using (var unknown = await firm.PutAsJsonAsync(FirmUrl("traders/identity"), report with { email = "nobody@test.example" }, TestContext.Current.CancellationToken))
        using (var badStatus = await firm.PutAsJsonAsync(FirmUrl("traders/identity"), report with { status = "Expired" }, TestContext.Current.CancellationToken))
        {
            Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.UnprocessableEntity), (unknown.StatusCode, badStatus.StatusCode));
        }

        // Pending says nothing about the outcome yet. The decision completes the whole flow.
        using (var pending = await firm.PutAsJsonAsync(FirmUrl("traders/identity"), report with { status = "Pending" }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        }

        Assert.Equal("NotTested", (await SettingsAsync(admin)).GetProperty("readiness").GetString());
        using var reported = await firm.PutAsJsonAsync(FirmUrl("traders/identity"), report, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, reported.StatusCode);
        var tested = await SettingsAsync(admin);
        Assert.Equal(("Ready", factory.Time.GetUtcNow()), (tested.GetProperty("readiness").GetString(), tested.GetProperty("externalTestedAt").GetDateTimeOffset()));

        // Saving the same address keeps the test. A new address is tried again, and a check sent to the old one does not count.
        Assert.Equal("Ready", (await SaveAsync(admin, new { mode = "External", requiredBefore = "Funding", checkAddress = false, checkSanctions = false, externalUrl = "https://kyc.firm.test/start?trader={traderId}&email={email}" })).GetProperty("readiness").GetString());
        await StartAsync(bert);
        factory.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("NotTested", (await SaveAsync(admin, new { mode = "External", requiredBefore = "FirstPayout", checkAddress = false, checkSanctions = false, externalUrl = "https://kyc.firm.test/v2?trader={traderId}" })).GetProperty("readiness").GetString());
        using (var old = await firm.PutAsJsonAsync(FirmUrl("traders/identity"), report with { email = "bert@test.example", status = "Declined" }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, old.StatusCode);
        }

        Assert.Equal("NotTested", (await SettingsAsync(admin)).GetProperty("readiness").GetString());
        var found = await firm.GetFromJsonAsync<JsonElement>(FirmUrl("traders/identity?email=anna@test.example"), TestContext.Current.CancellationToken);
        Assert.Equal(("Approved", "External", "Anna Andersson", true), (found.GetProperty("status").GetString(), found.GetProperty("provider").GetString(), found.GetProperty("fullName").GetString(), found.GetProperty("addressChecked").GetBoolean()));
        Assert.True((await MineAsync(anna)).GetProperty("verified").GetBoolean());
        var card = await admin.GetFromJsonAsync<JsonElement>(Url($"admin/accounts/{accountId}/trader"), TestContext.Current.CancellationToken);
        Assert.All(card.GetProperty("checks").EnumerateArray(), c => Assert.Equal("Your KYC service", c.GetProperty("checkedBy").GetString()));
    }

    [Fact]
    public async Task EachMonthTheChecksAreUsedOrOnCostsTheMonthsPrice()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), FakeDidit.Settings());
        using var admin = await LiveFirmAsync(factory, "kyc-firm");
        using var quiet = await LiveFirmAsync(factory, "quiet-firm");
        await SaveAsync(admin, new { mode = "BuiltIn", requiredBefore = "FirstPayout", checkAddress = false, checkSanctions = true });
        await SaveAsync(quiet, new { mode = "BuiltIn", requiredBefore = "FirstPayout", checkAddress = false, checkSanctions = false });
        using var trader = await TraderOfAsync(factory, admin, "kyc-firm", "anna@test.example");

        // A live firm's checks go to Didit, and a check that was sent in is billed with its month.
        Assert.Equal(new Uri("https://verify.didit.test/session/didit-session-1"), await StartAsync(trader));
        factory.Didit.Decide("didit-session-1", FakeDidit.Approved("didit-session-1", "Anna", "Andersson", "1990-04-01", "Sweden", screened: true));
        using var client = factory.CreateClient();
        using (var decided = await client.SendAsync(FakeDidit.Webhook("didit-session-1", "Approved"), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
        }

        Assert.Equal(1, (await admin.GetFromJsonAsync<JsonElement>(Url("admin/identity"), TestContext.Current.CancellationToken)).GetProperty("checksSinceLastCharge").GetInt32());

        // Turning the checks off, for the firm's own service, before the month is charged does not make the check used in it free.
        await SaveAsync(admin, new { mode = "External", requiredBefore = "FirstPayout", checkAddress = false, checkSanctions = false, externalUrl = "https://kyc.firm.test/start" });

        // October is charged with November, on 27 October. The quiet firm turned the checks on in October without a check,
        // so its October costs nothing.
        await factory.AdvanceUntilAsync(
            new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero) - factory.Time.GetUtcNow(),
            async () => (long)(await factory.ScalarAsync("select count(*) from billing_charges where kind = 'IdentityChecks'"))! == 1,
            "the charge for October's ID checks");
        Assert.Equal(
            [("KYC, October 2026, 25 checks included", 1, 15m), ("1 sanctions screening", 1, 0.3m)],
            await LinesAsync(factory, "kyc-firm", "2026-10-01"));
        Assert.Equal(0L, await factory.ScalarAsync("select count(*) from identity_sessions where billed_charge_id is null"));
        await factory.AdvanceAsync(TimeSpan.FromMinutes(5));
        Assert.Equal(1L, await factory.ScalarAsync("select count(*) from billing_charges where kind = 'IdentityChecks'"));

        // The quiet firm had the checks on all of November, so November costs the month's price. The other firm uses its
        // own service and used none of ours, so its November costs nothing.
        await factory.AdvanceUntilAsync(
            new DateTimeOffset(2026, 11, 26, 0, 0, 0, TimeSpan.Zero) - factory.Time.GetUtcNow(),
            async () => (long)(await factory.ScalarAsync("select count(*) from billing_charges where kind = 'IdentityChecks'"))! == 2,
            "the charge for November's ID checks");
        Assert.Equal([("KYC, November 2026, 25 checks included", 1, 15m)], await LinesAsync(factory, "quiet-firm", "2026-11-01"));
        Assert.Equal(1L, await factory.ScalarAsync("select count(*) from billing_charges where kind = 'IdentityChecks' and firm_id = 'kyc-firm'"));
    }

    // The lines of the firm's charge for the month's ID checks: description, quantity and amount.
    private static async Task<List<(string?, int, decimal)>> LinesAsync(PropFactory factory, string firmId, string month)
    {
        var lines = (string)(await factory.ScalarAsync($"select lines::text from billing_charges where kind = 'IdentityChecks' and firm_id = '{firmId}' and month = '{month}'"))!;
        return [.. JsonDocument.Parse(lines).RootElement.EnumerateArray().Select(l => (l.GetProperty("description").GetString(), l.GetProperty("quantity").GetInt32(), l.GetProperty("amount").GetDecimal()))];
    }

    private static async Task<JsonElement> SaveAsync(HttpClient admin, object settings)
    {
        using var response = await admin.PutAsJsonAsync(Url("admin/identity"), settings, TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected OK but got {response.StatusCode}: {content}");
        return JsonDocument.Parse(content).RootElement.Clone();
    }

    private static Task<JsonElement> SettingsAsync(HttpClient admin) =>
        admin.GetFromJsonAsync<JsonElement>(Url("admin/identity"), TestContext.Current.CancellationToken);

    private static async Task<(int Status, string? Field)> RefusedAsync(HttpClient admin, object settings)
    {
        using var response = await admin.PutAsJsonAsync(Url("admin/identity"), settings, TestContext.Current.CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return ((int)response.StatusCode, problem.TryGetProperty("field", out var field) ? field.GetString() : null);
    }

    private static Task<JsonElement> MineAsync(HttpClient trader) =>
        trader.GetFromJsonAsync<JsonElement>(Url("identity"), TestContext.Current.CancellationToken);

    private static async Task<Uri> StartAsync(HttpClient trader)
    {
        using var response = await trader.PostAsync(Url("identity/start"), null, TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected OK but got {response.StatusCode}: {content}");
        return new Uri(JsonDocument.Parse(content).RootElement.GetProperty("url").GetString()!);
    }

    private static Guid SessionOf(Uri testPage) => Guid.Parse(System.Web.HttpUtility.ParseQueryString(testPage.Query)["session"]!);

    /// <summary>Starts the quick challenge for the email and passes both evaluation stages, so the funded account waits for the firm.</summary>
    private static async Task<Guid> PassedAsync(PropFactory factory, string email, int number)
    {
        var id = (await factory.StartActiveAccountAsync(email, TestAccounts.QuickTest)).GetProperty("id").GetGuid();
        for (var stage = 0; stage < 2; stage++)
        {
            var current = stage;
            await factory.WaitForAccountAsync(id, a => a.GetProperty("stage").GetInt32() == current && a.GetProperty("status").GetString() == "Active");
            factory.Trading.OpenPosition($"demo-firm-{number}-{stage + 1}");
            factory.Trading.ClosePosition($"demo-firm-{number}-{stage + 1}", 100m);
            await factory.WaitForAccountAsync(id, a => a.GetProperty("stage").GetInt32() > current || a.GetProperty("status").GetString() == "AwaitingFunding");
        }

        await factory.WaitForAccountAsync(id, a => a.GetProperty("status").GetString() == "AwaitingFunding");
        return id;
    }

    /// <summary>A firm that signed up, was approved and went live with test payments. Returns its administrator's browser.</summary>
    private static async Task<HttpClient> LiveFirmAsync(PropFactory factory, string firmId)
    {
        var admin = await factory.SignUpAsync(firmId, Owner);
        await PropFactory.WaitUntilProvisionedAsync(admin);
        await factory.ApproveAsync(admin, firmId);
        using var activate = await admin.PostAsJsonAsync(Url("admin/billing/activate"), new { slots = 25 }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        var checkout = new Uri((await activate.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("checkoutUrl").GetString()!).Segments[^1];
        using var paid = await admin.PostAsJsonAsync(Url($"admin/billing/checkouts/{checkout}/complete"), new { declines = false }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, paid.StatusCode);
        return admin;
    }

    /// <summary>The firm starts a challenge for the email and invites its trader, who chooses a password. Returns the trader's browser.</summary>
    private static async Task<HttpClient> TraderOfAsync(PropFactory factory, HttpClient admin, string firmId, string email)
    {
        using var started = await admin.PostAsJsonAsync(Url("admin/accounts"), new { email, challengeId = "two-step-100k" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var accountId = (await started.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
        using var invite = await admin.PostAsync(Url($"admin/accounts/{accountId}/invite"), null, TestContext.Current.CancellationToken);
        var link = new Uri((await invite.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("url").GetString()!);
        var trader = factory.CreatePortalClient(PropFactory.HostOf(firmId));
        using var accepted = await trader.PostAsJsonAsync(
            Url("invites/accept"),
            new { token = System.Web.HttpUtility.ParseQueryString(link.Query)["token"], password = PropFactory.TraderPassword },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        return trader;
    }

    private static Uri Url(string path) => new($"/api/portal/{path}", UriKind.Relative);

    private static Uri FirmUrl(string path) => new($"/api/firm/v1/{path}", UriKind.Relative);
}
