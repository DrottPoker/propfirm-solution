using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Npgsql;

using Prop.Api.Identity;
using Prop.Api.Tests.Support;

namespace Prop.Api.Tests;

/// <summary>
/// Our review of a firm before it goes live (ADR 0021): the application and its documents, the deposit that sends
/// it, our staff's decisions in our own admin view, and going live with the deposit taken off the startup fee.
/// </summary>
public sealed class ReviewTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Owner = "owner@firm.test";

    private static readonly Dictionary<string, string> WithDeposit = new() { ["Billing:ReviewDeposit"] = "100" };

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\nA certificate of registration\n%%EOF");

    [Fact]
    public async Task ADraftIsSavedWithOnlyTheFieldsThatAreFilledInChecked()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), WithDeposit);
        using var admin = await SandboxFirmAsync(factory, "acme");
        var empty = await GetAsync(admin, "admin/verification");

        using var saved = await admin.PutAsJsonAsync(Url("admin/verification/application"), new { companyName = "  Acme Ltd  ", country = "se", owners = new[] { new { name = " ", sharePercent = (decimal?)null } } }, TestContext.Current.CancellationToken);
        var draft = await saved.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        using var badWebsite = await admin.PutAsJsonAsync(Url("admin/verification/application"), new { website = "http://acme.test" }, TestContext.Current.CancellationToken);
        using var badShares = await admin.PutAsJsonAsync(
            Url("admin/verification/application"),
            new { owners = new[] { new { name = "Anna", sharePercent = 70m }, new { name = "Bert", sharePercent = 40m } } },
            TestContext.Current.CancellationToken);
        using var incomplete = await PostAsync(admin, "admin/verification/submit", null);

        Assert.Equal(("Draft", true, 100m, false), (
            empty.GetProperty("status").GetString(),
            empty.GetProperty("canEdit").GetBoolean(),
            empty.GetProperty("deposit").GetProperty("amount").GetDecimal(),
            empty.GetProperty("deposit").GetProperty("paid").GetBoolean()));
        Assert.Equal("Fill in the company's legal name.", empty.GetProperty("submitProblem").GetString());
        Assert.Equal(
            ["companyName", "registrationNumber", "country", "address", "contactName", "owners", "termsUrl"],
            empty.GetProperty("problems").EnumerateArray().Select(p => p.GetProperty("field").GetString()));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(("Acme Ltd", "SE", 0), (
            draft.GetProperty("application").GetProperty("companyName").GetString(),
            draft.GetProperty("application").GetProperty("country").GetString(),
            draft.GetProperty("application").GetProperty("owners").GetArrayLength()));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "website"), (badWebsite.StatusCode, await FieldOfAsync(badWebsite)));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "owners"), (badShares.StatusCode, await FieldOfAsync(badShares)));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "registrationNumber"), (incomplete.StatusCode, await FieldOfAsync(incomplete)));
    }

    [Fact]
    public async Task TheFirmIsReviewedWithoutKycButGoesLiveOnlyWithKycThatWorks()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");
        await PropFactory.WaitUntilProvisionedAsync(admin);
        await SaveApplicationAsync(admin);

        // Nothing is chosen for the firm, and choosing nothing is refused. The application is sent and approved without it.
        var settings = await GetAsync(admin, "admin/identity");
        using var nothing = await admin.PutAsJsonAsync(Url("admin/identity"), new { requiredBefore = "FirstPayout", checkAddress = false, checkSanctions = false }, TestContext.Current.CancellationToken);
        var notChosen = await GetAsync(admin, "admin/verification");
        using var sent = await PostAsync(admin, "admin/verification/submit", null);
        using var ops = await factory.LogInAsStaffAsync();
        var firm = await GetAsync(ops, "ops/firms/acme");
        using var approved = await PostAsync(ops, "ops/firms/acme/approve", new { message = (string?)null });
        Assert.Equal(JsonValueKind.Null, settings.GetProperty("mode").ValueKind);
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "mode"), (nothing.StatusCode, await FieldOfAsync(nothing)));
        Assert.Equal(("NotChosen", JsonValueKind.Null), (notChosen.GetProperty("identity").GetString(), notChosen.GetProperty("submitProblem").ValueKind));
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (sent.StatusCode, approved.StatusCode));
        Assert.Equal((JsonValueKind.Null, "NotChosen"), (firm.GetProperty("identity").GetProperty("mode").ValueKind, firm.GetProperty("identity").GetProperty("readiness").GetString()));

        // Going live waits for the choice, and for the firm's own service to work through the whole flow.
        Assert.Equal(IdentityService.ReadinessProblem(IdentityReadiness.NotChosen), await GoLiveProblemAsync(admin));
        await SaveIdentityAsync(admin, "https://kyc.acme.test/start");
        Assert.Equal(IdentityService.ReadinessProblem(IdentityReadiness.NotTested), await GoLiveProblemAsync(admin));
        using (var refused = await PostAsync(admin, "admin/billing/activate", new { slots = 25 }))
        {
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        }

        await factory.ScalarAsync("update firm_identity_settings set external_tested_at = external_since where firm_id = 'acme'");
        Assert.Null(await GoLiveProblemAsync(admin));

        // A new address must work again.
        await SaveIdentityAsync(admin, "https://kyc2.acme.test/start");
        Assert.Equal(IdentityService.ReadinessProblem(IdentityReadiness.NotTested), await GoLiveProblemAsync(admin));
        await factory.ScalarAsync("update firm_identity_settings set external_tested_at = external_since where firm_id = 'acme'");
        using var activate = await PostAsync(admin, "admin/billing/activate", new { slots = 25 });
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
    }

    private static async Task<string?> GoLiveProblemAsync(HttpClient admin) =>
        (await GetAsync(admin, "admin/billing")).GetProperty("goLiveProblem").GetString();

    private static async Task SaveIdentityAsync(HttpClient admin, string externalUrl)
    {
        using var saved = await admin.PutAsJsonAsync(
            Url("admin/identity"),
            new { mode = "External", requiredBefore = "FirstPayout", checkAddress = false, checkSanctions = false, externalUrl },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    // One address for the firm's terms: saved with the application it is the shop's, and saved under Checkout the application's.
    [Fact]
    public async Task TheTermsAreOneAddressForTheShopAndTheApplication()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await SandboxFirmAsync(factory, "acme");

        (await admin.PutAsJsonAsync(Url("admin/verification/application"), new { termsUrl = "https://acme.test/terms" }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        var shop = (await GetAsync(admin, "admin/firm")).GetProperty("payments").GetProperty("termsUrl").GetString();
        (await admin.PutAsJsonAsync(Url("admin/firm/payments"), new { provider = (string?)null, termsUrl = "https://acme.test/terms-v2" }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        var application = (await GetAsync(admin, "admin/verification")).GetProperty("application").GetProperty("termsUrl").GetString();

        Assert.Equal(("https://acme.test/terms", "https://acme.test/terms-v2"), (shop, application));
    }

    [Fact]
    public async Task AFirmInTheEuGivesItsVatNumberOrSaysItHasNone()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await SandboxFirmAsync(factory, "acme");

        using var outsideEu = await admin.PutAsJsonAsync(Url("admin/verification/application"), PropFactory.Application(country: "AE", vatNumber: null), TestContext.Current.CancellationToken);
        var withoutEuNumber = await outsideEu.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        using var german = await admin.PutAsJsonAsync(Url("admin/verification/application"), PropFactory.Application(country: "DE", vatNumber: null), TestContext.Current.CancellationToken);
        var withoutNumber = await GetAsync(admin, "admin/verification");
        using var refused = await PostAsync(admin, "admin/verification/submit", null);
        using var wrongCountry = await admin.PutAsJsonAsync(
            Url("admin/verification/application"),
            PropFactory.Application(country: "DE", vatNumber: "SE559000123401"),
            TestContext.Current.CancellationToken);
        using var none = await admin.PutAsJsonAsync(
            Url("admin/verification/application"),
            PropFactory.Application(country: "DE", vatNumber: "DE123456789", noVatNumber: true),
            TestContext.Current.CancellationToken);
        var withNone = await none.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        using var saved = await admin.PutAsJsonAsync(
            Url("admin/verification/application"),
            PropFactory.Application(country: "DE", vatNumber: "de 123.456-789"),
            TestContext.Current.CancellationToken);
        var draft = await saved.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        using var sent = await PostAsync(admin, "admin/verification/submit", null);

        Assert.Equal(JsonValueKind.Null, withoutEuNumber.GetProperty("submitProblem").ValueKind);
        Assert.Equal(HttpStatusCode.OK, german.StatusCode);
        Assert.Equal("Fill in the company's VAT number, or tick that it has none.", withoutNumber.GetProperty("submitProblem").GetString());
        var eu = withoutNumber.GetProperty("euCountries").EnumerateArray().Select(c => c.GetString()).ToList();
        Assert.Equal((true, true, false), (eu.Contains("DE"), eu.Contains("SE"), eu.Contains("AE")));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "vatNumber"), (refused.StatusCode, await FieldOfAsync(refused)));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "vatNumber"), (wrongCountry.StatusCode, await FieldOfAsync(wrongCountry)));
        Assert.Equal((JsonValueKind.Null, true, JsonValueKind.Null), (
            withNone.GetProperty("application").GetProperty("vatNumber").ValueKind,
            withNone.GetProperty("application").GetProperty("noVatNumber").GetBoolean(),
            withNone.GetProperty("submitProblem").ValueKind));
        Assert.Equal(("DE123456789", JsonValueKind.Null), (
            draft.GetProperty("application").GetProperty("vatNumber").GetString(),
            draft.GetProperty("application").GetProperty("noVatNumber").ValueKind));
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        Assert.Equal("Submitted", (await GetAsync(admin, "admin/verification")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task DocumentsAreKnownByTheirContentKeptEncryptedAndOnlyTheFirmsOwn()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await SandboxFirmAsync(factory, "acme");
        using var other = await SandboxFirmAsync(factory, "globex", "owner@globex.test");

        using var added = await UploadAsync(admin, "..\\folder/certificate.pdf", Pdf);
        var document = await added.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var id = document.GetProperty("id").GetGuid();
        using var renamedText = await UploadAsync(admin, "evil.pdf", Encoding.ASCII.GetBytes("<script>alert(1)</script>"));
        using var tooLarge = await UploadAsync(admin, "large.pdf", [.. Pdf, .. new byte[10 * 1024 * 1024]]);
        using var downloaded = await admin.GetAsync(Url($"admin/verification/documents/{id}"), TestContext.Current.CancellationToken);
        using var othersView = await other.GetAsync(Url($"admin/verification/documents/{id}"), TestContext.Current.CancellationToken);
        var stored = StoredContent(factory, id);
        using var removed = await admin.DeleteAsync(Url($"admin/verification/documents/{id}"), TestContext.Current.CancellationToken);
        using var gone = await admin.GetAsync(Url($"admin/verification/documents/{id}"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.Equal(("certificate.pdf", "application/pdf", Pdf.Length), (
            document.GetProperty("fileName").GetString(),
            document.GetProperty("contentType").GetString(),
            document.GetProperty("size").GetInt32()));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, renamedText.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);
        Assert.Equal(Pdf, await downloaded.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(("application/pdf", "attachment", "nosniff"), (
            downloaded.Content.Headers.ContentType?.MediaType,
            downloaded.Content.Headers.ContentDisposition?.DispositionType,
            downloaded.Headers.GetValues("X-Content-Type-Options").Single()));
        Assert.Equal(HttpStatusCode.NotFound, othersView.StatusCode);
        Assert.DoesNotContain("certificate of registration", Encoding.ASCII.GetString(stored), StringComparison.Ordinal);
        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NotFound), (removed.StatusCode, gone.StatusCode));
    }

    [Fact]
    public async Task SendingTheApplicationTakesTheDepositAndTellsOurStaff()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), WithDeposit);
        using var admin = await SandboxFirmAsync(factory, "acme");
        await SaveApplicationAsync(admin);

        using var submitted = await PostAsync(admin, "admin/verification/submit", null);
        var checkoutId = CheckoutIdOf(await submitted.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken));
        var waiting = await GetAsync(admin, "admin/verification");
        var page = await GetAsync(admin, $"admin/billing/checkouts/{checkoutId}");
        using var paid = await CompleteAsync(admin, checkoutId);
        var sent = await GetAsync(admin, "admin/verification");
        var billing = await GetAsync(admin, "admin/billing");
        using var changed = await admin.PutAsJsonAsync(Url("admin/verification/application"), PropFactory.Application("Another name"), TestContext.Current.CancellationToken);
        using var activate = await PostAsync(admin, "admin/billing/activate", new { slots = 30 });

        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        Assert.Equal("Draft", waiting.GetProperty("status").GetString());
        Assert.Equal(("Payment", 100m, "admin/go-live"), (page.GetProperty("purpose").GetString(), page.GetProperty("amount").GetDecimal(), page.GetProperty("returnPath").GetString()));
        Assert.Equal(HttpStatusCode.NoContent, paid.StatusCode);
        Assert.Equal(("Submitted", true, false), (sent.GetProperty("status").GetString(), sent.GetProperty("deposit").GetProperty("paid").GetBoolean(), sent.GetProperty("canEdit").GetBoolean()));
        var deposit = Assert.Single(billing.GetProperty("charges").EnumerateArray());
        Assert.Equal(("Deposit", "Paid", 100m), (deposit.GetProperty("kind").GetString(), deposit.GetProperty("status").GetString(), deposit.GetProperty("amount").GetDecimal()));
        Assert.Equal(("Submitted", 100m), (billing.GetProperty("review").GetString(), billing.GetProperty("depositPaid").GetDecimal()));
        Assert.StartsWith("We are reviewing your application", billing.GetProperty("goLiveProblem").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, activate.StatusCode);
        var email = Assert.Single(factory.Emails.Sent, e => e.To == PropFactory.StaffEmail);
        Assert.Equal("Firm acme is waiting for review", email.Subject);
        Assert.Contains("http://ops.localhost:3002/ops/firms/acme", email.Body, StringComparison.Ordinal);

        // The firm hears that we have it, with the deposit's receipt. A German firm with a VAT number pays no VAT to us.
        var received = await factory.Emails.WaitForAsync(Owner, "We have received the application for Firm acme");
        Assert.Contains("Receipt for invoice ACME-0001", received.Body, StringComparison.Ordinal);
        Assert.Contains("Reverse charge", received.Body, StringComparison.Ordinal);
        Assert.Contains("Total paid: 100.00 USD", received.Body, StringComparison.Ordinal);
        Assert.Contains("http://acme.localhost:3002/admin/go-live", received.Body, StringComparison.Ordinal);
        Assert.Equal(("ACME-0001", "ReverseCharge", 0m), (deposit.GetProperty("invoice").GetString(), deposit.GetProperty("vatTreatment").GetString(), deposit.GetProperty("vatAmount").GetDecimal()));
    }

    [Fact]
    public async Task ADepositThatIsNotPaidInTimeIsVoidAndTheFirmSendsAgain()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), WithDeposit);
        using var admin = await SandboxFirmAsync(factory, "acme");
        await SaveApplicationAsync(admin);
        using var first = await PostAsync(admin, "admin/verification/submit", null);
        var firstCheckout = CheckoutIdOf(await first.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken));

        await factory.AdvanceUntilAsync(
            TimeSpan.FromHours(1),
            async () => (await GetAsync(admin, $"admin/billing/checkouts/{firstCheckout}")).GetProperty("status").GetString() == "Expired",
            "the checkout to expire");
        using var second = await PostAsync(admin, "admin/verification/submit", null);
        using var paid = await CompleteAsync(admin, CheckoutIdOf(await second.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)));
        var billing = await GetAsync(admin, "admin/billing");

        Assert.Equal(HttpStatusCode.NoContent, paid.StatusCode);
        Assert.Equal(["Paid", "Void"], billing.GetProperty("charges").EnumerateArray().Select(c => c.GetProperty("status").GetString()));
        Assert.Equal("Submitted", (await GetAsync(admin, "admin/verification")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task WithoutADepositTheApplicationIsSentAtOnce()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await SandboxFirmAsync(factory, "acme");
        await SaveApplicationAsync(admin);

        using var submitted = await PostAsync(admin, "admin/verification/submit", null);
        var verification = await GetAsync(admin, "admin/verification");

        Assert.Equal(JsonValueKind.Null, (await submitted.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("checkoutUrl").ValueKind);
        Assert.Equal(("Submitted", false, 0m), (verification.GetProperty("status").GetString(), verification.GetProperty("deposit").GetProperty("paid").GetBoolean(), verification.GetProperty("deposit").GetProperty("amount").GetDecimal()));
        Assert.Contains(factory.Emails.Sent, e => e.To == PropFactory.StaffEmail && e.Subject == "Firm acme is waiting for review");
        var received = await factory.Emails.WaitForAsync(Owner, "We have received the application for Firm acme");
        Assert.DoesNotContain("Receipt", received.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnApprovedFirmGoesLiveWithTheDepositTakenOffTheStartupFee()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), WithDeposit);
        using var admin = await SubmittedFirmAsync(factory, "acme");
        using var ops = await factory.LogInAsStaffAsync();

        using var approved = await PostAsync(ops, "ops/firms/acme/approve", new { message = "Welcome aboard." });
        var firm = await approved.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var quote = await GetAsync(admin, "admin/billing/quote?slots=30");
        using var activated = await PostAsync(admin, "admin/billing/activate", new { slots = 30 });
        using var paid = await CompleteAsync(admin, CheckoutIdOf(await activated.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)));
        var billing = await GetAsync(admin, "admin/billing");
        using var changed = await admin.PutAsJsonAsync(Url("admin/verification/application"), PropFactory.Application("Another name"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal(("Approved", "ops@test.com", "Welcome aboard."), (firm.GetProperty("review").GetString(), firm.GetProperty("decidedBy").GetString(), firm.GetProperty("message").GetString()));
        var email = Assert.Single(factory.Emails.Sent, e => e.To == Owner && e.Subject == "Firm acme is approved");
        Assert.Contains("Welcome aboard.", email.Body, StringComparison.Ordinal);
        Assert.Contains("http://acme.localhost:3002/admin/go-live", email.Body, StringComparison.Ordinal);
        Assert.Equal(1057.25m, quote.GetProperty("amount").GetDecimal());
        Assert.Equal(("Startup fee, less the deposit of 100.00 USD", 600m), (quote.GetProperty("lines")[0].GetProperty("description").GetString(), quote.GetProperty("lines")[0].GetProperty("amount").GetDecimal()));
        Assert.Equal(HttpStatusCode.NoContent, paid.StatusCode);
        Assert.Equal("Live", billing.GetProperty("status").GetString());
        Assert.Equal([("Activation", 1057.25m), ("Deposit", 100m)], billing.GetProperty("charges").EnumerateArray().Select(c => (c.GetProperty("kind").GetString(), c.GetProperty("amount").GetDecimal())));
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
    }

    [Fact]
    public async Task AFirmAskedForChangesSendsAgainWithoutANewDeposit()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), WithDeposit);
        using var admin = await SubmittedFirmAsync(factory, "acme");
        using var ops = await factory.LogInAsStaffAsync();

        using var withoutMessage = await PostAsync(ops, "ops/firms/acme/request-changes", new { message = " " });
        using var requested = await PostAsync(ops, "ops/firms/acme/request-changes", new { message = "Your terms do not say how payouts work." });
        var verification = await GetAsync(admin, "admin/verification");
        using var approveNow = await PostAsync(ops, "ops/firms/acme/approve", new { message = (string?)null });
        using var saved = await admin.PutAsJsonAsync(Url("admin/verification/application"), PropFactory.Application("Acme Trading AB"), TestContext.Current.CancellationToken);
        using var resubmitted = await PostAsync(admin, "admin/verification/submit", null);
        var firm = await GetAsync(ops, "ops/firms/acme");
        var billing = await GetAsync(admin, "admin/billing");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, withoutMessage.StatusCode);
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        Assert.Equal(("ChangesRequested", "Your terms do not say how payouts work.", true), (
            verification.GetProperty("status").GetString(),
            verification.GetProperty("message").GetString(),
            verification.GetProperty("canEdit").GetBoolean()));
        Assert.Contains(factory.Emails.Sent, e => e.To == Owner && e.Subject == "Changes needed for Firm acme" && e.Body.Contains("how payouts work", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Conflict, approveNow.StatusCode);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await resubmitted.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("checkoutUrl").ValueKind);
        Assert.Equal(("Submitted", "Acme Trading AB"), (firm.GetProperty("review").GetString(), firm.GetProperty("application").GetProperty("companyName").GetString()));
        Assert.Equal(["submitted", "changes_requested", "submitted"], firm.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("type").GetString()));
        Assert.Single(billing.GetProperty("charges").EnumerateArray());
    }

    [Fact]
    public async Task ARejectedFirmCannotSendAgainOrGoLive()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), WithDeposit);
        using var admin = await SubmittedFirmAsync(factory, "acme");
        using var ops = await factory.LogInAsStaffAsync();

        using var rejected = await PostAsync(ops, "ops/firms/acme/reject", new { message = "We could not find the company in the register." });
        using var again = await PostAsync(ops, "ops/firms/acme/reject", new { message = "Again." });
        var verification = await GetAsync(admin, "admin/verification");
        using var resubmitted = await PostAsync(admin, "admin/verification/submit", null);
        using var activate = await PostAsync(admin, "admin/billing/activate", new { slots = 30 });

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.Conflict), (rejected.StatusCode, again.StatusCode));
        Assert.Equal(("Rejected", false), (verification.GetProperty("status").GetString(), verification.GetProperty("canEdit").GetBoolean()));
        Assert.Contains(factory.Emails.Sent, e => e.To == Owner && e.Subject == "Firm acme was not approved" && e.Body.Contains("the register", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Conflict, resubmitted.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, activate.StatusCode);
        Assert.Equal("Your application was not approved, so the firm cannot go live.", await TitleOfAsync(activate));
    }

    [Fact]
    public async Task OurAdminViewIsOnlyOnItsOwnAddressAndOnlyForOurStaff()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await SubmittedFirmAsync(factory, "acme");
        using var platform = factory.CreatePlatformClient();
        using var anonymous = factory.CreatePortalClient(PropFactory.OpsHost);
        using var firmAdmin = await factory.LogInAsAdminAsync();
        using var ops = await factory.LogInAsStaffAsync();

        using var onPlatform = await platform.GetAsync(Url("ops"), TestContext.Current.CancellationToken);
        using var onPortal = await firmAdmin.GetAsync(Url("ops/firms"), TestContext.Current.CancellationToken);
        var site = await GetAsync(anonymous, "ops");
        using var notLoggedIn = await anonymous.GetAsync(Url("ops/firms"), TestContext.Current.CancellationToken);
        using var wrongPassword = await PostAsync(anonymous, "ops/login", new { email = PropFactory.StaffEmail, password = "wrong" });
        using var firmAdminAsStaff = await PostAsync(anonymous, "ops/login", new { email = PropFactory.AdminEmail, password = PropFactory.AdminPassword });
        var toReview = await GetAsync(ops, "ops/firms?group=ToReview");
        var all = await GetAsync(ops, "ops/firms");
        var firm = await GetAsync(ops, "ops/firms/acme");
        var me = await GetAsync(ops, "ops/me");

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (onPlatform.StatusCode, onPortal.StatusCode));
        Assert.Equal(("Kronant Prop", "http://app.localhost:3002/signup"), (site.GetProperty("name").GetString(), site.GetProperty("signupUrl").GetString()));
        Assert.Equal(HttpStatusCode.Unauthorized, notLoggedIn.StatusCode);
        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (wrongPassword.StatusCode, firmAdminAsStaff.StatusCode));
        Assert.Equal(["acme"], toReview.GetProperty("firms").EnumerateArray().Select(f => f.GetProperty("id").GetString()));
        Assert.Equal(["acme", "demo-firm"], all.GetProperty("firms").EnumerateArray().Select(f => f.GetProperty("id").GetString()).Order());
        var demo = all.GetProperty("firms").EnumerateArray().Single(f => f.GetProperty("id").GetString() == "demo-firm");
        Assert.Equal((true, "Live"), (demo.GetProperty("configured").GetBoolean(), demo.GetProperty("stage").GetString()));
        Assert.Equal(("Firm acme", "Sandbox", "Submitted", "Acme Trading Ltd"), (
            firm.GetProperty("name").GetString(),
            firm.GetProperty("status").GetString(),
            firm.GetProperty("review").GetString(),
            firm.GetProperty("application").GetProperty("companyName").GetString()));
        Assert.Equal([Owner], firm.GetProperty("admins").EnumerateArray().Select(a => a.GetProperty("email").GetString()));
        Assert.Equal(PropFactory.StaffEmail, me.GetProperty("email").GetString());
    }

    /// <summary>A firm that signed up and has its server. Returns its administrator's browser.</summary>
    /// <summary>A firm in the sandbox that chose its KYC, so it can go live once we approve it.</summary>
    private static async Task<HttpClient> SandboxFirmAsync(PropFactory factory, string firmId, string email = Owner)
    {
        var admin = await factory.SignUpAsync(firmId, email);
        await PropFactory.WaitUntilProvisionedAsync(admin);
        await factory.ChooseIdentityChecksAsync(admin, firmId);
        return admin;
    }

    /// <summary>A firm that sent a complete application and paid the deposit, if there is one.</summary>
    private static async Task<HttpClient> SubmittedFirmAsync(PropFactory factory, string firmId)
    {
        var admin = await SandboxFirmAsync(factory, firmId);
        await SaveApplicationAsync(admin);
        using var submitted = await PostAsync(admin, "admin/verification/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        if ((await submitted.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("checkoutUrl").GetString() is { } checkoutUrl)
        {
            using var paid = await CompleteAsync(admin, new Uri(checkoutUrl).Segments[^1]);
            Assert.Equal(HttpStatusCode.NoContent, paid.StatusCode);
        }

        return admin;
    }

    private static async Task SaveApplicationAsync(HttpClient admin)
    {
        using var saved = await admin.PutAsJsonAsync(Url("admin/verification/application"), PropFactory.Application(), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient admin, string fileName, byte[] content)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", fileName);
        return admin.PostAsync(Url("admin/verification/documents"), form, TestContext.Current.CancellationToken);
    }

    // The document as it is stored in the database.
    private static byte[] StoredContent(PropFactory factory, Guid documentId)
    {
        using var connection = new NpgsqlConnection(factory.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand("select content from firm_documents where id = $1", connection);
        command.Parameters.AddWithValue(documentId);
        return (byte[])command.ExecuteScalar()!;
    }

    private static string CheckoutIdOf(JsonElement response)
    {
        var url = new Uri(response.GetProperty("checkoutUrl").GetString()!);
        Assert.StartsWith("/admin/billing/checkout/test_", url.AbsolutePath, StringComparison.Ordinal);
        return url.Segments[^1];
    }

    private static Task<HttpResponseMessage> CompleteAsync(HttpClient admin, string checkoutId) =>
        PostAsync(admin, $"admin/billing/checkouts/{checkoutId}/complete", new { declines = false });

    private static async Task<string?> FieldOfAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("field").GetString();

    private static async Task<string?> TitleOfAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("title").GetString();

    private static async Task<JsonElement> GetAsync(HttpClient client, string path) =>
        await client.GetFromJsonAsync<JsonElement>(Url(path), TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object? body) =>
        body is null ? client.PostAsync(Url(path), null, TestContext.Current.CancellationToken) : client.PostAsJsonAsync(Url(path), body, TestContext.Current.CancellationToken);

    private static Uri Url(string path) => new($"/api/portal/{path}", UriKind.Relative);
}
