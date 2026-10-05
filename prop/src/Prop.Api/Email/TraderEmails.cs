using System.Globalization;
using System.Net;
using System.Text;

using Prop.Api.Firms;

namespace Prop.Api.Email;

/// <summary>
/// The emails to a firm's traders (ADR 0033). They come in the firm's name and look, with its logo and accent color,
/// and replies go to the firm's support address when it has one. Each has the same text in plain text too, for mail
/// programs that show no HTML.
/// </summary>
internal static class TraderEmails
{
    private const string DefaultAccent = "#2563eb";

    /// <summary>
    /// An email to a trader: paragraphs of text and, with <paramref name="link"/>, a button to it. <paramref name="note"/>
    /// is a line in small print under the button, for example how long the link works. Without
    /// <paramref name="askForReplies"/> the email does not ask the trader to reply to it, for example when the answer
    /// belongs in the portal, though a reply still goes to the firm's support address.
    /// </summary>
    public static EmailMessage Create(
        Firm firm,
        string to,
        string subject,
        IReadOnlyList<string> paragraphs,
        string? button,
        Uri? link,
        string? note = null,
        bool askForReplies = true)
    {
        var text = new StringBuilder("Hi,\n\n");
        foreach (var paragraph in paragraphs)
        {
            text.Append(paragraph).Append("\n\n");
        }

        if (link is not null)
        {
            text.Append(link).Append("\n\n");
        }

        if (note is not null)
        {
            text.Append(note).Append("\n\n");
        }

        if (firm.SupportEmail is not null && askForReplies)
        {
            text.Append("Questions? Reply to this email.\n\n");
        }

        text.Append(firm.Name);
        return new EmailMessage(to, subject, text.ToString(), firm.Name, Html(firm, paragraphs, button, link, note, askForReplies), firm.SupportEmail);
    }

    /// <summary>After paying for a challenge in the portal: a link that confirms the email and lets the buyer choose a password.</summary>
    public static EmailMessage InviteBuyer(Firm firm, string challengeName, string to, Uri link, TimeSpan lifetime) =>
        Create(
            firm,
            to,
            $"Your {challengeName} with {firm.Name} is starting",
            [
                $"Thank you for buying {challengeName} from {firm.Name}. Your challenge is being set up now.",
                $"Open this link to confirm your email and get into {firm.Name}'s portal, where you follow your challenge and open the trading terminal. If you have not chosen a password yet, you choose it there.",
            ],
            "Confirm your email",
            link,
            $"The link works once, within {PlatformEmails.Lifetime(lifetime)}. If you did not buy this, you can ignore this email.");

    /// <summary>The firm started a challenge for a trader who has no password for its portal yet, or has not confirmed the email.</summary>
    public static EmailMessage InviteTrader(Firm firm, string challengeName, string to, Uri link, TimeSpan lifetime) =>
        Create(
            firm,
            to,
            $"Your {challengeName} with {firm.Name} has started",
            [$"{firm.Name} has started {challengeName} for you. Open this link to get into {firm.Name}'s portal, where you follow your challenge and open the trading terminal. If you have not chosen a password yet, you choose it there."],
            "Open the portal",
            link,
            $"The link works once, within {PlatformEmails.Lifetime(lifetime)}.");

    /// <summary>The firm started another challenge for a trader who already has a password for its portal.</summary>
    public static EmailMessage ChallengeStarted(Firm firm, string challengeName, string to, Uri accountUrl) =>
        Create(
            firm,
            to,
            $"Your {challengeName} with {firm.Name} has started",
            [$"{firm.Name} has started {challengeName} for you. Log in to {firm.Name}'s portal to follow it and open the trading terminal."],
            "Open your account",
            accountUrl);

    /// <summary>A trader forgot the password for the firm's portal.</summary>
    public static EmailMessage ResetPassword(Firm firm, string to, Uri link, TimeSpan lifetime) =>
        Create(
            firm,
            to,
            $"Choose a new password for {firm.Name}",
            [$"Open this link to choose a new password for {firm.Name}'s portal."],
            "Choose a new password",
            link,
            $"The link works once, within {PlatformEmails.Lifetime(lifetime)}. If you did not ask for a new password, you can ignore this email, and your password stays as it is.");

    /// <summary>A trader who chose a password on the order's page asks for the link that confirms the email again.</summary>
    public static EmailMessage ConfirmEmail(Firm firm, string to, Uri link, TimeSpan lifetime) =>
        Create(
            firm,
            to,
            $"Confirm your email for {firm.Name}",
            [$"Open this link to confirm that this is your email for {firm.Name}'s portal. Payouts are paid only to traders who have."],
            "Confirm your email",
            link,
            $"The link works once, within {PlatformEmails.Lifetime(lifetime)}. If you did not ask for this, you can ignore this email.");

    /// <summary>The firm went live, so a trader's account from its sandbox ended. A firm that sells in its portal points to its shop.</summary>
    public static EmailMessage SandboxAccountEnded(Firm firm, string to, string challengeName, long accountNumber) =>
        Create(
            firm,
            to,
            $"Your test account with {firm.Name} has ended",
            [
                $"{firm.Name} has finished testing and is now open for real. Your {challengeName} (account #{accountNumber.ToString(CultureInfo.InvariantCulture)}) was a test account, so it has ended, with any trades on it closed.",
                firm.Payments.Provider is null ? $"Ask {firm.Name} if you want a new challenge." : $"New challenges are bought in {firm.Name}'s shop.",
            ],
            firm.Payments.Provider is null ? "Go to the portal" : "Go to the shop",
            new Uri(firm.Portal.Url, firm.Payments.Provider is null ? "" : "buy"));

    /// <summary>
    /// The firm answered the trader's support ticket (ADR 0041), with the answer. The email asks the trader to answer in the
    /// portal, so the conversation stays in one place.
    /// </summary>
    public static EmailMessage SupportAnswer(Firm firm, string to, long ticketNumber, string subject, string answer, int files, bool closed, Uri ticketUrl) =>
        Create(
            firm,
            to,
            $"{firm.Name} answered your ticket #{ticketNumber.ToString(CultureInfo.InvariantCulture)}",
            [
                $"{firm.Name} answered your support ticket \"{subject}\":",
                answer,
                .. files > 0 ? [FilesNote(files)] : Array.Empty<string>(),
                closed
                    ? $"{firm.Name} has closed the ticket. If you need more help, write in it again in the portal."
                    : "Answer in the portal, so the whole conversation stays in one place.",
            ],
            "Open the ticket",
            ticketUrl,
            askForReplies: false);

    /// <summary>That a message has files, which are only in the portal, for example "1 file is attached in the ticket."</summary>
    public static string FilesNote(int files) => files == 1 ? "1 file is attached in the ticket." : $"{files.ToString(CultureInfo.InvariantCulture)} files are attached in the ticket.";

    // Tables and inline styles, which mail programs show the same.
    private static string Html(Firm firm, IReadOnlyList<string> paragraphs, string? button, Uri? link, string? note, bool askForReplies)
    {
        var colors = firm.Portal.Branding.Colors;
        var accent = colors.TryGetValue("accent", out var a) ? a : DefaultAccent;
        var onAccent = colors.TryGetValue("accent-foreground", out var f) ? f : ForegroundOn(accent);
        var name = Encode(firm.Name);
        var logo = firm.Portal.Branding.LogoUrl is { } logoUrl && Uri.TryCreate(firm.Portal.Url, logoUrl, out var logoUri)
            ? $"""<img src="{Encode(logoUri.AbsoluteUri)}" alt="{name}" height="40" style="display:block;height:40px;max-width:240px;border:0">"""
            : $"""<span style="font-size:20px;font-weight:700;color:{accent}">{name}</span>""";
        var html = new StringBuilder();
        html.Append(CultureInfo.InvariantCulture, $"""
            <!doctype html>
            <html><body style="margin:0;padding:0;background:#f4f4f5">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f4f4f5;padding:24px 12px">
            <tr><td align="center">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;background:#ffffff;border-radius:8px;font-family:-apple-system,'Segoe UI',Helvetica,Arial,sans-serif;font-size:15px;line-height:1.5;color:#18181b">
            <tr><td style="padding:24px 28px 8px">{logo}</td></tr>
            <tr><td style="padding:8px 28px 0">
            <p style="margin:0 0 14px">Hi,</p>
            """);
        foreach (var paragraph in paragraphs)
        {
            // A paragraph can be a message someone wrote, with lines of its own.
            html.Append(CultureInfo.InvariantCulture, $"""<p style="margin:0 0 14px">{Encode(paragraph).Replace("\n", "<br>", StringComparison.Ordinal)}</p>""").Append('\n');
        }

        if (link is not null)
        {
            var href = Encode(link.AbsoluteUri);
            html.Append(CultureInfo.InvariantCulture, $"""
                <p style="margin:20px 0"><a href="{href}" style="display:inline-block;background:{accent};color:{onAccent};padding:11px 20px;border-radius:6px;text-decoration:none;font-weight:600">{Encode(button ?? "Open")}</a></p>
                <p style="margin:0 0 14px;font-size:13px;color:#71717a">Or open this link: <a href="{href}" style="color:#71717a;word-break:break-all">{href}</a></p>
                """);
        }

        if (note is not null)
        {
            html.Append(CultureInfo.InvariantCulture, $"""<p style="margin:0 0 14px;font-size:13px;color:#71717a">{Encode(note)}</p>""").Append('\n');
        }

        var reply = firm.SupportEmail is not null && askForReplies ? " Questions? Reply to this email." : "";
        html.Append(CultureInfo.InvariantCulture, $"""
            </td></tr>
            <tr><td style="padding:16px 28px 24px;border-top:1px solid #e4e4e7;font-size:13px;color:#71717a">{name}.{Encode(reply)}</td></tr>
            </table>
            </td></tr>
            </table>
            </body></html>
            """);
        return html.ToString();
    }

    private static string Encode(string text) => WebUtility.HtmlEncode(text);

    // Black or white, whichever reads better on the color, as the portal does for a color the firm did not pair.
    private static string ForegroundOn(string hex)
    {
        if (hex.Length != 7 || !int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return "#ffffff";
        }

        static double Channel(int value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        var luminance = (0.2126 * Channel((rgb >> 16) & 0xff)) + (0.7152 * Channel((rgb >> 8) & 0xff)) + (0.0722 * Channel(rgb & 0xff));
        return luminance > 0.179 ? "#000000" : "#ffffff";
    }
}
