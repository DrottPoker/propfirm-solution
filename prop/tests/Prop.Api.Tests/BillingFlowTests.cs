using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Prop.Api.Billing;
using Prop.Api.Tests.Support;

namespace Prop.Api.Tests;

/// <summary>
/// What firms pay us, with test payments: going live, more and fewer slots, each month in advance, a month that
/// starts unpaid and pauses the firm's challenges, automatic expansion and the slots warning.
/// </summary>
public sealed class BillingFlowTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Owner = "owner@firm.test";

    // A package of one slot, so a few challenges fill the slots.
    private static readonly Dictionary<string, string> FewSlots = new() { ["Billing:PackageSlots"] = "1", ["Billing:SlotPrices:0:From"] = "2" };

    [Fact]
    public async Task AFirmInTheSandboxGoesLiveByPayingTheStartupFeeAndItsFirstMonth()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await SandboxFirmAsync(factory, "acme");
        var sandboxAccount = await StartAsync(admin, "tester@firm.test");
        var before = await GetAsync(admin, "admin/billing");
        var quote = await GetAsync(admin, "admin/billing/quote?slots=30");

        var checkoutId = await ActivateAsync(admin, 30);
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
        Assert.Equal(("Activation", 1157.25m, "2026-11-01"), (quote.GetProperty("kind").GetString(), quote.GetProperty("amount").GetDecimal(), quote.GetProperty("from").GetString()));
        Assert.Equal(
            ["Startup fee", "Package with 25 slots, October 2026 (27 of 31 days)", "5 extra slots, October 2026 (27 of 31 days)"],
            quote.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("description").GetString()));
        Assert.Equal(("Payment", "Open", 1157.25m), (page.GetProperty("purpose").GetString(), page.GetProperty("status").GetString(), page.GetProperty("amount").GetDecimal()));
        Assert.Equal(HttpStatusCode.PaymentRequired, declined.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, paid.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        Assert.Equal(("Live", "Paid", "Paid", 30, 0, 30), (
            after.GetProperty("status").GetString(),
            after.GetProperty("plan").GetString(),
            after.GetProperty("slots").GetProperty("limit").GetString(),
            after.GetProperty("slots").GetProperty("slots").GetInt32(),
            after.GetProperty("slots").GetProperty("used").GetInt32(),
            after.GetProperty("nextMonthSlots").GetInt32()));
        Assert.Equal(("Test", "4242"), (after.GetProperty("card").GetProperty("brand").GetString(), after.GetProperty("card").GetProperty("last4").GetString()));
        var charge = Assert.Single(after.GetProperty("charges").EnumerateArray());
        Assert.Equal(("Activation", "Paid", 1157.25m, 3), (charge.GetProperty("kind").GetString(), charge.GetProperty("status").GetString(), charge.GetProperty("amount").GetDecimal(), charge.GetProperty("lines").GetArrayLength()));
        var next = after.GetProperty("nextCharge");
        Assert.Equal(("2026-11-01", 30, 525m), (next.GetProperty("month").GetString(), next.GetProperty("slots").GetInt32(), next.GetProperty("amount").GetDecimal()));
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero), next.GetProperty("chargeAt").GetDateTimeOffset());
        Assert.Equal("Live", branding.GetProperty("status").GetString());
        Assert.Equal("Cancelled", account.GetProperty("account").GetProperty("status").GetString());
    }

    // A Swedish firm pays our VAT, its charges and invoices count in its own series, and going live emails the receipt.
    [Fact]
    public async Task ASwedishFirmPaysVatAndGetsAnInvoiceForEachPaidCharge()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await SandboxFirmAsync(factory, "acme", approved: false);
        await factory.ApproveAsync(admin, "acme", PropFactory.Application(country: "SE", vatNumber: "SE559000123401"));
        using var other = await SandboxFirmAsync(factory, "beta", "owner@beta.test");
        using var otherPaid = await CompleteAsync(other, await ActivateAsync(other, 30), declines: false);
        await StartAsync(admin, "tester@firm.test");
        var before = await GetAsync(admin, "admin/billing");
        var quote = await GetAsync(admin, "admin/billing/quote?slots=30&expandBy=10");

        using var paid = await CompleteAsync(admin, await ActivateAsync(admin, 30), declines: false);
        var billing = await GetAsync(admin, "admin/billing");
        var charge = Assert.Single(billing.GetProperty("charges").EnumerateArray());
        var chargeId = charge.GetProperty("id").GetGuid();
        using var invoice = await admin.GetAsync(Url($"admin/billing/charges/{chargeId}/invoice"), TestContext.Current.CancellationToken);
        var pdf = await invoice.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        using var othersInvoice = await other.GetAsync(Url($"admin/billing/charges/{chargeId}/invoice"), TestContext.Current.CancellationToken);
        var otherCharge = Assert.Single((await GetAsync(other, "admin/billing")).GetProperty("charges").EnumerateArray());
        var live = await factory.Emails.WaitForAsync(Owner, "Firm acme is live");
        var ended = await factory.Emails.WaitForAsync("tester@firm.test", "Your test account with Firm acme has ended");

        Assert.Equal(("Charged", 25m, 1), (before.GetProperty("vat").GetProperty("treatment").GetString(), before.GetProperty("vat").GetProperty("percent").GetDecimal(), before.GetProperty("sandboxAccounts").GetInt32()));
        Assert.Equal((1157.25m, 289.31m, 1446.56m), (quote.GetProperty("netAmount").GetDecimal(), quote.GetProperty("vatAmount").GetDecimal(), quote.GetProperty("amount").GetDecimal()));
        Assert.Equal((10, 50m, 43.55m), (
            quote.GetProperty("expansion").GetProperty("slots").GetInt32(),
            quote.GetProperty("expansion").GetProperty("monthlyPrice").GetDecimal(),
            quote.GetProperty("expansion").GetProperty("restOfMonth").GetDecimal()));
        Assert.Equal(HttpStatusCode.NoContent, paid.StatusCode);
        Assert.Equal((1001L, "ACME-0001", 1157.25m, 289.31m, 1446.56m), (
            charge.GetProperty("number").GetInt64(),
            charge.GetProperty("invoice").GetString(),
            charge.GetProperty("netAmount").GetDecimal(),
            charge.GetProperty("vatAmount").GetDecimal(),
            charge.GetProperty("amount").GetDecimal()));
        Assert.Equal((HttpStatusCode.OK, "application/pdf"), (invoice.StatusCode, invoice.Content.Headers.ContentType?.MediaType));
        Assert.Equal("%PDF-"u8.ToArray(), pdf[..5]);
        Assert.Equal(HttpStatusCode.NotFound, othersInvoice.StatusCode);
        Assert.Equal((1001L, "BETA-0001", "ReverseCharge", 0m, 1157.25m), (
            otherCharge.GetProperty("number").GetInt64(),
            otherCharge.GetProperty("invoice").GetString(),
            otherCharge.GetProperty("vatTreatment").GetString(),
            otherCharge.GetProperty("vatAmount").GetDecimal(),
            otherCharge.GetProperty("amount").GetDecimal()));
        Assert.Contains("Receipt for invoice ACME-0001", live.Body, StringComparison.Ordinal);
        Assert.Contains("VAT 25%: 289.31 USD", live.Body, StringComparison.Ordinal);
        Assert.Contains("Total paid: 1,446.56 USD", live.Body, StringComparison.Ordinal);
        Assert.Contains("Your test account from the sandbox has ended", live.Body, StringComparison.Ordinal);
        Assert.Equal("Firm acme", ended.FromName);
        Assert.Contains("account #1001", ended.Body, StringComparison.Ordinal);
    }

    // Where test payments stop when the firm is live, a shop on them would close at once, so the firm sets it up first.
    [Fact]
    public async Task AShopThatWouldStopTakingPaymentKeepsTheFirmFromGoingLive()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), new Dictionary<string, string> { ["Payments:TestPaymentsForLiveFirms"] = "false" });
        using var admin = await SandboxFirmAsync(factory, "acme");

        using var test = await admin.PutAsJsonAsync(Url("admin/firm/payments"), new { provider = "Test" }, TestContext.Current.CancellationToken);
        var billing = await GetAsync(admin, "admin/billing");
        var quote = await GetAsync(admin, "admin/billing/quote?slots=30");
        using var refused = await PostAsync(admin, "admin/billing/activate", new { slots = 30 });
        using var noSales = await admin.PutAsJsonAsync(Url("admin/firm/payments"), new { provider = (string?)null }, TestContext.Current.CancellationToken);
        var after = await GetAsync(admin, "admin/billing");

        // Live, the shop takes only real money.
        using var paid = await CompleteAsync(admin, await ActivateAsync(admin, 30), declines: false);
        using var testAgain = await admin.PutAsJsonAsync(Url("admin/firm/payments"), new { provider = "Test" }, TestContext.Current.CancellationToken);
        using var testKeys = await admin.PutAsJsonAsync(Url("admin/firm/payments"), new { provider = "Stripe", stripeSecretKey = FakeStripe.SecretKey }, TestContext.Current.CancellationToken);
        var live = await GetAsync(admin, "admin/billing");

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (test.StatusCode, noSales.StatusCode));
        Assert.StartsWith("Your shop takes test payments, which stop when you go live.", billing.GetProperty("goLiveProblem").GetString(), StringComparison.Ordinal);
        Assert.Equal(billing.GetProperty("goLiveProblem").GetString(), billing.GetProperty("shopProblem").GetString());
        Assert.Equal(billing.GetProperty("goLiveProblem").GetString(), quote.GetProperty("problem").GetString());
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null), (after.GetProperty("goLiveProblem").ValueKind, after.GetProperty("shopProblem").ValueKind));
        Assert.Equal(HttpStatusCode.NoContent, paid.StatusCode);
        Assert.Equal((HttpStatusCode.UnprocessableEntity, HttpStatusCode.UnprocessableEntity), (testAgain.StatusCode, testKeys.StatusCode));
        Assert.Empty(factory.Stripe.Webhooks);
        Assert.Equal(JsonValueKind.Null, live.GetProperty("shopProblem").ValueKind);
    }

    // The firm's tests show in its figures while it tries the platform, but not once it is live.
    [Fact]
    public async Task TestPurchasesInTheSandboxCountUntilTheFirmGoesLive()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await SandboxFirmAsync(factory, "acme");
        (await admin.PutAsJsonAsync(Url("admin/challenges/two-step-100k/price"), new { amount = 99m, currency = "USD", forSale = true }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync(Url("admin/firm/payments"), new { provider = "Test" }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        using var buyer = factory.CreatePortalClient(PropFactory.HostOf("acme"));
        using var ordered = await PostAsync(buyer, "orders", new { challengeId = "two-step-100k", email = "tester@test.example", acceptTerms = true, name = "Ann Buyer", country = "SE" });
        var order = await ordered.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var token = order.GetProperty("checkoutUrl").GetString()!.Split("token=")[1].Split('&')[0];
        (await PostAsync(buyer, $"orders/{order.GetProperty("orderId").GetGuid()}/test-payment", new { token })).EnsureSuccessStatusCode();
        var sale = await factory.Emails.WaitForAsync(Owner, "New sale: Two-step 100K for 99.00 USD");

        var inSandbox = await GetAsync(admin, "admin/overview");
        using var paid = await CompleteAsync(admin, await ActivateAsync(admin, 30), declines: false);
        var live = await GetAsync(admin, "admin/overview");

        Assert.Contains("Ann Buyer (tester@test.example) bought Two-step 100K for 99.00 USD in your portal (order 1001).", sale.Body, StringComparison.Ordinal);

        // Our emails to firms have the same text as HTML in our look, with the main link as a button.
        Assert.Contains("""<a href="mailto:tester@test.example" """, sale.Html, StringComparison.Ordinal);
        Assert.Contains("style=\"display:inline-block;background:#c9a35b;color:#15120c", sale.Html, StringComparison.Ordinal);
        Assert.Contains(">Open the account</a>", sale.Html, StringComparison.Ordinal);
        Assert.Contains("Choose which emails the firm sends under Notifications in the admin panel.</p>", sale.Html, StringComparison.Ordinal);
        Assert.Equal(1, inSandbox.GetProperty("sales").GetProperty("orders").GetInt32());
        Assert.Single(inSandbox.GetProperty("weeks").EnumerateArray().Last().GetProperty("sales").EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, paid.StatusCode);
        Assert.Equal(0, live.GetProperty("sales").GetProperty("orders").GetInt32());
        Assert.Empty(live.GetProperty("weeks").EnumerateArray().Last().GetProperty("sales").EnumerateArray());
        Assert.Equal((true, true), (await factory.ScalarAsync("select sandbox from orders"), await factory.ScalarAsync("select sandbox from challenge_accounts")));
    }

    // Its traders find the firm's server in the terminal once it is live, and log in through its portal.
    [Fact]
    public async Task TheTerminalListsTheFirmOnceItIsLive()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await SandboxFirmAsync(factory, "acme");
        await Eventually.ThatAsync(() => factory.Trading.ListingOf("acme") is not null, "the trading platform to hear where the firm's traders log in");
        var inSandbox = factory.Trading.ListingOf("acme");

        using var paid = await CompleteAsync(admin, await ActivateAsync(admin, 30), declines: false);
        await Eventually.ThatAsync(() => factory.Trading.ListingOf("acme") is { Listed: true }, "the firm's server to be listed");

        // The terminal shows the firm's logo with its name, from the portal's address.
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new ByteArrayContent(png), "file", "logo.png");
            (await admin.PutAsync(Url("admin/firm/logo"), form, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        }

        await Eventually.ThatAsync(() => factory.Trading.ListingOf("acme") is { LogoUrl: not null }, "the trading platform to hear about the logo");

        Assert.Equal((false, new Uri("http://acme.localhost:3002/terminal"), (Uri?)null), inSandbox);
        Assert.Equal(HttpStatusCode.NoContent, paid.StatusCode);
        Assert.Equal(new Uri("http://acme.localhost:3002/terminal"), factory.Trading.ListingOf("acme")!.Value.LoginUrl);
        Assert.Equal(
            new Uri($"http://acme.localhost:3002/api/portal/logo/{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(png))}"),
            factory.Trading.ListingOf("acme")!.Value.LogoUrl);
        Assert.Null(factory.Trading.ListingOf("demo-firm"));
    }

    [Fact]
    public async Task GoingLiveNeedsOurApprovalOfTheFirm()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await SandboxFirmAsync(factory, "acme", approved: false);

        using var response = await PostAsync(admin, "admin/billing/activate", new { slots = 30 });
        var billing = await GetAsync(admin, "admin/billing");
        var quote = await GetAsync(admin, "admin/billing/quote?slots=30");
        var tooFew = await GetAsync(admin, "admin/billing/quote?slots=10");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.StartsWith("Choose 25 to 10,000 slots.", tooFew.GetProperty("problem").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("We review your company before you go live", billing.GetProperty("goLiveProblem").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, billing.GetProperty("review").ValueKind);
        Assert.Equal(billing.GetProperty("goLiveProblem").GetString(), quote.GetProperty("problem").GetString());
    }

    [Fact]
    public async Task AGoLiveThatIsStartedAgainOrNotPaidInTimeIsVoid()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await SandboxFirmAsync(factory, "acme");
        var first = await ActivateAsync(admin, 30);
        var second = await ActivateAsync(admin, 40);

        await factory.AdvanceUntilAsync(
            TimeSpan.FromHours(1),
            async () => (await GetAsync(admin, $"admin/billing/checkouts/{second}")).GetProperty("status").GetString() == "Expired",
            "the checkout to expire");
        var third = await ActivateAsync(admin, 35);
        using var paid = await CompleteAsync(admin, third, declines: false);
        var billing = await GetAsync(admin, "admin/billing");

        Assert.Equal("Expired", (await GetAsync(admin, $"admin/billing/checkouts/{first}")).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NoContent, paid.StatusCode);
        Assert.Equal(35, billing.GetProperty("slots").GetProperty("slots").GetInt32());
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
        using var admin = await LiveFirmAsync(factory, "acme", slots: 30);

        // From Monday 5 October at 08:00 to 27 October, when November is due.
        await AdvanceToAsync(factory, admin, new DateTimeOffset(2026, 10, 27, 0, 0, 1, TimeSpan.Zero));
        var billing = await WaitForBillingAsync(admin, b => b.GetProperty("charges").GetArrayLength() == 2, "November's charge");

        var renewal = billing.GetProperty("charges")[0];
        Assert.Equal(("Renewal", "Paid", "2026-11-01", 525m), (renewal.GetProperty("kind").GetString(), renewal.GetProperty("status").GetString(), renewal.GetProperty("month").GetString(), renewal.GetProperty("amount").GetDecimal()));
        Assert.Equal("2026-12-01", billing.GetProperty("nextCharge").GetProperty("month").GetString());
    }

    [Fact]
    public async Task AMonthThatStartsUnpaidPausesTheChallengesUntilItIsPaid()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await LiveFirmAsync(factory, "acme", slots: 30);
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
    public async Task ASuspensionAndAnUnpaidMonthBothKeepTheChallengesPaused()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await LiveFirmAsync(factory, "acme", slots: 30);
        var accountId = await StartAsync(admin, "anna@test.example");
        await WaitForAccountAsync(admin, accountId, a => a.GetProperty("account").GetProperty("status").GetString() == "Active");
        await NewCardAsync(admin, declines: true);
        await AdvanceToAsync(factory, admin, new DateTimeOffset(2026, 10, 27, 0, 0, 1, TimeSpan.Zero));
        await AdvanceToAsync(factory, admin, new DateTimeOffset(2026, 11, 1, 0, 0, 1, TimeSpan.Zero));
        await WaitForAccountAsync(admin, accountId, a => a.GetProperty("account").GetProperty("paused").GetBoolean());
        using var ops = await factory.LogInAsStaffAsync();

        // Paid while suspended: the challenges stay paused.
        using var suspended = await PostAsync(ops, "ops/firms/acme/suspend", new { reason = "We need to talk about your payouts." });
        await NewCardAsync(admin, declines: false);
        await WaitForBillingAsync(admin, b => b.GetProperty("unpaidSince").ValueKind == JsonValueKind.Null, "the firm to be paid");
        await factory.AdvanceAsync(BillingWorker.PollInterval);
        var whileSuspended = await GetAsync(admin, $"admin/accounts/{accountId}");

        using var lifted = await PostAsync(ops, "ops/firms/acme/unsuspend", null);
        await WaitForAccountAsync(admin, accountId, a => !a.GetProperty("account").GetProperty("paused").GetBoolean());

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (suspended.StatusCode, lifted.StatusCode));
        Assert.True(whileSuspended.GetProperty("account").GetProperty("paused").GetBoolean());
        await Eventually.ThatAsync(() => !factory.Trading.AccountOf("acme-1001-1").Suspended, "the trading account to be resumed");
    }

    [Fact]
    public async Task AnUnpaidMonthCanBePaidWithAnotherCardOnACheckoutPage()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await LiveFirmAsync(factory, "acme", slots: 30);
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
        using var admin = await LiveFirmAsync(factory, "acme", slots: 30);
        await NewCardAsync(admin, declines: true);
        await AdvanceToAsync(factory, admin, new DateTimeOffset(2026, 10, 27, 0, 0, 1, TimeSpan.Zero));
        await WaitForBillingAsync(admin, b => b.GetProperty("charges")[0].GetProperty("failure").ValueKind == JsonValueKind.String, "November's charge to be declined");

        var quote = await GetAsync(admin, "admin/billing/quote?slots=25");
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
        var checkoutId = await ActivateAsync(admin, 30);

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

    /// <summary>A firm that signed up and has its server, and that we approved unless told not to. Returns its administrator's browser.</summary>
    private static async Task<HttpClient> SandboxFirmAsync(PropFactory factory, string firmId, string email = Owner, bool approved = true)
    {
        var admin = await factory.SignUpAsync(firmId, email);
        await PropFactory.WaitUntilProvisionedAsync(admin);
        if (approved)
        {
            await factory.ApproveAsync(admin, firmId);
        }

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
