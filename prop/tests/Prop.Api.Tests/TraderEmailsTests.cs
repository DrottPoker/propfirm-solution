using Prop.Api.Email;
using Prop.Api.Firms;
using Prop.Api.Payments;

namespace Prop.Api.Tests;

/// <summary>The emails to a firm's traders in the firm's look (ADR 0033): its name, logo and color, and replies to its support.</summary>
public sealed class TraderEmailsTests
{
    private static readonly Firm Firm = new(
        "acme",
        "Acme <Capital> & Co",
        FirmStatus.Live,
        null,
        null,
        null,
        new FirmPortal(new Uri("https://acme.example/"), ["acme.example"], new Branding("Acme <Capital> & Co", "/api/portal/logo/abc", new Dictionary<string, string> { ["accent"] = "#facc15" })),
        new FirmPayments(null, null, null, null),
        null,
        SupportEmail: "support@acme.example");

    [Fact]
    public void AnEmailHasTheFirmsLogoColorAndSupportAddress()
    {
        var email = TraderEmails.ChallengeStarted(Firm, "Two-step 50K", "anna@test.example", new Uri("https://acme.example/accounts/1"));

        Assert.Equal(("Acme <Capital> & Co", "support@acme.example"), (email.FromName, email.ReplyTo));
        Assert.NotNull(email.Html);
        Assert.Contains("""src="https://acme.example/api/portal/logo/abc" alt="Acme &lt;Capital&gt; &amp; Co""", email.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<Capital>", email.Html, StringComparison.Ordinal);

        // A light accent gets dark text on the button, as the portal does.
        Assert.Contains("background:#facc15;color:#000000", email.Html, StringComparison.Ordinal);
        Assert.Contains("https://acme.example/accounts/1", email.Body, StringComparison.Ordinal);
        Assert.Contains("Questions? Reply to this email.", email.Body, StringComparison.Ordinal);
    }

    // A firm that sells in its portal points to its shop, and one that does not asks the trader to ask it.
    [Fact]
    public void AnEndedTestAccountPointsToTheShopOnlyWhenTheFirmSellsThere()
    {
        var selling = Firm with { Payments = new FirmPayments(PaymentProvider.Test, null, null, null) };

        var withShop = TraderEmails.SandboxAccountEnded(selling, "anna@test.example", "Two-step 50K", 1001);
        var withoutShop = TraderEmails.SandboxAccountEnded(Firm, "anna@test.example", "Two-step 50K", 1001);

        Assert.Contains("https://acme.example/buy", withShop.Body, StringComparison.Ordinal);
        Assert.Contains("(account #1001) was a test account", withShop.Body, StringComparison.Ordinal);
        Assert.Contains("if you want a new challenge", withoutShop.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("/buy", withoutShop.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void AFirmWithoutLogoOrSupportHasItsNameAndNoReplyAddress()
    {
        var firm = Firm with
        {
            SupportEmail = null,
            Portal = Firm.Portal with { Branding = new Branding("Acme", null, new Dictionary<string, string>()) },
        };

        var email = TraderEmails.ResetPassword(firm, "anna@test.example", new Uri("https://acme.example/reset-password?token=x"), TimeSpan.FromHours(1));

        Assert.Null(email.ReplyTo);
        Assert.Contains("color:#2563eb", email.Html, StringComparison.Ordinal);
        Assert.Contains("background:#2563eb;color:#ffffff", email.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("Reply to this email", email.Body, StringComparison.Ordinal);
        Assert.Contains("within 1 hour", email.Body, StringComparison.Ordinal);
    }
}
