using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Prop.Api.Tests.Support;

namespace Prop.Api.Tests;

/// <summary>Buying challenges in the firm's portal: the shop, test payments, Stripe, the firm's own checkout and the orders.</summary>
public sealed class OrderFlowTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Challenge = "two-step-100k";

    [Fact]
    public async Task TheShopOpensWhenTheFirmTakesPaymentAndPricesAChallenge()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");
        await PropFactory.WaitUntilProvisionedAsync(admin);
        using var visitor = factory.CreatePortalClient(PropFactory.HostOf("acme"));

        var before = await GetAsync(visitor, "shop");
        await SetPriceAsync(admin, 99m);
        var priced = await GetAsync(visitor, "shop");
        await SetPaymentsAsync(admin, new { provider = "Test" });
        var open = await GetAsync(visitor, "shop");

        Assert.False(before.GetProperty("open").GetBoolean());
        Assert.False(priced.GetProperty("open").GetBoolean());
        Assert.True(open.GetProperty("open").GetBoolean());
        Assert.True(open.GetProperty("test").GetBoolean());
        var item = Assert.Single(open.GetProperty("items").EnumerateArray());
        Assert.Equal((Challenge, 99m, "USD"), (item.GetProperty("challenge").GetProperty("id").GetString(), item.GetProperty("price").GetDecimal(), item.GetProperty("currency").GetString()));
    }

    [Fact]
    public async Task ABuyerPaysWithATestPaymentAndIsInvitedToThePortal()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await OpenTestShopAsync(factory, "acme");
        using var buyer = factory.CreatePortalClient(PropFactory.HostOf("acme"));

        var (orderId, token, checkoutUrl) = await BuyAsync(buyer, "new.trader@test.example");
        var pending = await GetAsync(buyer, $"orders/{orderId}?token={token}");
        using var withoutToken = await buyer.GetAsync(Url($"orders/{orderId}"), TestContext.Current.CancellationToken);
        using var paidResponse = await buyer.PostAsJsonAsync(Url($"orders/{orderId}/test-payment"), new { token }, TestContext.Current.CancellationToken);
        var paid = await paidResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var invitation = factory.Emails.Sent.Single(e => e.To == "new.trader@test.example");
        using var accepted = await buyer.PostAsJsonAsync(
            Url("invites/accept"), new { token = FakeEmailSender.TokenIn(invitation), password = PropFactory.TraderPassword }, TestContext.Current.CancellationToken);
        var accounts = await buyer.GetFromJsonAsync<JsonElement>(Url("accounts"), TestContext.Current.CancellationToken);

        Assert.StartsWith($"http://acme.localhost:3002/checkout/test?order={orderId}&token=", checkoutUrl, StringComparison.Ordinal);
        Assert.Equal("Pending", pending.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, withoutToken.StatusCode);
        Assert.Equal(HttpStatusCode.OK, paidResponse.StatusCode);
        Assert.Equal("Paid", paid.GetProperty("status").GetString());
        Assert.False(paid.GetProperty("canLogIn").GetBoolean());
        Assert.Equal(JsonValueKind.String, paid.GetProperty("inviteSentAt").ValueKind);
        Assert.Equal("Firm acme", invitation.FromName);
        Assert.Contains("http://acme.localhost:3002/invite?token=", invitation.Body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var account = Assert.Single(accounts.EnumerateArray());
        Assert.Equal(paid.GetProperty("accountId").GetGuid(), account.GetProperty("account").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task ALoggedInTraderBuysWithTheirOwnEmailAndNeedsNoInvitation()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var started = await factory.StartActiveAccountAsync("anna@test.example");
        using var trader = await factory.LogInAsTraderAsync(started.GetProperty("id").GetGuid());

        var (orderId, token, _) = await BuyAsync(trader, "someone.else@test.example", "quick-test-100k");
        using var paid = await trader.PostAsJsonAsync(Url($"orders/{orderId}/test-payment"), new { token }, TestContext.Current.CancellationToken);
        var order = await paid.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var accounts = await trader.GetFromJsonAsync<JsonElement>(Url("accounts"), TestContext.Current.CancellationToken);

        Assert.Equal("anna@test.example", order.GetProperty("email").GetString());
        Assert.True(order.GetProperty("canLogIn").GetBoolean());
        Assert.Empty(factory.Emails.Sent);
        Assert.Equal(["quick-test-100k", Challenge], accounts.EnumerateArray().Select(a => a.GetProperty("account").GetProperty("challengeId").GetString()).Order());
    }

    [Fact]
    public async Task StripeCheckoutUsesTheFirmsOwnKeysAndStripesWebhookStartsTheAccountOnce()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await OpenStripeShopAsync(factory, "acme");
        using var buyer = factory.CreatePortalClient(PropFactory.HostOf("acme"));

        var (orderId, _, checkoutUrl) = await BuyAsync(buyer, "buyer@test.example");
        var session = Assert.Single(factory.Stripe.Requests);
        var token = TokenIn(session.Form["success_url"]);
        using var completed = await StripeWebhooks.SendAsync(factory, "acme", FakeStripe.CheckoutEvent("checkout.session.completed", session));
        using var again = await StripeWebhooks.SendAsync(factory, "acme", FakeStripe.CheckoutEvent("checkout.session.completed", session));
        var order = await GetAsync(buyer, $"orders/{orderId}?token={token}");
        var accounts = await admin.GetFromJsonAsync<JsonElement>(Url("admin/accounts?search=buyer@test.example"), TestContext.Current.CancellationToken);

        Assert.Equal("https://checkout.stripe.test/c/pay/cs_test_1", checkoutUrl);
        Assert.Equal("/v1/checkout/sessions", session.Path);
        Assert.Equal($"Bearer {FakeStripe.SecretKey}", session.Authorization);
        Assert.Equal($"order-{orderId}", session.IdempotencyKey);
        Assert.Equal(("payment", "9900", "usd", "buyer@test.example"), (session.Form["mode"], session.Form["line_items[0][price_data][unit_amount]"], session.Form["line_items[0][price_data][currency]"], session.Form["customer_email"]));
        Assert.StartsWith($"http://acme.localhost:3002/orders/{orderId}?token=", session.Form["success_url"], StringComparison.Ordinal);
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (completed.StatusCode, again.StatusCode));
        Assert.Equal("Paid", order.GetProperty("status").GetString());
        Assert.Single(accounts.GetProperty("accounts").EnumerateArray());
    }

    [Fact]
    public async Task AStripeWebhookWithoutTheFirmsSignatureIsRefused()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await OpenStripeShopAsync(factory, "acme");
        using var buyer = factory.CreatePortalClient(PropFactory.HostOf("acme"));
        var (orderId, _, _) = await BuyAsync(buyer, "buyer@test.example");
        var session = Assert.Single(factory.Stripe.Requests);
        var token = TokenIn(session.Form["success_url"]);
        var completed = FakeStripe.CheckoutEvent("checkout.session.completed", session);

        using var wrongSecret = await StripeWebhooks.SendAsync(factory, "acme", completed, "whsec_SomeoneElsesSecret");
        using var otherFirm = await StripeWebhooks.SendAsync(factory, "demo-firm", completed);
        var order = await GetAsync(buyer, $"orders/{orderId}?token={token}");

        Assert.Equal(HttpStatusCode.BadRequest, wrongSecret.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherFirm.StatusCode);
        Assert.Equal("Pending", order.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AStripePaymentThatDoesNotMatchThePriceStartsNothing()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await OpenStripeShopAsync(factory, "acme");
        using var buyer = factory.CreatePortalClient(PropFactory.HostOf("acme"));
        var (orderId, _, _) = await BuyAsync(buyer, "buyer@test.example");

        using var cheaper = await StripeWebhooks.SendAsync(
            factory, "acme", FakeStripe.CheckoutEvent("checkout.session.completed", Assert.Single(factory.Stripe.Requests), amountTotal: 100));
        var order = await GetAsync(admin, $"admin/orders/{orderId}");

        Assert.Equal(HttpStatusCode.OK, cheaper.StatusCode);
        Assert.Equal("Pending", order.GetProperty("order").GetProperty("status").GetString());
        Assert.Contains("payment_mismatch", order.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("type").GetString()));
    }

    [Fact]
    public async Task StripeRefundsAndDisputesAreMarkedOnTheOrderAndSentToTheFirm()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await OpenStripeShopAsync(factory, "acme");
        (await admin.PutAsJsonAsync(Url("admin/firm/webhook"), new { url = "https://hooks.acme.test/prop" }, TestContext.Current.CancellationToken)).Dispose();
        using var buyer = factory.CreatePortalClient(PropFactory.HostOf("acme"));
        var (orderId, _, _) = await BuyAsync(buyer, "buyer@test.example");
        var session = Assert.Single(factory.Stripe.Requests);

        (await StripeWebhooks.SendAsync(factory, "acme", FakeStripe.CheckoutEvent("checkout.session.completed", session))).Dispose();
        (await StripeWebhooks.SendAsync(factory, "acme", FakeStripe.RefundEvent(session))).Dispose();
        (await StripeWebhooks.SendAsync(factory, "acme", FakeStripe.DisputeEvent(session))).Dispose();
        await Eventually.ThatAsync(() => factory.Webhooks.Delivered("order.disputed").Count == 1, "the webhooks");
        var order = (await GetAsync(admin, $"admin/orders/{orderId}")).GetProperty("order");
        var account = await admin.GetFromJsonAsync<JsonElement>(Url($"admin/accounts/{order.GetProperty("accountId").GetGuid()}"), TestContext.Current.CancellationToken);
        var paidWebhook = Assert.Single(factory.Webhooks.Delivered("order.paid")).Json;

        Assert.Equal(JsonValueKind.String, order.GetProperty("refundedAt").ValueKind);
        Assert.Equal(JsonValueKind.String, order.GetProperty("disputedAt").ValueKind);
        Assert.Equal($"pi_{session.SessionId}", order.GetProperty("paymentReference").GetString());
        Assert.Single(factory.Webhooks.Delivered("order.refunded"));
        Assert.Equal(orderId, paidWebhook.GetProperty("data").GetProperty("order").GetProperty("id").GetGuid());
        Assert.Equal(order.GetProperty("accountId").GetGuid(), paidWebhook.GetProperty("account").GetProperty("id").GetGuid());
        Assert.NotEqual("Cancelled", account.GetProperty("account").GetProperty("status").GetString());
    }

    [Fact]
    public async Task AStripePaymentAfterTheCheckoutExpiredStillCounts()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await OpenStripeShopAsync(factory, "acme");
        using var buyer = factory.CreatePortalClient(PropFactory.HostOf("acme"));
        var (orderId, _, _) = await BuyAsync(buyer, "buyer@test.example");
        var session = Assert.Single(factory.Stripe.Requests);
        var token = TokenIn(session.Form["success_url"]);

        (await StripeWebhooks.SendAsync(factory, "acme", FakeStripe.CheckoutEvent("checkout.session.expired", session, paymentStatus: "unpaid"))).Dispose();
        var expired = await GetAsync(buyer, $"orders/{orderId}?token={token}");
        (await StripeWebhooks.SendAsync(factory, "acme", FakeStripe.CheckoutEvent("checkout.session.async_payment_succeeded", session))).Dispose();
        var paid = await GetAsync(buyer, $"orders/{orderId}?token={token}");

        Assert.Equal("Expired", expired.GetProperty("status").GetString());
        Assert.Equal("Paid", paid.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, paid.GetProperty("accountId").ValueKind);
    }

    [Fact]
    public async Task WhenStripeRefusesTheCheckoutNoOrderIsMade()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await OpenStripeShopAsync(factory, "acme");
        using var buyer = factory.CreatePortalClient(PropFactory.HostOf("acme"));
        factory.Stripe.RefuseNext("Invalid API Key provided.");

        using var refused = await buyer.PostAsJsonAsync(
            Url("orders"), new { challengeId = Challenge, email = "buyer@test.example", acceptTerms = false }, TestContext.Current.CancellationToken);
        var orders = await admin.GetFromJsonAsync<JsonElement>(Url("admin/orders"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.Empty(orders.EnumerateArray());
    }

    [Theory]
    [InlineData("sk_live_51LiveStripeSecretKey", "whsec_FakeStripeWebhookSecret")]
    [InlineData("pk_test_51PublishableKeyOnly", "whsec_FakeStripeWebhookSecret")]
    [InlineData(FakeStripe.SecretKey, "not-a-signing-secret")]
    public async Task OnlyStripeTestKeysAreAcceptedInTheSandbox(string secretKey, string webhookSecret)
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");

        using var refused = await admin.PutAsJsonAsync(
            Url("admin/firm/payments"), new { provider = "Stripe", stripeSecretKey = secretKey, stripeWebhookSecret = webhookSecret }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
    }

    [Fact]
    public async Task StripeKeysAreKeptButNeverShown()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await OpenStripeShopAsync(factory, "acme");

        await SetPaymentsAsync(admin, new { provider = "Test" });
        var settings = await SetPaymentsAsync(admin, new { provider = "Stripe" });

        var payments = settings.GetProperty("payments");
        Assert.Equal(("Stripe", true, true, true), (payments.GetProperty("provider").GetString(), payments.GetProperty("active").GetBoolean(), payments.GetProperty("hasStripeKeys").GetBoolean(), payments.GetProperty("stripeTestMode").GetBoolean()));
        Assert.Equal("http://localhost:5201/api/payments/v1/stripe/acme", payments.GetProperty("stripeWebhookUrl").GetString());
        Assert.DoesNotContain(FakeStripe.SecretKey, settings.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheFirmsOwnCheckoutMarksOrdersPaidThroughTheFirmApi()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");
        await PropFactory.WaitUntilProvisionedAsync(admin);
        await SetPriceAsync(admin, 149m);
        await SetPaymentsAsync(admin, new { provider = "External", checkoutUrl = "https://pay.acme.test/checkout?lang=en" });
        using var firmApi = factory.CreateFirmClient(await NewApiKeyAsync(admin));
        using var buyer = factory.CreatePortalClient(PropFactory.HostOf("acme"));

        var (orderId, token, checkoutUrl) = await BuyAsync(buyer, "buyer@test.example");
        await SetPriceAsync(admin, 199m);
        var seenByFirm = await firmApi.GetFromJsonAsync<JsonElement>(FirmUrl($"orders/{orderId}"), TestContext.Current.CancellationToken);
        using var paid = await firmApi.PostAsJsonAsync(FirmUrl($"orders/{orderId}/mark-paid"), new { reference = "pay_123" }, TestContext.Current.CancellationToken);
        using var again = await firmApi.PostAsJsonAsync(FirmUrl($"orders/{orderId}/mark-paid"), new { reference = "pay_456" }, TestContext.Current.CancellationToken);
        using var refunded = await firmApi.PostAsJsonAsync(FirmUrl($"orders/{orderId}/mark-refunded"), new { reference = "re_1" }, TestContext.Current.CancellationToken);
        var paidOrders = await firmApi.GetFromJsonAsync<JsonElement>(FirmUrl("orders?status=Paid"), TestContext.Current.CancellationToken);
        var buyerView = await GetAsync(buyer, $"orders/{orderId}?token={token}");

        var returnUrl = Uri.EscapeDataString($"http://acme.localhost:3002/orders/{orderId}?token={token}");
        Assert.Equal($"https://pay.acme.test/checkout?lang=en&order={orderId}&return={returnUrl}", checkoutUrl);
        Assert.Equal(("Pending", "buyer@test.example", 149m), (seenByFirm.GetProperty("order").GetProperty("status").GetString(), seenByFirm.GetProperty("order").GetProperty("email").GetString(), seenByFirm.GetProperty("order").GetProperty("amount").GetDecimal()));
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK), (paid.StatusCode, again.StatusCode, refunded.StatusCode));
        var order = Assert.Single(paidOrders.EnumerateArray());
        Assert.Equal(("pay_123", 149m), (order.GetProperty("paymentReference").GetString(), order.GetProperty("amount").GetDecimal()));
        Assert.Equal(JsonValueKind.String, order.GetProperty("refundedAt").ValueKind);
        Assert.Equal("Paid", buyerView.GetProperty("status").GetString());
    }

    [Fact]
    public async Task OnlyOrdersFromTheFirmsOwnCheckoutAreMarkedByHand()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var buyer = factory.CreatePortalClient();
        using var admin = await factory.LogInAsAdminAsync();
        var (orderId, token, _) = await BuyAsync(buyer, "buyer@test.example");

        using var firmApi = factory.CreateFirmClient();
        using var markedPaid = await firmApi.PostAsJsonAsync(FirmUrl($"orders/{orderId}/mark-paid"), new { reference = "x" }, TestContext.Current.CancellationToken);
        (await buyer.PostAsJsonAsync(Url($"orders/{orderId}/test-payment"), new { token }, TestContext.Current.CancellationToken)).Dispose();
        using var refundedByAdmin = await admin.PostAsJsonAsync(Url($"admin/orders/{orderId}/mark-refunded"), new { reference = "x" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, markedPaid.StatusCode);
        Assert.Equal(HttpStatusCode.OK, refundedByAdmin.StatusCode);
    }

    [Theory]
    [InlineData(99, "XYZ")]
    [InlineData(0.5, "USD")]
    [InlineData(99.999, "USD")]
    [InlineData(100001, "USD")]
    public async Task PricesAreInAKnownCurrencyAndWholeCents(double amount, string currency)
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var firmApi = factory.CreateFirmClient();

        using var refused = await firmApi.PutAsJsonAsync(
            FirmUrl($"challenges/{Challenge}/price"), new { amount = (decimal)amount, currency, forSale = true }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
    }

    [Fact]
    public async Task TheFirmApiSetsPricesForItsOwnChallenges()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var firmApi = factory.CreateFirmClient();

        using var saved = await firmApi.PutAsJsonAsync(FirmUrl($"challenges/{Challenge}/price"), new { amount = 89.5m, currency = "EUR", forSale = false }, TestContext.Current.CancellationToken);
        using var unknown = await firmApi.PutAsJsonAsync(FirmUrl("challenges/no-such-challenge/price"), new { amount = 89.5m, currency = "EUR", forSale = true }, TestContext.Current.CancellationToken);
        var prices = await firmApi.GetFromJsonAsync<JsonElement>(FirmUrl("prices"), TestContext.Current.CancellationToken);
        using var visitor = factory.CreatePortalClient();
        var shop = await GetAsync(visitor, "shop");

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        var price = prices.EnumerateArray().Single(p => p.GetProperty("challengeId").GetString() == Challenge);
        Assert.Equal((89.5m, "EUR", false), (price.GetProperty("amount").GetDecimal(), price.GetProperty("currency").GetString(), price.GetProperty("forSale").GetBoolean()));
        Assert.Equal(["quick-test-100k"], shop.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("challenge").GetProperty("id").GetString()));
    }

    [Fact]
    public async Task AFullSandboxTakesNoOrders()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), new Dictionary<string, string> { ["Sandbox:MaxOpenAccounts"] = "1" });
        using var admin = await OpenTestShopAsync(factory, "acme");
        (await admin.PostAsJsonAsync(Url("admin/accounts"), new { email = "first@test.example", challengeId = Challenge }, TestContext.Current.CancellationToken)).Dispose();
        using var buyer = factory.CreatePortalClient(PropFactory.HostOf("acme"));

        using var refused = await buyer.PostAsJsonAsync(
            Url("orders"), new { challengeId = Challenge, email = "second@test.example", acceptTerms = false }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    }

    [Fact]
    public async Task TheFirmsTermsMustBeAccepted()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await OpenTestShopAsync(factory, "acme");
        await SetPaymentsAsync(admin, new { provider = "Test", termsUrl = "https://acme.test/terms" });
        using var buyer = factory.CreatePortalClient(PropFactory.HostOf("acme"));

        using var refused = await buyer.PostAsJsonAsync(
            Url("orders"), new { challengeId = Challenge, email = "buyer@test.example", acceptTerms = false }, TestContext.Current.CancellationToken);
        var shop = await GetAsync(buyer, "shop");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal("https://acme.test/terms", shop.GetProperty("termsUrl").GetString());
    }

    [Fact]
    public async Task TheBuyerAsksForTheInvitationAgainWhenTheEmailFailed()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await OpenTestShopAsync(factory, "acme");
        using var buyer = factory.CreatePortalClient(PropFactory.HostOf("acme"));
        var (orderId, token, _) = await BuyAsync(buyer, "buyer@test.example");
        factory.Emails.FailNext(1);

        (await buyer.PostAsJsonAsync(Url($"orders/{orderId}/test-payment"), new { token }, TestContext.Current.CancellationToken)).Dispose();
        var notSent = await GetAsync(buyer, $"orders/{orderId}?token={token}");
        using var resent = await buyer.PostAsJsonAsync(Url($"orders/{orderId}/invite"), new { token }, TestContext.Current.CancellationToken);
        using var tooSoon = await buyer.PostAsJsonAsync(Url($"orders/{orderId}/invite"), new { token }, TestContext.Current.CancellationToken);
        using var wrongToken = await buyer.PostAsJsonAsync(Url($"orders/{orderId}/invite"), new { token = "not-the-token" }, TestContext.Current.CancellationToken);

        Assert.Equal(JsonValueKind.Null, notSent.GetProperty("inviteSentAt").ValueKind);
        Assert.Equal(HttpStatusCode.Accepted, resent.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, tooSoon.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, wrongToken.StatusCode);
        Assert.Single(factory.Emails.Sent);
    }

    [Fact]
    public async Task AnExpiredTestOrderCannotBePaid()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var buyer = factory.CreatePortalClient();
        var (orderId, token, _) = await BuyAsync(buyer, "buyer@test.example");

        await factory.AdvanceAsync(TimeSpan.FromHours(2));
        using var refused = await buyer.PostAsJsonAsync(Url($"orders/{orderId}/test-payment"), new { token }, TestContext.Current.CancellationToken);
        var order = await GetAsync(buyer, $"orders/{orderId}?token={token}");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("Expired", order.GetProperty("status").GetString());
    }

    [Fact]
    public async Task TestPaymentsAreOnlyForTheSandboxOutsideDevelopment()
    {
        await using var factory = PropFactory.Create(
            await postgres.CreateDatabaseAsync(), new Dictionary<string, string> { ["Payments:TestPaymentsForLiveFirms"] = "false" });
        using var admin = await factory.LogInAsAdminAsync();

        using var visitor = factory.CreatePortalClient();
        var shop = await GetAsync(visitor, "shop");
        using var refused = await admin.PutAsJsonAsync(Url("admin/firm/payments"), new { provider = "Test" }, TestContext.Current.CancellationToken);

        Assert.False(shop.GetProperty("open").GetBoolean());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
    }

    [Fact]
    public async Task OrdersAreTheFirmsOwn()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), PropFactory.WithOtherFirm());
        using var buyer = factory.CreatePortalClient();
        var (orderId, token, _) = await BuyAsync(buyer, "buyer@test.example");
        using var otherPortal = factory.CreatePortalClient(PropFactory.OtherFirmHost);

        using var otherFirm = factory.CreateFirmClient(PropFactory.OtherFirmKey);
        using var viaOtherApi = await otherFirm.GetAsync(FirmUrl($"orders/{orderId}"), TestContext.Current.CancellationToken);
        using var viaOtherPortal = await otherPortal.GetAsync(Url($"orders/{orderId}?token={token}"), TestContext.Current.CancellationToken);
        using var payViaOtherPortal = await otherPortal.PostAsJsonAsync(Url($"orders/{orderId}/test-payment"), new { token }, TestContext.Current.CancellationToken);
        var otherOrders = await otherFirm.GetFromJsonAsync<JsonElement>(FirmUrl("orders"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, viaOtherApi.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, viaOtherPortal.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, payViaOtherPortal.StatusCode);
        Assert.Empty(otherOrders.EnumerateArray());
    }

    private static Uri Url(string path) => new($"/api/portal/{path}", UriKind.Relative);

    private static Uri FirmUrl(string path) => new($"/api/firm/v1/{path}", UriKind.Relative);

    private static async Task<JsonElement> GetAsync(HttpClient client, string path) =>
        await client.GetFromJsonAsync<JsonElement>(Url(path), TestContext.Current.CancellationToken);

    /// <summary>Buys the challenge on the portal. Returns the order, the token from the buyer's link and where the buyer pays.</summary>
    private static async Task<(Guid OrderId, string Token, string CheckoutUrl)> BuyAsync(HttpClient buyer, string email, string challengeId = Challenge)
    {
        using var response = await buyer.PostAsJsonAsync(Url("orders"), new { challengeId, email, acceptTerms = true }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var checkoutUrl = created.GetProperty("checkoutUrl").GetString()!;
        var orderId = created.GetProperty("orderId").GetGuid();

        // The token of the buyer's link: in the test checkout's address, or in the return address to the firm's own
        // checkout. Stripe has it in the success address it was given.
        var token = checkoutUrl.Contains("return=", StringComparison.Ordinal)
            ? TokenIn(Uri.UnescapeDataString(checkoutUrl.Split("return=")[1]))
            : checkoutUrl.Contains("token=", StringComparison.Ordinal) ? TokenIn(checkoutUrl) : "";
        return (orderId, token, checkoutUrl);
    }

    private static string TokenIn(string url) => url.Split("token=")[1].Split('&')[0];

    /// <summary>A firm that signed up, with its server, the two-step challenge for 99 USD and test payments.</summary>
    private static async Task<HttpClient> OpenTestShopAsync(PropFactory factory, string firmId)
    {
        var admin = await factory.SignUpAsync(firmId);
        await PropFactory.WaitUntilProvisionedAsync(admin);
        await SetPriceAsync(admin, 99m);
        await SetPaymentsAsync(admin, new { provider = "Test" });
        return admin;
    }

    /// <summary>A firm that signed up, with the two-step challenge for 99 USD and its own Stripe test keys.</summary>
    private static async Task<HttpClient> OpenStripeShopAsync(PropFactory factory, string firmId)
    {
        var admin = await factory.SignUpAsync(firmId);
        await PropFactory.WaitUntilProvisionedAsync(admin);
        await SetPriceAsync(admin, 99m);
        await SetPaymentsAsync(admin, new { provider = "Stripe", stripeSecretKey = FakeStripe.SecretKey, stripeWebhookSecret = FakeStripe.WebhookSecret });
        return admin;
    }

    private static async Task SetPriceAsync(HttpClient admin, decimal amount)
    {
        using var response = await admin.PutAsJsonAsync(Url($"admin/challenges/{Challenge}/price"), new { amount, currency = "USD", forSale = true }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<JsonElement> SetPaymentsAsync(HttpClient admin, object payments)
    {
        using var response = await admin.PutAsJsonAsync(Url("admin/firm/payments"), payments, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private static async Task<string> NewApiKeyAsync(HttpClient admin)
    {
        using var response = await admin.PostAsync(Url("admin/firm/api-key"), null, TestContext.Current.CancellationToken);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("apiKey").GetString()!;
    }
}
