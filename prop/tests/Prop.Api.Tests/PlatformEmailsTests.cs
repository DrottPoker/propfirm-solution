using Prop.Api.Email;

namespace Prop.Api.Tests;

/// <summary>Our own emails to firms and our staff: the plain text as it was, and the same text as HTML in our look.</summary>
public sealed class PlatformEmailsTests
{
    private const string Platform = "Kronant Prop";

    [Fact]
    public void APlatformEmailHasTheSameTextAsHtmlInOurLook()
    {
        var email = PlatformEmails.Welcome(Platform, "Acme <Capital> & Co", "owner@acme.example", new Uri("https://acme.example/admin/login"));

        Assert.StartsWith("Hi,\n\nAcme <Capital> & Co is set up in its sandbox", email.Body, StringComparison.Ordinal);
        Assert.EndsWith("You get this email as an administrator of Acme <Capital> & Co on Kronant Prop.\n\nKronant Prop", email.Body, StringComparison.Ordinal);
        Assert.NotNull(email.Html);

        // The wordmark is text: Kronant in a serif, and the product small and in capitals.
        Assert.Contains("""font-family:Georgia,'Times New Roman',serif;font-size:26px;line-height:1;color:#15171b">Kronant</span>""", email.Html, StringComparison.Ordinal);
        Assert.Contains(">PROP</span>", email.Html, StringComparison.Ordinal);

        // The main link is a brass button with dark text, the steps a list, and the reason is in the footer.
        Assert.Contains("""<a href="https://acme.example/admin/login" style="display:inline-block;background:#c9a35b;color:#15120c""", email.Html, StringComparison.Ordinal);
        Assert.Contains(">Open your admin panel</a>", email.Html, StringComparison.Ordinal);
        Assert.Contains(">Give your first challenge a price, so it is for sale in your shop.</li>", email.Html, StringComparison.Ordinal);
        Assert.Contains(">You get this email as an administrator of Acme &lt;Capital&gt; &amp; Co on Kronant Prop.</p>", email.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<Capital>", email.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<svg", email.Html, StringComparison.Ordinal);
    }

    // A receipt's "label: value" lines become a table, and an email with no reason has only the platform in its footer.
    [Fact]
    public void AReceiptIsATable()
    {
        var receipt = "Receipt for invoice ACME-0001, paid 5 Oct 2026:\n\nReview deposit, taken off the startup fee: 200.00 USD\nTotal paid: 200.00 USD\n\nThe invoice is a PDF under Plan and billing in your admin panel.";

        var email = PlatformEmails.PaymentReceived(Platform, "Acme", "owner@acme.example", receipt, new Uri("https://acme.example/admin/go-live"));
        var signup = PlatformEmails.ConfirmSignup(Platform, "owner@acme.example", new Uri("https://app.example/signup/verify?token=abc"), TimeSpan.FromHours(24));

        Assert.Contains(receipt, email.Body, StringComparison.Ordinal);
        Assert.Contains(">Review deposit, taken off the startup fee</td>", email.Html, StringComparison.Ordinal);
        Assert.Contains(">200.00 USD</td></tr></table>", email.Html, StringComparison.Ordinal);
        Assert.EndsWith("nothing is created.\n\nKronant Prop", signup.Body, StringComparison.Ordinal);
        Assert.Contains(">Confirm your email</a>", signup.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("You get this email", signup.Html, StringComparison.Ordinal);
    }

    // Without a paragraph that is only a link there is no button, and the links in the text are links still.
    [Fact]
    public void LinksInTheTextAreLinks()
    {
        var email = PlatformEmails.LoginHelp(
            Platform,
            "owner@acme.example",
            [
                ("Acme", new Uri("https://acme.example/admin/welcome?token=a"), new Uri("https://acme.example/admin/login")),
                ("Beta", new Uri("https://beta.example/admin/welcome?token=b"), new Uri("https://beta.example/admin/login")),
            ],
            TimeSpan.FromHours(1));

        Assert.DoesNotContain("display:inline-block", email.Html, StringComparison.Ordinal);
        Assert.Contains("""<a href="https://beta.example/admin/welcome?token=b" """, email.Html, StringComparison.Ordinal);
        Assert.Contains(""">https://acme.example/admin/login</a>, worth a bookmark.)""", email.Html, StringComparison.Ordinal);
    }
}
