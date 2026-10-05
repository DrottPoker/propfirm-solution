using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Prop.Api.Tests.Support;

namespace Prop.Api.Tests;

/// <summary>What firms pay us through our own Stripe account: Checkout to go live and save cards, and charges of the saved card.</summary>
public sealed class StripeBillingTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly Dictionary<string, string> WithStripe = new()
    {
        ["Billing:Provider"] = "Stripe",
        ["Billing:StripeSecretKey"] = FakeStripe.PlatformKey,
        ["Billing:StripeWebhookSecret"] = FakeStripe.PlatformWebhookSecret,
    };

    [Fact]
    public async Task GoingLiveIsPaidOnAStripeCheckoutOfOursAndItsWebhookTakesTheFirmLiveOnce()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), WithStripe);
        using var admin = await SandboxFirmAsync(factory);

        using var response = await PostAsync(admin, "admin/billing/activate", new { slots = 30 });
        var checkoutUrl = (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("checkoutUrl").GetString();
        var session = Assert.Single(factory.Stripe.Requests);
        using var forged = await StripeWebhooks.SendBillingAsync(factory, FakeStripe.BillingCheckoutEvent("checkout.session.completed", session), FakeStripe.WebhookSecret);
        using var completed = await StripeWebhooks.SendBillingAsync(factory, FakeStripe.BillingCheckoutEvent("checkout.session.completed", session));
        using var again = await StripeWebhooks.SendBillingAsync(factory, FakeStripe.BillingCheckoutEvent("checkout.session.completed", session));
        var billing = await GetAsync(admin, "admin/billing");

        Assert.Equal("https://checkout.stripe.test/c/pay/cs_test_1", checkoutUrl);
        Assert.Equal($"Bearer {FakeStripe.PlatformKey}", session.Authorization);
        Assert.Equal(("payment", "card", "always", "owner@firm.test", "off_session"), (
            session.Form["mode"],
            session.Form["payment_method_types[0]"],
            session.Form["customer_creation"],
            session.Form["customer_email"],
            session.Form["payment_intent_data[setup_future_usage]"]));
        Assert.Equal(("Startup fee", "70000", "Package with 25 slots, October 2026 (27 of 31 days)", "43548", "2177"), (
            session.Form["line_items[0][price_data][product_data][name]"],
            session.Form["line_items[0][price_data][unit_amount]"],
            session.Form["line_items[1][price_data][product_data][name]"],
            session.Form["line_items[1][price_data][unit_amount]"],
            session.Form["line_items[2][price_data][unit_amount]"]));
        Assert.Equal("http://acme.localhost:3002/admin/go-live?checkout={CHECKOUT_SESSION_ID}", session.Form["success_url"]);
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (completed.StatusCode, again.StatusCode));
        Assert.Equal(("Live", "visa", "4242"), (billing.GetProperty("status").GetString(), billing.GetProperty("card").GetProperty("brand").GetString(), billing.GetProperty("card").GetProperty("last4").GetString()));
        var charge = Assert.Single(billing.GetProperty("charges").EnumerateArray());
        Assert.Equal(("Paid", 1157.25m), (charge.GetProperty("status").GetString(), charge.GetProperty("amount").GetDecimal()));
    }

    // Stripe takes the VAT as a line of its own, so the lines add up to what the charge says is paid.
    [Fact]
    public async Task VatIsALineOfItsOwnOnTheStripeCheckout()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), WithStripe);
        using var admin = await factory.SignUpAsync("acme");
        await PropFactory.WaitUntilProvisionedAsync(admin);
        await factory.ApproveAsync(admin, "acme", PropFactory.Application(country: "SE", vatNumber: "SE559000123401"));

        using var response = await PostAsync(admin, "admin/billing/activate", new { slots = 30 });
        var session = Assert.Single(factory.Stripe.Requests);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("VAT 25%", "28931"), (session.Form["line_items[3][price_data][product_data][name]"], session.Form["line_items[3][price_data][unit_amount]"]));
    }

    [Fact]
    public async Task TheDepositIsPaidOnAStripeCheckoutAndItsCustomerPaysToGoLive()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), new Dictionary<string, string>(WithStripe) { ["Billing:ReviewDeposit"] = "100" });
        using var admin = await factory.SignUpAsync("acme");
        await PropFactory.WaitUntilProvisionedAsync(admin);
        using var saved = await admin.PutAsJsonAsync(Url("admin/verification/application"), PropFactory.Application(), TestContext.Current.CancellationToken);
        await factory.ChooseIdentityChecksAsync(admin, "acme");

        using var submitted = await PostAsync(admin, "admin/verification/submit", null);
        var deposit = Assert.Single(factory.Stripe.Requests);
        using var completed = await StripeWebhooks.SendBillingAsync(factory, FakeStripe.BillingCheckoutEvent("checkout.session.completed", deposit));
        var verification = await GetAsync(admin, "admin/verification");
        using var ops = await factory.LogInAsStaffAsync();
        using var approved = await PostAsync(ops, "ops/firms/acme/approve", new { message = (string?)null });
        using var activated = await PostAsync(admin, "admin/billing/activate", new { slots = 30 });
        var goLive = factory.Stripe.Requests[^1];

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (saved.StatusCode, submitted.StatusCode));
        Assert.Equal(("payment", "always", "10000"), (deposit.Form["mode"], deposit.Form["customer_creation"], deposit.Form["line_items[0][price_data][unit_amount]"]));
        Assert.Equal("http://acme.localhost:3002/admin/go-live?checkout={CHECKOUT_SESSION_ID}", deposit.Form["success_url"]);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal(("Submitted", true), (verification.GetProperty("status").GetString(), verification.GetProperty("deposit").GetProperty("paid").GetBoolean()));
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (approved.StatusCode, activated.StatusCode));
        Assert.Equal((FakeStripe.Customer, "Startup fee, less the deposit of 100.00 USD", "60000"), (
            goLive.Form["customer"],
            goLive.Form["line_items[0][price_data][product_data][name]"],
            goLive.Form["line_items[0][price_data][unit_amount]"]));
    }

    [Fact]
    public async Task TheMonthIsChargedToTheSavedCardWithAKeyForEachAttempt()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), WithStripe);
        using var admin = await LiveFirmAsync(factory);

        await factory.AdvanceUntilAsync(
            new DateTimeOffset(2026, 10, 27, 0, 0, 1, TimeSpan.Zero) - factory.Time.GetUtcNow(), () => factory.Stripe.Charges.Count == 1, "November's charge");
        var charge = factory.Stripe.Charges[0];

        Assert.Equal(("52500", "usd", FakeStripe.Customer, "pm_card_visa"), (charge.Form["amount"], charge.Form["currency"], charge.Form["customer"], charge.Form["payment_method"]));
        Assert.Equal(("true", "true", "card_on_file"), (charge.Form["off_session"], charge.Form["confirm"], charge.Form["metadata[source]"]));
        Assert.Equal($"charge-{charge.Form["metadata[charge_id]"]}-1-pm_card_visa", charge.IdempotencyKey);
    }

    [Fact]
    public async Task ADeclinedCardGivesStripesReasonAndBuysNoSlots()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), WithStripe);
        using var admin = await LiveFirmAsync(factory);
        factory.Stripe.DeclineCharges(true);

        using var response = await admin.PutAsJsonAsync(Url("admin/billing/slots"), new { slots = 40 }, TestContext.Current.CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var billing = await GetAsync(admin, "admin/billing");

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        Assert.Equal("The card was declined: Your card was declined.", problem.GetProperty("title").GetString());
        Assert.Equal(30, billing.GetProperty("slots").GetProperty("slots").GetInt32());
        Assert.Equal(("Slots", "Void"), (billing.GetProperty("charges")[0].GetProperty("kind").GetString(), billing.GetProperty("charges")[0].GetProperty("status").GetString()));
    }

    [Fact]
    public async Task ANewCardIsSavedFromAStripeSetupPage()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), WithStripe);
        using var admin = await LiveFirmAsync(factory);

        using var response = await PostAsync(admin, "admin/billing/card", null);
        var session = factory.Stripe.Requests[^1];
        using var completed = await StripeWebhooks.SendBillingAsync(factory, FakeStripe.BillingCheckoutEvent("checkout.session.completed", session));
        var billing = await GetAsync(admin, "admin/billing");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("setup", FakeStripe.Customer, "card"), (session.Form["mode"], session.Form["customer"], session.Form["payment_method_types[0]"]));
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal(("mastercard", "4444"), (billing.GetProperty("card").GetProperty("brand").GetString(), billing.GetProperty("card").GetProperty("last4").GetString()));
    }

    [Fact]
    public async Task AGoLiveStartedAgainExpiresTheFirstStripePage()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), WithStripe);
        using var admin = await SandboxFirmAsync(factory);

        using var first = await PostAsync(admin, "admin/billing/activate", new { slots = 30 });
        using var second = await PostAsync(admin, "admin/billing/activate", new { slots = 40 });

        Assert.Equal(["cs_test_1"], factory.Stripe.Expired);
    }

    [Fact]
    public async Task APaymentStripeReportsForOurOwnChargeIsCounted()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), WithStripe);
        using var admin = await LiveFirmAsync(factory);
        factory.Stripe.DeclineCharges(true);
        await factory.AdvanceUntilAsync(
            new DateTimeOffset(2026, 10, 27, 0, 0, 1, TimeSpan.Zero) - factory.Time.GetUtcNow(), () => factory.Stripe.Charges.Count == 1, "November's charge");
        var chargeId = Guid.Parse(factory.Stripe.Charges[0].Form["metadata[charge_id]"]);

        // A payment that is not ours, and one for another amount, change nothing.
        using var checkoutPayment = await StripeWebhooks.SendBillingAsync(factory, FakeStripe.ChargeSucceededEvent(chargeId, 52_500, source: "checkout"));
        using var wrongAmount = await StripeWebhooks.SendBillingAsync(factory, FakeStripe.ChargeSucceededEvent(chargeId, 52_499));
        using var reported = await StripeWebhooks.SendBillingAsync(factory, FakeStripe.ChargeSucceededEvent(chargeId, 52_500));
        using var login = await PostAsync(admin, "admin/login", new { email = "owner@firm.test", password = PropFactory.SignupPassword });
        var billing = await GetAsync(admin, "admin/billing");

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK), (checkoutPayment.StatusCode, wrongAmount.StatusCode, reported.StatusCode));
        var renewal = billing.GetProperty("charges")[0];
        Assert.Equal(("Renewal", "Paid", $"pi_reported_{chargeId:N}"), (renewal.GetProperty("kind").GetString(), renewal.GetProperty("status").GetString(), Reference(factory, chargeId)));
    }

    [Fact]
    public async Task SlotsDelayedByStripePastTheMonthsChargeAlsoHoldNextMonth()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), WithStripe);
        using var admin = await LiveFirmAsync(factory);

        // Stripe fails the purchase just before November is charged, so it is tried again after November is paid.
        await factory.AdvanceAsync(new DateTimeOffset(2026, 10, 26, 23, 58, 0, TimeSpan.Zero) - factory.Time.GetUtcNow());
        using var login = await PostAsync(admin, "admin/login", new { email = "owner@firm.test", password = PropFactory.SignupPassword });
        factory.Stripe.FailNextCharges(1);
        using var purchase = await admin.PutAsJsonAsync(Url("admin/billing/slots"), new { slots = 40 }, TestContext.Current.CancellationToken);
        // November is charged at midnight, and the purchase is tried again at 00:03.
        await factory.AdvanceUntilAsync(
            TimeSpan.FromMinutes(3), () => factory.Stripe.Charges.Count == 2, "November's charge", latest: new DateTimeOffset(2026, 10, 27, 0, 2, 50, TimeSpan.Zero));
        await factory.AdvanceUntilAsync(TimeSpan.FromMinutes(5), () => factory.Stripe.Charges.Count == 4, "the purchase and the rest for November");
        var billing = await GetAsync(admin, "admin/billing");

        var charges = factory.Stripe.Charges;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, purchase.StatusCode);
        Assert.Equal(charges[0].Form["metadata[charge_id]"], charges[2].Form["metadata[charge_id]"]);
        Assert.NotEqual(charges[0].IdempotencyKey, charges[2].IdempotencyKey);
        Assert.Equal(("52500", "5000"), (charges[1].Form["amount"], charges[3].Form["amount"]));
        var forNovember = billing.GetProperty("charges").EnumerateArray().First(c => c.GetProperty("kind").GetString() == "Slots" && c.GetProperty("month").GetString() == "2026-11-01");
        Assert.Equal(("Paid", 40), (forNovember.GetProperty("status").GetString(), forNovember.GetProperty("slots").GetInt32()));
    }

    [Fact]
    public async Task ASecondPaymentForAPaidChargeIsRecordedForARefund()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), WithStripe);
        using var admin = await LiveFirmAsync(factory);
        await factory.AdvanceUntilAsync(
            new DateTimeOffset(2026, 10, 27, 0, 0, 1, TimeSpan.Zero) - factory.Time.GetUtcNow(), () => factory.Stripe.Charges.Count == 1, "November's charge");
        var chargeId = Guid.Parse(factory.Stripe.Charges[0].Form["metadata[charge_id]"]);
        await Eventually.ThatAsync(() => Reference(factory, chargeId) is not null, "November to be paid");

        using var again = await StripeWebhooks.SendBillingAsync(factory, FakeStripe.ChargeSucceededEvent(chargeId, 52_500));

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal("pi_offsession_1", Reference(factory, chargeId));
        Assert.Contains("paid_twice", EventTypes(factory, chargeId));
    }

    private static Uri Url(string path) => new($"/api/portal/{path}", UriKind.Relative);

    private static async Task<JsonElement> GetAsync(HttpClient client, string path) =>
        await client.GetFromJsonAsync<JsonElement>(Url(path), TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object? body) =>
        body is null ? client.PostAsync(Url(path), null, TestContext.Current.CancellationToken) : client.PostAsJsonAsync(Url(path), body, TestContext.Current.CancellationToken);

    /// <summary>A firm that signed up, has its server and was approved by us.</summary>
    private static async Task<HttpClient> SandboxFirmAsync(PropFactory factory)
    {
        var admin = await factory.SignUpAsync("acme");
        await PropFactory.WaitUntilProvisionedAsync(admin);
        await factory.ApproveAsync(admin, "acme");
        return admin;
    }

    /// <summary>A firm that went live with 30 slots, paid on Stripe Checkout.</summary>
    private static async Task<HttpClient> LiveFirmAsync(PropFactory factory)
    {
        var admin = await SandboxFirmAsync(factory);
        using var response = await PostAsync(admin, "admin/billing/activate", new { slots = 30 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var completed = await StripeWebhooks.SendBillingAsync(factory, FakeStripe.BillingCheckoutEvent("checkout.session.completed", factory.Stripe.Requests[^1]));
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        return admin;
    }

    private static List<string> EventTypes(PropFactory factory, Guid chargeId)
    {
        using var connection = new Npgsql.NpgsqlConnection(factory.ConnectionString);
        connection.Open();
        using var command = new Npgsql.NpgsqlCommand("select type from billing_events where charge_id = $1 order by id", connection);
        command.Parameters.AddWithValue(chargeId);
        using var reader = command.ExecuteReader();
        var types = new List<string>();
        while (reader.Read())
        {
            types.Add(reader.GetString(0));
        }

        return types;
    }

    // The provider's reference of the charge, which the admin panel does not show.
    private static string? Reference(PropFactory factory, Guid chargeId)
    {
        using var connection = new Npgsql.NpgsqlConnection(factory.ConnectionString);
        connection.Open();
        using var command = new Npgsql.NpgsqlCommand("select payment_reference from billing_charges where id = $1", connection);
        command.Parameters.AddWithValue(chargeId);
        return command.ExecuteScalar() as string;
    }
}
