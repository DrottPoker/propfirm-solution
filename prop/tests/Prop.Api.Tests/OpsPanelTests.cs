using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Prop.Api.Tests.Support;

using static Prop.Api.Tests.Support.TestAccounts;

namespace Prop.Api.Tests;

/// <summary>
/// Our own admin view across the firms (ADR 0024): the overview with what waits for us, the list of firms with search
/// and groups, our checks during a review, a firm's figures and payouts, and what firms pay us.
/// </summary>
public sealed class OpsPanelTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task TheOverviewSaysWhatWaitsForUsAndHowTheFirmsGot()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var globex = await SubmittedFirmAsync(factory, "globex");
        var funded = await FundedAsync(factory, "anna@test.example", 1001, 8_000m);
        await RequestPayoutAsync(factory, funded);

        // The trader has waited more than a week when we look.
        await factory.AdvanceAsync(TimeSpan.FromDays(8));
        using var ops = await factory.LogInAsStaffAsync();
        var overview = await GetAsync(ops, "ops/overview");
        var waiting = await GetAsync(ops, "ops/waiting");

        var needsUs = overview.GetProperty("needsUs");
        Assert.Equal(["globex"], needsUs.GetProperty("toReview").EnumerateArray().Select(f => f.GetProperty("id").GetString()));
        Assert.Empty(needsUs.GetProperty("unpaid").EnumerateArray());
        Assert.Empty(needsUs.GetProperty("settingUp").EnumerateArray());
        var late = Assert.Single(needsUs.GetProperty("latePayouts").EnumerateArray());
        Assert.Equal(("demo-firm", "Demo Firm", 1, 0), (late.GetProperty("firmId").GetString(), late.GetProperty("firmName").GetString(), late.GetProperty("count").GetInt32(), late.GetProperty("approved").GetInt32()));
        Assert.Equal([("USD", 6_400m)], Totals(late.GetProperty("totals")));
        Assert.Equal(7, needsUs.GetProperty("lateAfterDays").GetInt32());
        Assert.Equal((1, 0), (waiting.GetProperty("toReview").GetInt32(), waiting.GetProperty("unpaid").GetInt32()));

        var firms = overview.GetProperty("firms");
        Assert.Equal((2, 1, 1, 1, 0), (firms.GetProperty("all").GetInt32(), firms.GetProperty("toReview").GetInt32(), firms.GetProperty("sandbox").GetInt32(), firms.GetProperty("live").GetInt32(), firms.GetProperty("suspended").GetInt32()));
        Assert.Equal((0m, 0), (overview.GetProperty("monthly").GetProperty("amount").GetDecimal(), overview.GetProperty("monthly").GetProperty("firms").GetInt32()));
        Assert.Equal((1, 0), (overview.GetProperty("challenges").GetProperty("open").GetInt32(), overview.GetProperty("challenges").GetProperty("paused").GetInt32()));
        Assert.Equal(12, overview.GetProperty("weeks").GetArrayLength());

        // The demo firm is configured, so only globex signed up.
        var funnel = overview.GetProperty("funnel");
        Assert.Equal((1, 1, 0, 0, 0), (funnel.GetProperty("signedUp").GetInt32(), funnel.GetProperty("sent").GetInt32(), funnel.GetProperty("approved").GetInt32(), funnel.GetProperty("live").GetInt32(), funnel.GetProperty("approvedNotLive").GetInt32()));
        var activity = overview.GetProperty("activity").EnumerateArray().ToList();
        Assert.Equal(["ApplicationSent", "SignedUp"], activity.Select(a => a.GetProperty("kind").GetString()).Order(StringComparer.Ordinal));
        var sent = activity.Single(a => a.GetProperty("kind").GetString() == "ApplicationSent");
        Assert.Equal(("globex", "owner@globex.test"), (sent.GetProperty("firmId").GetString(), sent.GetProperty("actor").GetString()));
    }

    [Fact]
    public async Task FirmsAreFoundByNameShortNameOrAdministratorAndListedInTheirGroup()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var acme = await factory.SignUpAsync("acme", "owner@acme.test");
        await PropFactory.WaitUntilProvisionedAsync(acme);
        using var globex = await SubmittedFirmAsync(factory, "globex");
        using var ops = await factory.LogInAsStaffAsync();

        var all = await GetAsync(ops, "ops/firms");
        var byName = await GetAsync(ops, "ops/firms?search=firm%20ACME");
        var byShortName = await GetAsync(ops, "ops/firms?search=glob");
        var byAdministrator = await GetAsync(ops, "ops/firms?search=owner@acme");
        var toReview = await GetAsync(ops, "ops/firms?group=ToReview");
        var live = await GetAsync(ops, "ops/firms?group=Live");
        using var tooFew = await ops.GetAsync(Url("ops/firms?limit=0"), TestContext.Current.CancellationToken);

        Assert.Equal(["acme", "demo-firm", "globex"], Ids(all).Order(StringComparer.Ordinal));
        Assert.Equal(["acme"], Ids(byName));
        Assert.Equal(["globex"], Ids(byShortName));
        Assert.Equal(["acme"], Ids(byAdministrator));
        Assert.Equal(["globex"], Ids(toReview));
        Assert.Equal(["demo-firm"], Ids(live));
        Assert.Equal((3, 1, 2, 1), Counts(all.GetProperty("counts")));
        Assert.Equal((1, 1, 1, 0), Counts(byShortName.GetProperty("counts")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooFew.StatusCode);

        var stages = all.GetProperty("firms").EnumerateArray().ToDictionary(f => f.GetProperty("id").GetString()!, f => f.GetProperty("stage").GetString());
        Assert.Equal(("Sandbox", "ToReview", "Live"), (stages["acme"], stages["globex"], stages["demo-firm"]));
        var demo = live.GetProperty("firms")[0];
        Assert.Equal((true, JsonValueKind.Null), (demo.GetProperty("configured").GetBoolean(), demo.GetProperty("monthlyPrice").ValueKind));
        Assert.Equal("USD", all.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task OurChecksAreTickedWhileAnApplicationWaitsAndKeptWithTheDecision()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var globex = await SubmittedFirmAsync(factory, "globex");
        using var acme = await factory.SignUpAsync("acme", "owner@acme.test");
        await PropFactory.WaitUntilProvisionedAsync(acme);
        using var ops = await factory.LogInAsStaffAsync();

        using var vat = await CheckAsync(ops, "globex", "vat", true);
        var ticked = await vat.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        using var owners = await CheckAsync(ops, "globex", "owners", true);
        using var ownersAgain = await CheckAsync(ops, "globex", "owners", false);
        using var unknownCheck = await CheckAsync(ops, "globex", "horoscope", true);
        using var notSent = await CheckAsync(ops, "acme", "vat", true);
        using var unknownFirm = await CheckAsync(ops, "nobody", "vat", true);
        var firm = await GetAsync(ops, "ops/firms/globex");
        using var approved = await ops.PostAsJsonAsync(Url("ops/firms/globex/approve"), new { message = (string?)null }, TestContext.Current.CancellationToken);
        var decided = await approved.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        using var afterDecision = await CheckAsync(ops, "globex", "terms", true);

        Assert.Equal(HttpStatusCode.OK, vat.StatusCode);
        Assert.Equal(["vat", "register", "owners", "terms", "website"], ticked.EnumerateArray().Select(c => c.GetProperty("item").GetString()));
        Assert.Equal((true, PropFactory.StaffEmail), (ticked[0].GetProperty("done").GetBoolean(), ticked[0].GetProperty("doneBy").GetString()));
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (owners.StatusCode, ownersAgain.StatusCode));
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.Conflict, HttpStatusCode.NotFound), (unknownCheck.StatusCode, notSent.StatusCode, unknownFirm.StatusCode));
        Assert.Equal(["vat"], firm.GetProperty("checks").EnumerateArray().Where(c => c.GetProperty("done").GetBoolean()).Select(c => c.GetProperty("item").GetString()));
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var decision = decided.GetProperty("events")[0];
        Assert.Equal("approved", decision.GetProperty("type").GetString());
        Assert.Equal(["vat"], decision.GetProperty("detail").GetProperty("checks").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal(HttpStatusCode.Conflict, afterDecision.StatusCode);
    }

    [Fact]
    public async Task AFirmShowsHowItPaysItsTradersWithoutWhoTheyAre()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var funded = await FundedAsync(factory, "anna@test.example", 1001, 8_000m);
        var payoutId = await RequestPayoutAsync(factory, funded);
        using var firmApi = factory.CreateFirmClient();
        await firmApi.PostJsonAsync($"payouts/{payoutId}/approve", null);
        using var ops = await factory.LogInAsStaffAsync();

        using var response = await ops.GetAsync(Url("ops/firms/demo-firm"), TestContext.Current.CancellationToken);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var firm = JsonDocument.Parse(raw).RootElement;

        var payouts = firm.GetProperty("figures").GetProperty("payouts");
        Assert.Equal((0, 1), (payouts.GetProperty("summary").GetProperty("toApprove").GetProperty("count").GetInt32(), payouts.GetProperty("summary").GetProperty("toPay").GetProperty("count").GetInt32()));
        var waiting = Assert.Single(payouts.GetProperty("waiting").EnumerateArray());
        Assert.Equal((1001L, "Approved", 6_400m), (waiting.GetProperty("accountNumber").GetInt64(), waiting.GetProperty("status").GetString(), waiting.GetProperty("amount").GetDecimal()));
        Assert.Equal(7, payouts.GetProperty("lateAfterDays").GetInt32());
        Assert.Equal(1, firm.GetProperty("figures").GetProperty("accounts").GetProperty("funded").GetInt32());
        Assert.Equal((1, 1), (firm.GetProperty("sandboxUse").GetProperty("challenges").GetInt32(), firm.GetProperty("sandboxUse").GetProperty("payouts").GetInt32()));
        Assert.Equal([PropFactory.AdminEmail], firm.GetProperty("admins").EnumerateArray().Select(a => a.GetProperty("email").GetString()));
        Assert.Equal("Complimentary", firm.GetProperty("billing").GetProperty("plan").GetString());

        // We see accounts and amounts, never the traders behind them.
        Assert.DoesNotContain("anna@test.example", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WhatFirmsPayUsComesFromTheirPlansAndCharges()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await LiveFirmAsync(factory, "acme", slots: 30);
        await NewCardAsync(admin, declines: true);

        // November is charged on 27 October and the card is declined. The administrator logs in again, since the session has expired by then.
        await factory.AdvanceAsync(new DateTimeOffset(2026, 10, 27, 0, 0, 1, TimeSpan.Zero) - factory.Time.GetUtcNow());
        using var login = await admin.PostAsJsonAsync(Url("admin/login"), new { email = "owner@acme.test", password = PropFactory.SignupPassword }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        JsonElement own = default;
        await factory.AdvanceUntilAsync(
            TimeSpan.Zero,
            async () =>
            {
                own = await GetAsync(admin, "admin/billing");
                return own.GetProperty("charges")[0].GetProperty("failure").ValueKind == JsonValueKind.String;
            },
            "November's charge to be declined");
        using var ops = await factory.LogInAsStaffAsync();

        var billing = await GetAsync(ops, "ops/billing");
        var overview = await GetAsync(ops, "ops/overview");
        var unpaidFirms = await GetAsync(ops, "ops/firms?group=Unpaid");
        var waiting = await GetAsync(ops, "ops/waiting");

        var monthly = billing.GetProperty("monthly");
        Assert.Equal((own.GetProperty("nextCharge").GetProperty("amount").GetDecimal(), 1, 30), (monthly.GetProperty("amount").GetDecimal(), monthly.GetProperty("firms").GetInt32(), monthly.GetProperty("slots").GetInt32()));
        // November waits for payment, so December is charged next, as in the firm's own billing.
        var next = billing.GetProperty("nextMonth");
        Assert.Equal(("2026-12-01", "2026-12-01"), (next.GetProperty("month").GetString(), own.GetProperty("nextCharge").GetProperty("month").GetString()));
        var acme = Assert.Single(next.GetProperty("firms").EnumerateArray());
        Assert.Equal(("acme", true), (acme.GetProperty("firmId").GetString(), acme.GetProperty("unpaid").GetBoolean()));

        var declined = Assert.Single(billing.GetProperty("unpaid").EnumerateArray());
        Assert.Equal(("Firm acme", "Renewal", "2026-11-01", "The test card was declined."), (
            declined.GetProperty("firmName").GetString(),
            declined.GetProperty("charge").GetProperty("kind").GetString(),
            declined.GetProperty("charge").GetProperty("month").GetString(),
            declined.GetProperty("charge").GetProperty("failure").GetString()));
        Assert.Equal(["Activation"], billing.GetProperty("paid").EnumerateArray().Select(c => c.GetProperty("charge").GetProperty("kind").GetString()));
        Assert.Equal((1, 0), (billing.GetProperty("paidLast30Days").GetProperty("goingLive").GetInt32(), billing.GetProperty("paidLast30Days").GetProperty("months").GetInt32()));
        Assert.Equal(700m, billing.GetProperty("prices").GetProperty("startupFee").GetDecimal());

        Assert.Equal(["acme"], overview.GetProperty("needsUs").GetProperty("unpaid").EnumerateArray().Select(c => c.GetProperty("firmId").GetString()));
        Assert.Equal(1, overview.GetProperty("firms").GetProperty("unpaid").GetInt32());
        Assert.Contains(overview.GetProperty("activity").EnumerateArray(), a => a.GetProperty("kind").GetString() == "ChargeDeclined");
        Assert.Contains(overview.GetProperty("activity").EnumerateArray(), a => a.GetProperty("kind").GetString() == "WentLive");
        Assert.Equal(["Unpaid"], unpaidFirms.GetProperty("firms").EnumerateArray().Select(f => f.GetProperty("stage").GetString()));
        Assert.Equal(1, waiting.GetProperty("unpaid").GetInt32());
    }

    [Fact]
    public async Task OnlyOurStaffSeeTheFirmsAndWhatTheyPay()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var anonymous = factory.CreatePortalClient(PropFactory.OpsHost);
        using var firmAdmin = await factory.LogInAsAdminAsync();

        foreach (var path in new[] { "ops/overview", "ops/waiting", "ops/firms", "ops/billing" })
        {
            using var notLoggedIn = await anonymous.GetAsync(Url(path), TestContext.Current.CancellationToken);
            using var onPortal = await firmAdmin.GetAsync(Url(path), TestContext.Current.CancellationToken);
            Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.NotFound), (notLoggedIn.StatusCode, onPortal.StatusCode));
        }
    }

    /// <summary>A firm that signed up, has its server and sent a complete application, without a deposit.</summary>
    private static async Task<HttpClient> SubmittedFirmAsync(PropFactory factory, string firmId)
    {
        var admin = await factory.SignUpAsync(firmId, $"owner@{firmId}.test");
        await PropFactory.WaitUntilProvisionedAsync(admin);
        using var saved = await admin.PutAsJsonAsync(Url("admin/verification/application"), PropFactory.Application(), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var submitted = await admin.PostAsync(Url("admin/verification/submit"), null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        return admin;
    }

    /// <summary>A firm that we approved and that went live with test payments.</summary>
    private static async Task<HttpClient> LiveFirmAsync(PropFactory factory, string firmId, int slots)
    {
        var admin = await factory.SignUpAsync(firmId, $"owner@{firmId}.test");
        await PropFactory.WaitUntilProvisionedAsync(admin);
        await factory.ApproveAsync(admin, firmId);
        using var activated = await admin.PostAsJsonAsync(Url("admin/billing/activate"), new { slots }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        using var paid = await CompleteAsync(admin, CheckoutIdOf(await activated.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)), declines: false);
        Assert.Equal(HttpStatusCode.NoContent, paid.StatusCode);
        return admin;
    }

    /// <summary>Saves a new test card, which pays or declines from now on.</summary>
    private static async Task NewCardAsync(HttpClient admin, bool declines)
    {
        using var response = await admin.PostAsync(Url("admin/billing/card"), null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var saved = await CompleteAsync(admin, CheckoutIdOf(await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)), declines);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
    }

    private static string CheckoutIdOf(JsonElement response) => new Uri(response.GetProperty("checkoutUrl").GetString()!).Segments[^1];

    private static Task<HttpResponseMessage> CompleteAsync(HttpClient admin, string checkoutId, bool declines) =>
        admin.PostAsJsonAsync(Url($"admin/billing/checkouts/{checkoutId}/complete"), new { declines }, TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> CheckAsync(HttpClient ops, string firmId, string item, bool done) =>
        ops.PutAsJsonAsync(Url($"ops/firms/{firmId}/checks/{item}"), new { done }, TestContext.Current.CancellationToken);

    private static async Task<JsonElement> GetAsync(HttpClient client, string path) =>
        await client.GetFromJsonAsync<JsonElement>(Url(path), TestContext.Current.CancellationToken);

    private static IEnumerable<string?> Ids(JsonElement page) => page.GetProperty("firms").EnumerateArray().Select(f => f.GetProperty("id").GetString());

    private static (int All, int ToReview, int Sandbox, int Live) Counts(JsonElement counts) =>
        (counts.GetProperty("all").GetInt32(), counts.GetProperty("toReview").GetInt32(), counts.GetProperty("sandbox").GetInt32(), counts.GetProperty("live").GetInt32());

    private static List<(string Currency, decimal Amount)> Totals(JsonElement totals) =>
        [.. totals.EnumerateArray().Select(t => (t.GetProperty("currency").GetString()!, t.GetProperty("amount").GetDecimal()))];

    private static Uri Url(string path) => new($"/api/portal/{path}", UriKind.Relative);
}
