using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Prop.Api.Tests.Support;

namespace Prop.Api.Tests;

/// <summary>
/// What firms pay us, with test payments: going live, more and fewer slots, each month in advance, a month that
/// starts unpaid and pauses the firm's challenges, automatic expansion and the slots warning.
/// </summary>
public sealed class BillingFlowTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Owner = "owner@firm.test";

    private static readonly Dictionary<string, string> FewSlots = new() { ["Billing:MinSlots"] = "1" };

    [Fact]
    public async Task AFirmInTheSandboxGoesLiveByPayingTheStartupFeeAndItsFirstMonth()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await SandboxFirmAsync(factory, "acme");
        var sandboxAccount = await StartAsync(admin, "tester@firm.test");
        var before = await GetAsync(admin, "admin/billing");
        var quote = await GetAsync(admin, "admin/billing/quote?slots=20");

        var checkoutId = await ActivateAsync(admin, 20);
        var page = await GetAsync(admin, $"admin/billing/checkouts/{checkoutId}");
        using var declined = await CompleteAsync(admin, checkoutId, declines: true);
        using var paid = await CompleteAsync(admin, checkoutId, declines: false);
        using var again = await CompleteAsync(admin, checkoutId, declines: false);
        var after = await GetAsync(admin, "admin/billing");
        var branding = await GetAsync(admin, "branding");
        var account = await GetAsync(admin, $"admin/accounts/{sandboxAccount}");

        Assert.Equal(("Sandbox", JsonValueKind.Null, "Sandbox", 10, JsonValueKind.Null), (
            before.GetProperty("status").GetString(),
            before.GetProperty("plan").ValueKind,
            before.GetProperty("slots").GetProperty("limit").GetString(),
            before.GetProperty("slots").GetProperty("slots").GetInt32(),
            before.GetProperty("goLiveProblem").ValueKind));
        Assert.Equal(("Activation", 587.10m, "2026-11-01"), (quote.GetProperty("kind").GetString(), quote.GetProperty("amount").GetDecimal(), quote.GetProperty("from").GetString()));
        Assert.Equal(("Payment", "Open", 587.10m), (page.GetProperty("purpose").GetString(), page.GetProperty("status").GetString(), page.GetProperty("amount").GetDecimal()));
        Assert.Equal(HttpStatusCode.PaymentRequired, declined.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, paid.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        Assert.Equal(("Live", "Paid", "Paid", 20, 0, 20), (
            after.GetProperty("status").GetString(),
            after.GetProperty("plan").GetString(),
            after.GetProperty("slots").GetProperty("limit").GetString(),
            after.GetProperty("slots").GetProperty("slots").GetInt32(),
            after.GetProperty("slots").GetProperty("used").GetInt32(),
            after.GetProperty("nextMonthSlots").GetInt32()));
        Assert.Equal(("Test", "4242"), (after.GetProperty("card").GetProperty("brand").GetString(), after.GetProperty("card").GetProperty("last4").GetString()));
        var charge = Assert.Single(after.GetProperty("charges").EnumerateArray());
        Assert.Equal(("Activation", "Paid", 587.10m, 2), (charge.GetProperty("kind").GetString(), charge.GetProperty("status").GetString(), charge.GetProperty("amount").GetDecimal(), charge.GetProperty("lines").GetArrayLength()));
        var next = after.GetProperty("nextCharge");
        Assert.Equal(("2026-11-01", 20, 100m), (next.GetProperty("month").GetString(), next.GetProperty("slots").GetInt32(), next.GetProperty("amount").GetDecimal()));
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero), next.GetProperty("chargeAt").GetDateTimeOffset());
        Assert.Equal("Live", branding.GetProperty("status").GetString());
        Assert.Equal("Cancelled", account.GetProperty("account").GetProperty("status").GetString());
    }

    [Fact]
    public async Task GoingLiveNeedsTheCheckOfTheFirmWhenItIsNotAllowedWithout()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), new Dictionary<string, string> { ["Billing:AllowGoLiveWithoutVerification"] = "false" });
        using var admin = await SandboxFirmAsync(factory, "acme");

        using var response = await PostAsync(admin, "admin/billing/activate", new { slots = 20 });
        var billing = await GetAsync(admin, "admin/billing");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.StartsWith("Going live needs a check of the company", billing.GetProperty("goLiveProblem").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGoLiveThatIsStartedAgainOrNotPaidInTimeIsVoid()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await SandboxFirmAsync(factory, "acme");
        var first = await ActivateAsync(admin, 20);
        var second = await ActivateAsync(admin, 30);

        await factory.AdvanceAsync(TimeSpan.FromHours(1));
        await Eventually.ThatAsync(async () => (await GetAsync(admin, $"admin/billing/checkouts/{second}")).GetProperty("status").GetString() == "Expired", "the checkout to expire");
        var third = await ActivateAsync(admin, 25);
        using var paid = await CompleteAsync(admin, third, declines: false);
        var billing = await GetAsync(admin, "admin/billing");

        Assert.Equal("Expired", (await GetAsync(admin, $"admin/billing/checkouts/{first}")).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NoContent, paid.StatusCode);
        Assert.Equal(25, billing.GetProperty("slots").GetProperty("slots").GetInt32());
        Assert.Equal(["Paid", "Void", "Void"], billing.GetProperty("charges").EnumerateArray().Select(c => c.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task MoreSlotsArePaidNowAndFewerApplyFromNextMonth()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), FewSlots);
        using var admin = await LiveFirmAsync(factory, "acme", slots: 3);
        await StartAsync(admin, "anna@test.example");
        await StartAsync(admin, "bert@test.example");

        using var more = await admin.PutAsJsonAsync(Url("admin/billing/slots"), new { slots = 13 }, TestContext.Current.CancellationToken);
        var afterMore = await more.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        using var fewer = await admin.PutAsJsonAsync(Url("admin/billing/slots"), new { slots = 5 }, TestContext.Current.CancellationToken);
        var afterFewer = await fewer.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        using var tooFew = await admin.PutAsJsonAsync(Url("admin/billing/slots"), new { slots = 1 }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, more.StatusCode);
        Assert.Equal((13, 13), (afterMore.GetProperty("slots").GetProperty("slots").GetInt32(), afterMore.GetProperty("nextMonthSlots").GetInt32()));
        var bought = afterMore.GetProperty("charges")[0];
        Assert.Equal(("Slots", "Paid", 43.55m), (bought.GetProperty("kind").GetString(), bought.GetProperty("status").GetString(), bought.GetProperty("amount").GetDecimal()));
        Assert.Equal((13, 5, 5), (afterFewer.GetProperty("slots").GetProperty("slots").GetInt32(), afterFewer.GetProperty("nextMonthSlots").GetInt32(), afterFewer.GetProperty("nextCharge").GetProperty("slots").GetInt32()));
        Assert.Equal(HttpStatusCode.Conflict, tooFew.StatusCode);
    }

    [Fact]
    public async Task EachMonthIsChargedToTheSavedCardBeforeItStarts()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await LiveFirmAsync(factory, "acme", slots: 20);

        // From Monday 5 October at 08:00 to 27 October, when November is due.
        await AdvanceToAsync(factory, admin, new DateTimeOffset(2026, 10, 27, 0, 0, 1, TimeSpan.Zero));
        var billing = await WaitForBillingAsync(admin, b => b.GetProperty("charges").GetArrayLength() == 2, "November's charge");

        var renewal = billing.GetProperty("charges")[0];
        Assert.Equal(("Renewal", "Paid", "2026-11-01", 100m), (renewal.GetProperty("kind").GetString(), renewal.GetProperty("status").GetString(), renewal.GetProperty("month").GetString(), renewal.GetProperty("amount").GetDecimal()));
        Assert.Equal("2026-12-01", billing.GetProperty("nextCharge").GetProperty("month").GetString());
    }

    [Fact]
    public async Task AMonthThatStartsUnpaidPausesTheChallengesUntilItIsPaid()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await LiveFirmAsync(factory, "acme", slots: 20);
        using var webhook = await admin.PutAsJsonAsync(Url("admin/firm/webhook"), new { url = "https://acme.test/webhooks" }, TestContext.Current.CancellationToken);
        var accountId = await StartAsync(admin, "anna@test.example");
        await WaitForAccountAsync(admin, accountId, a => a.GetProperty("account").GetProperty("status").GetString() == "Active");
        await NewCardAsync(admin, declines: true);

        // November is due and declined. When it starts unpaid, the challenges pause.
        await AdvanceToAsync(factory, admin, new DateTimeOffset(2026, 10, 27, 0, 0, 1, TimeSpan.Zero));
        await Eventually.ThatAsync(() => factory.Emails.Sent.Any(e => e.To == Owner && e.Subject == "Payment for Firm acme declined"), "the declined payment email");
        await AdvanceToAsync(factory, admin, new DateTimeOffset(2026, 11, 1, 0, 0, 1, TimeSpan.Zero));
        var unpaid = await WaitForBillingAsync(admin, b => b.GetProperty("unpaidSince").ValueKind == JsonValueKind.String, "the firm to be unpaid");
        var paused = await WaitForAccountAsync(admin, accountId, a => a.GetProperty("account").GetProperty("paused").GetBoolean());
        await Eventually.ThatAsync(() => factory.Trading.AccountOf("acme-1001-1").Suspended, "the trading account to be suspended");
        using var refused = await PostAsync(admin, "admin/accounts", new { email = "bert@test.example", challengeId = "two-step-100k" });
        using var retried = await PostAsync(admin, $"admin/billing/charges/{unpaid.GetProperty("charges")[0].GetProperty("id").GetGuid()}/retry", null);

        Assert.False(unpaid.GetProperty("slots").GetProperty("paid").GetBoolean());
        Assert.Contains(factory.Emails.Sent, e => e.To == Owner && e.Subject == "Firm acme is paused until this month is paid");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(HttpStatusCode.PaymentRequired, retried.StatusCode);
        Assert.Equal("Active", paused.GetProperty("account").GetProperty("status").GetString());

        // A card that pays is tried at once, and the challenges go on.
        await NewCardAsync(admin, declines: false);
        await WaitForBillingAsync(admin, b => b.GetProperty("unpaidSince").ValueKind == JsonValueKind.Null, "the firm to be paid");
        await WaitForAccountAsync(admin, accountId, a => !a.GetProperty("account").GetProperty("paused").GetBoolean());
        await Eventually.ThatAsync(() => !factory.Trading.AccountOf("acme-1001-1").Suspended, "the trading account to be resumed");
        await Eventually.ThatAsync(() => factory.Webhooks.Delivered("account.resumed").Count == 1, "the resumed webhook");
        Assert.Single(factory.Webhooks.Delivered("account.paused"));
    }

    [Fact]
    public async Task AnUnpaidMonthCanBePaidWithAnotherCardOnACheckoutPage()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await LiveFirmAsync(factory, "acme", slots: 20);
        await NewCardAsync(admin, declines: true);
        await AdvanceToAsync(factory, admin, new DateTimeOffset(2026, 10, 27, 0, 0, 1, TimeSpan.Zero));
        var declined = await WaitForBillingAsync(admin, b => b.GetProperty("charges")[0].GetProperty("failure").ValueKind == JsonValueKind.String, "November's charge to be declined");
        var charge = declined.GetProperty("charges")[0];

        using var response = await PostAsync(admin, $"admin/billing/charges/{charge.GetProperty("id").GetGuid()}/checkout", null);
        var checkoutId = CheckoutIdOf(await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken));
        using var paid = await CompleteAsync(admin, checkoutId, declines: false);
        var billing = await GetAsync(admin, "admin/billing");

        Assert.Equal(("The test card was declined.", true), (charge.GetProperty("failure").GetString(), charge.GetProperty("canPay").GetBoolean()));
        Assert.Equal(HttpStatusCode.NoContent, paid.StatusCode);
        Assert.Equal(("Paid", "4242"), (billing.GetProperty("charges")[0].GetProperty("status").GetString(), billing.GetProperty("card").GetProperty("last4").GetString()));
    }

    [Fact]
    public async Task FewerSlotsApplyAfterTheMonthThatWaitsForPayment()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await LiveFirmAsync(factory, "acme", slots: 20);
        await NewCardAsync(admin, declines: true);
        await AdvanceToAsync(factory, admin, new DateTimeOffset(2026, 10, 27, 0, 0, 1, TimeSpan.Zero));
        await WaitForBillingAsync(admin, b => b.GetProperty("charges")[0].GetProperty("failure").ValueKind == JsonValueKind.String, "November's charge to be declined");

        var quote = await GetAsync(admin, "admin/billing/quote?slots=15");
        var billing = await GetAsync(admin, "admin/billing");

        Assert.Equal(("FewerSlots", "2026-12-01"), (quote.GetProperty("kind").GetString(), quote.GetProperty("from").GetString()));
        Assert.Equal("2026-12-01", billing.GetProperty("nextCharge").GetProperty("month").GetString());
    }

    [Fact]
    public async Task AutomaticExpansionBuysMoreSlotsWhenTheLastIsTaken()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), FewSlots);
        using var admin = await LiveFirmAsync(factory, "acme", slots: 2, autoExpandStep: 3);
        await StartAsync(admin, "anna@test.example");
        await StartAsync(admin, "bert@test.example");

        var billing = await WaitForBillingAsync(admin, b => b.GetProperty("slots").GetProperty("slots").GetInt32() == 5, "more slots");

        Assert.Equal(("Slots", "Paid", 5), (billing.GetProperty("charges")[0].GetProperty("kind").GetString(), billing.GetProperty("charges")[0].GetProperty("status").GetString(), billing.GetProperty("nextMonthSlots").GetInt32()));
        Assert.Equal(3, billing.GetProperty("slots").GetProperty("free").GetInt32());
    }

    [Fact]
    public async Task DeclinedAutomaticExpansionWaitsADayBeforeItTriesAgain()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), FewSlots);
        using var admin = await LiveFirmAsync(factory, "acme", slots: 2, autoExpandStep: 3);
        await NewCardAsync(admin, declines: true);
        await StartAsync(admin, "anna@test.example");
        await StartAsync(admin, "bert@test.example");

        await Eventually.ThatAsync(() => factory.Emails.Sent.Any(e => e.Subject == "Payment for Firm acme declined"), "the declined expansion");
        await factory.AdvanceAsync(TimeSpan.FromMinutes(5));
        var waiting = await GetAsync(admin, "admin/billing");
        await NewCardAsync(admin, declines: false);
        await AdvanceToAsync(factory, admin, factory.Time.GetUtcNow() + TimeSpan.FromDays(1));
        var expanded = await WaitForBillingAsync(admin, b => b.GetProperty("slots").GetProperty("slots").GetInt32() == 5, "the slots bought a day later");

        Assert.Equal(["Void"], waiting.GetProperty("charges").EnumerateArray().Where(c => c.GetProperty("kind").GetString() == "Slots").Select(c => c.GetProperty("status").GetString()));
        Assert.Single(factory.Emails.Sent, e => e.Subject == "Payment for Firm acme declined");
        Assert.Equal(["Paid", "Void"], expanded.GetProperty("charges").EnumerateArray().Where(c => c.GetProperty("kind").GetString() == "Slots").Select(c => c.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task TheAdministratorsAreWarnedOnceWhenMostSlotsAreTaken()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), FewSlots);
        using var admin = await LiveFirmAsync(factory, "acme", slots: 5);
        for (var i = 0; i < 4; i++)
        {
            await StartAsync(admin, $"trader{i}@test.example");
        }

        await Eventually.ThatAsync(() => factory.Emails.Sent.Any(e => e.Subject == "Firm acme has used 4 of 5 slots"), "the slots warning");
        await StartAsync(admin, "trader4@test.example");
        await factory.AdvanceAsync(TimeSpan.FromMinutes(1));
        var billing = await GetAsync(admin, "admin/billing");

        Assert.True(billing.GetProperty("slots").GetProperty("warning").GetBoolean());
        Assert.Single(factory.Emails.Sent, e => e.Subject.StartsWith("Firm acme has used", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ATestCheckoutPageIsOnlyTheFirmsOwn()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await SandboxFirmAsync(factory, "acme");
        using var other = await SandboxFirmAsync(factory, "globex", "owner@globex.test");
        var checkoutId = await ActivateAsync(admin, 20);

        using var read = await other.GetAsync(Url($"admin/billing/checkouts/{checkoutId}"), TestContext.Current.CancellationToken);
        using var completed = await CompleteAsync(other, checkoutId, declines: false);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (read.StatusCode, completed.StatusCode));
        Assert.Equal("Sandbox", (await GetAsync(admin, "admin/billing")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task AConfiguredFirmHasComplimentarySlotsAndNothingToPay()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), new Dictionary<string, string> { ["Firms:0:Slots"] = "50" });
        using var admin = await factory.LogInAsAdminAsync();

        var billing = await GetAsync(admin, "admin/billing");
        using var slots = await admin.PutAsJsonAsync(Url("admin/billing/slots"), new { slots = 60 }, TestContext.Current.CancellationToken);

        Assert.Equal(("Live", "Complimentary", "Complimentary", 50), (
            billing.GetProperty("status").GetString(),
            billing.GetProperty("plan").GetString(),
            billing.GetProperty("slots").GetProperty("limit").GetString(),
            billing.GetProperty("slots").GetProperty("slots").GetInt32()));
        Assert.Equal(JsonValueKind.Null, billing.GetProperty("nextCharge").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, slots.StatusCode);
    }

    private static Uri Url(string path) => new($"/api/portal/{path}", UriKind.Relative);

    /// <summary>Moves the clock, and logs the administrator in again, since the session has expired by then.</summary>
    private static async Task AdvanceToAsync(PropFactory factory, HttpClient admin, DateTimeOffset time)
    {
        await factory.AdvanceAsync(time - factory.Time.GetUtcNow());
        using var login = await PostAsync(admin, "admin/login", new { email = Owner, password = PropFactory.SignupPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string path) =>
        await client.GetFromJsonAsync<JsonElement>(Url(path), TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object? body) =>
        body is null ? client.PostAsync(Url(path), null, TestContext.Current.CancellationToken) : client.PostAsJsonAsync(Url(path), body, TestContext.Current.CancellationToken);

    /// <summary>A firm that signed up and has its server. Returns its administrator's browser.</summary>
    private static async Task<HttpClient> SandboxFirmAsync(PropFactory factory, string firmId, string email = Owner)
    {
        var admin = await factory.SignUpAsync(firmId, email);
        await PropFactory.WaitUntilProvisionedAsync(admin);
        return admin;
    }

    /// <summary>A firm that went live with test payments.</summary>
    private static async Task<HttpClient> LiveFirmAsync(PropFactory factory, string firmId, int slots, int? autoExpandStep = null)
    {
        var admin = await SandboxFirmAsync(factory, firmId);
        using var paid = await CompleteAsync(admin, await ActivateAsync(admin, slots, autoExpandStep), declines: false);
        Assert.Equal(HttpStatusCode.NoContent, paid.StatusCode);
        return admin;
    }

    /// <summary>Starts going live. Returns the test checkout page's id.</summary>
    private static async Task<string> ActivateAsync(HttpClient admin, int slots, int? autoExpandStep = null)
    {
        using var response = await PostAsync(admin, "admin/billing/activate", new { slots, autoExpandStep });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return CheckoutIdOf(await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken));
    }

    private static string CheckoutIdOf(JsonElement response)
    {
        var url = new Uri(response.GetProperty("checkoutUrl").GetString()!);
        Assert.StartsWith("/admin/billing/checkout/test_", url.AbsolutePath, StringComparison.Ordinal);
        return url.Segments[^1];
    }

    private static Task<HttpResponseMessage> CompleteAsync(HttpClient admin, string checkoutId, bool declines) =>
        PostAsync(admin, $"admin/billing/checkouts/{checkoutId}/complete", new { declines });

    /// <summary>Saves a new test card, which pays or declines from now on.</summary>
    private static async Task NewCardAsync(HttpClient admin, bool declines)
    {
        using var response = await PostAsync(admin, "admin/billing/card", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var saved = await CompleteAsync(admin, CheckoutIdOf(await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)), declines);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
    }

    private static async Task<Guid> StartAsync(HttpClient admin, string email)
    {
        using var response = await PostAsync(admin, "admin/accounts", new { email, challengeId = "two-step-100k" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> WaitForBillingAsync(HttpClient admin, Func<JsonElement, bool> condition, string what)
    {
        JsonElement billing = default;
        await Eventually.ThatAsync(
            async () =>
            {
                billing = await GetAsync(admin, "admin/billing");
                return condition(billing);
            },
            what);
        return billing;
    }

    private static async Task<JsonElement> WaitForAccountAsync(HttpClient admin, Guid accountId, Func<JsonElement, bool> condition)
    {
        JsonElement account = default;
        await Eventually.ThatAsync(
            async () =>
            {
                account = await GetAsync(admin, $"admin/accounts/{accountId}");
                return condition(account);
            },
            $"account {accountId} to change");
        return account;
    }
}
