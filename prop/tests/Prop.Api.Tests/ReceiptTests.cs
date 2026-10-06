using System.Text;

using Prop.Api.Email;
using Prop.Api.Firms;
using Prop.Api.Payments;
using Prop.Api.Tests.Support;
using Prop.Rules;

namespace Prop.Api.Tests;

/// <summary>The receipt of a paid order: in the buyer's email, with the rules and how to get started, and as a PDF.</summary>
public sealed class ReceiptTests
{
    private static readonly DateTimeOffset PaidAt = new(2026, 10, 5, 9, 30, 0, TimeSpan.Zero);

    private static readonly Firm Firm = new(
        "aurora",
        "Aurora Funded",
        FirmStatus.Live,
        null,
        null,
        null,
        new FirmPortal(new Uri("https://aurora.example/"), ["aurora.example"], new Branding("Aurora Funded", null, new Dictionary<string, string>())),
        new FirmPayments(PaymentProvider.Stripe, null, null, null),
        null,
        SupportEmail: "support@aurora.example");

    [Fact]
    public void TheBuyersEmailHasTheReceiptTheRulesAndHowToGetStarted()
    {
        var receipt = new OrderReceipt(PaidOrder(PaymentProvider.Stripe, 89.10m, "SAVE10", 99m), ChallengeTemplates.TwoStep("two-step-100k", 100_000m));

        var email = TraderEmails.InviteBuyer(Firm, receipt, new Uri("https://aurora.example/invite?token=abc"), TimeSpan.FromDays(7));

        foreach (var line in new[]
        {
            "1. Choose a password with the link above, or log in if you have chosen one already.",
            "2. Open the trading terminal from your account in the portal.",
            "3. Place your first trade.",
            "Account size: 100,000.00 USD",
            "Phase 1 profit target: 10% (10,000.00 USD)",
            "Phase 2 profit target: 5% (5,000.00 USD)",
            "Daily loss limit: 5% (5,000.00 USD)",
            "Max loss limit: 10% (10,000.00 USD)",
            "Profit split: 80% to you",
            "Order 1002, paid 5 Oct 2026.",
            "Two-step 100K: 99.00 USD\nDiscount, code SAVE10: -9.90 USD\nTotal paid: 89.10 USD\nPaid with: Stripe\nPayment reference: pi_123",
        })
        {
            Assert.Contains(line, email.Body, StringComparison.Ordinal);
        }

        // Orders carry no VAT, so the receipt has no VAT line, and the order is named as the app names it.
        Assert.DoesNotContain("VAT", email.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("#1002", email.Body, StringComparison.Ordinal);
        Assert.Equal("abc", FakeEmailSender.TokenIn(email));
        Assert.Equal("Your Two-step 100K with Aurora Funded is starting", email.Subject);
        Assert.Contains(">Receipt</h2>", email.Html, StringComparison.Ordinal);
        Assert.Contains(">Discount, code SAVE10</td>", email.Html, StringComparison.Ordinal);
        Assert.Contains(">-9.90 USD</td>", email.Html, StringComparison.Ordinal);
        Assert.Contains("font-weight:600\">Total paid</td>", email.Html, StringComparison.Ordinal);
        Assert.Contains(">Place your first trade.</li></ol>", email.Html, StringComparison.Ordinal);
    }

    // A buyer with a password logs in; a challenge funded from the start has no profit target, and its loss limit can trail.
    [Fact]
    public void ABuyerWithAPasswordGetsTheReceiptWithALinkToTheAccount()
    {
        var receipt = new OrderReceipt(PaidOrder(PaymentProvider.Test, 59m), ChallengeTemplates.InstantFunded("instant-10k", 10_000m));

        var email = TraderEmails.ChallengeBought(Firm, receipt, new Uri("https://aurora.example/accounts/1"));

        Assert.Equal("Your Instant funded 10K with Aurora Funded has started", email.Subject);
        Assert.Contains("https://aurora.example/accounts/1", email.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("token=", email.Body, StringComparison.Ordinal);
        Assert.Contains("1. Log in to Aurora Funded's portal with your password.", email.Body, StringComparison.Ordinal);
        Assert.Contains("Max loss limit: 6% (600.00 USD), trailing", email.Body, StringComparison.Ordinal);
        Assert.Contains("Profit split: 70% to you", email.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("profit target", email.Body, StringComparison.Ordinal);
        Assert.Contains("Instant funded 10K: 59.00 USD\nTotal paid: 59.00 USD\nPaid with: Test payment, no money was taken\n", email.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Discount", email.Body, StringComparison.Ordinal);
        Assert.EndsWith(
            "You get this email because you bought a challenge from Aurora Funded.\n\nAurora Funded\nsupport@aurora.example", email.Body, StringComparison.Ordinal);
    }

    // Loss limits that differ between stages are given per stage.
    [Fact]
    public void LossLimitsThatDifferAreGivenPerStage()
    {
        var challenge = ChallengeTemplates.OneStep("one-step-10k", 10_000m);
        challenge = challenge with { Funded = challenge.Funded with { DailyLoss = new DailyLossRule(3, DailyLossReference.Balance) } };

        var email = TraderEmails.ChallengeBought(Firm, new OrderReceipt(PaidOrder(PaymentProvider.Test, 59m), challenge), new Uri("https://aurora.example/accounts/1"));

        Assert.Contains("Daily loss limit: Phase 1 4% (400.00 USD), Funded 3% (300.00 USD)", email.Body, StringComparison.Ordinal);
        Assert.Contains("Max loss limit: 6% (600.00 USD)\n", email.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePdfReceiptHasTheSameLinesInTheFirmsName()
    {
        var receipt = new OrderReceipt(PaidOrder(PaymentProvider.Stripe, 89.10m, "SAVE10", 99m), ChallengeTemplates.TwoStep("two-step-100k", 100_000m));

        var text = Encoding.Latin1.GetString(ReceiptPdf.Render(receipt, Firm, "Kronant Prop"));

        Assert.StartsWith("%PDF-1.4", text, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);
        foreach (var expected in new[]
        {
            "(Receipt)", "(Aurora Funded)", "(support@aurora.example)", "(Order 1002)", "(5 Oct 2026)", "(Stripe)", "(pi_123)", "(Maja Lind)", "(maja@test.example)",
            "(Two-step 100K)", "(99.00)", "(Discount, code SAVE10)", "(-9.90)", "(89.10 USD)", "(Receipt for Order 1002)", "(Kronant Prop)",
            "(Thank you. This order is paid, so there is nothing more to pay.)",
        })
        {
            Assert.Contains(expected, text, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("VAT", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReceiptOfARefundedOrderSaysSo()
    {
        var order = PaidOrder(PaymentProvider.Stripe, 99m) with { RefundedAt = PaidAt.AddDays(1) };

        var text = Encoding.Latin1.GetString(ReceiptPdf.Render(new OrderReceipt(order, null), Firm, "Kronant Prop"));

        Assert.Contains("(The payment was refunded on 6 Oct 2026.)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("nothing more to pay", text, StringComparison.Ordinal);

        // Without the challenge, the receipt names the order's challenge by its id.
        Assert.Contains("(two-step-100k)", text, StringComparison.Ordinal);
    }

    private static Order PaidOrder(PaymentProvider provider, decimal amount, string? discountCode = null, decimal? listAmount = null) =>
        new(
            Guid.CreateVersion7(PaidAt),
            Firm.Id,
            1002,
            "maja@test.example",
            "two-step-100k",
            amount,
            "USD",
            provider,
            OrderStatus.Paid,
            null,
            new Uri("https://aurora.example/checkout/test"),
            provider == PaymentProvider.Stripe ? "pi_123" : null,
            Guid.CreateVersion7(PaidAt),
            null,
            PaidAt.AddMinutes(-5),
            PaidAt.AddHours(1),
            PaidAt,
            null,
            null,
            null,
            [],
            "Maja Lind",
            "SE",
            discountCode,
            listAmount);
}
