using MailKit.Net.Smtp;
using MailKit.Security;

using Microsoft.Extensions.Options;

using MimeKit;

using Prop.Api.Configuration;

namespace Prop.Api.Email;

/// <summary>
/// An email from the platform, in plain text, and with <paramref name="Html"/> also as HTML. <paramref name="FromName"/>
/// replaces the platform's name as sender, for example with a firm's, and <paramref name="ReplyTo"/> is where replies go.
/// </summary>
internal sealed record EmailMessage(string To, string Subject, string Body, string? FromName = null, string? Html = null, string? ReplyTo = null);

/// <summary>Sends email from the platform. Throws <see cref="EmailNotSentException"/> when the mail server cannot take it.</summary>
internal interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Sends email through the configured SMTP server. Locally that is Mailpit, which keeps every email for viewing.</summary>
internal sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var email = options.Value;
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(message.FromName ?? email.FromName, email.From));
        mime.To.Add(MailboxAddress.Parse(message.To));
        if (message.ReplyTo is { } replyTo)
        {
            mime.ReplyTo.Add(MailboxAddress.Parse(replyTo));
        }

        mime.Subject = message.Subject;
        mime.Body = message.Html is null ? new TextPart("plain") { Text = message.Body } : new BodyBuilder { TextBody = message.Body, HtmlBody = message.Html }.ToMessageBody();

        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(email.Smtp.Host, email.Smtp.Port, Enum.Parse<SecureSocketOptions>(email.Smtp.Security), cancellationToken);
            if (email.Smtp.UserName.Length > 0)
            {
                await client.AuthenticateAsync(email.Smtp.UserName, email.Smtp.Password, cancellationToken);
            }

            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
        catch (Exception exception) when (exception is SmtpCommandException or SmtpProtocolException or AuthenticationException or IOException or System.Net.Sockets.SocketException)
        {
            throw new EmailNotSentException($"The email to {message.To} could not be sent: {exception.Message}", exception);
        }
    }
}

internal sealed class EmailNotSentException : Exception
{
    public EmailNotSentException()
    {
    }

    public EmailNotSentException(string message)
        : base(message)
    {
    }

    public EmailNotSentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The platform's emails to firms and our staff. Plain text, so they read the same in every mail program. Those to traders are in <see cref="TraderEmails"/>.</summary>
internal static class PlatformEmails
{
    /// <summary>How long a link works, in words, for example "1 hour" or "7 days".</summary>
    public static string Lifetime(TimeSpan lifetime) =>
        lifetime.TotalDays >= 1 ? Plural(lifetime.TotalDays, "day") : lifetime.TotalHours >= 1 ? Plural(lifetime.TotalHours, "hour") : Plural(lifetime.TotalMinutes, "minute");

    private static string Plural(double count, string unit)
    {
        var whole = (int)Math.Round(count);
        return whole == 1 ? $"1 {unit}" : $"{whole} {unit}s";
    }

    /// <summary>To a firm's first administrator when the firm is created: where the admin panel is, and the first steps.</summary>
    public static EmailMessage Welcome(string platform, string firmName, string to, Uri adminLogin) =>
        new(
            to,
            $"Welcome to {platform}: {firmName} is ready to try",
            $"""
            Hi,

            {firmName} is set up in its sandbox, where you can try everything with test accounts and test payments. Your admin panel is here, worth a bookmark:

            {adminLogin}

            Three steps to begin:

            1. Add your logo and brand color, so the portal looks like yours.
            2. Give your first challenge a price, so it is for sale in your shop.
            3. Choose how traders pay. Test payments are enough to try.

            Then buy a challenge in your own shop and place a trade, as your traders will. When you are ready, send your company for review and go live.

            {platform}
            """);

    public static EmailMessage ConfirmSignup(string platform, string firmName, string to, Uri link, TimeSpan lifetime) =>
        new(
            to,
            $"Confirm your email for {firmName}",
            $"""
            Hi,

            Thank you for signing up {firmName} on {platform}. Open this link to confirm your email address and open your admin panel:

            {link}

            The link works once, within {lifetime.TotalHours:0} hours. If you did not sign up, you can ignore this email.

            {platform}
            """);

    /// <summary>A charge of the firm's saved card was declined.</summary>
    public static EmailMessage PaymentDeclined(
        string platform,
        string firmName,
        string to,
        string charge,
        string amount,
        string reason,
        DateTimeOffset? nextAttempt,
        DateTimeOffset? pausedFrom,
        Uri billingUrl)
    {
        var next = nextAttempt is { } attempt
            ? $"We try the card again on {attempt:yyyy-MM-dd} at {attempt:HH:mm} UTC."
            : "We do not try the card again by ourselves.";
        var pause = pausedFrom is { } from
            ? $" If it is not paid by {from:yyyy-MM-dd} at {from:HH:mm} UTC, no new challenges can start and your traders' accounts are paused until it is."
            : "";
        return new(
            to,
            $"Payment for {firmName} declined",
            $"""
            Hi,

            We could not charge your card {amount} for {charge}: {reason}

            {next}{pause} Pay with another card or try again in your admin panel:

            {billingUrl}

            {platform}
            """);
    }

    /// <summary>The month started unpaid, so the firm's challenges are paused.</summary>
    public static EmailMessage FirmPaused(string platform, string firmName, string to, Uri billingUrl) =>
        new(
            to,
            $"{firmName} is paused until this month is paid",
            $"""
            Hi,

            This month's slots for {firmName} are not paid yet. Until they are, no new challenges can start, and your traders' accounts are paused: they cannot open new trades, but they can close the ones they have, and their days do not count. Everything goes on as soon as the payment goes through.

            Pay with another card or try again in your admin panel:

            {billingUrl}

            {platform}
            """);

    /// <summary>Most of the firm's slots are taken.</summary>
    public static EmailMessage SlotsNearlyFull(string platform, string firmName, string to, int taken, int slots, bool autoExpand, Uri billingUrl) =>
        new(
            to,
            $"{firmName} has used {taken} of {slots} slots",
            $"""
            Hi,

            {taken} of your {slots} slots for open challenges are taken. When every slot is taken, no new challenges can start and your shop stops selling until a challenge ends or you buy more slots.{(autoExpand ? " Automatic expansion is on, so more slots are bought when the last one is taken." : "")}

            See your slots and buy more in your admin panel:

            {billingUrl}

            {platform}
            """);

    /// <summary>
    /// We have the firm's application, sent when its deposit was paid or sent again after changes. With the deposit's
    /// <paramref name="receipt"/>, when it was paid.
    /// </summary>
    public static EmailMessage ApplicationReceived(string platform, string firmName, string to, string? receipt, Uri goLiveUrl) =>
        new(
            to,
            $"We have received the application for {firmName}",
            $"""
            Hi,

            Thank you. We have received the application for {firmName}, and review it by hand, usually within a day. We email you when we have decided, and you follow the review in your admin panel:

            {goLiveUrl}
            {(receipt is null ? "" : $"\n{receipt}\n")}
            {platform}
            """);

    /// <summary>A payment the firm made on a checkout page went through, with its receipt.</summary>
    public static EmailMessage PaymentReceived(string platform, string firmName, string to, string receipt, Uri billingUrl) =>
        new(
            to,
            $"Payment received for {firmName}",
            $"""
            Hi,

            Thank you. We have received your payment for {firmName}.

            {receipt}

            {billingUrl}

            {platform}
            """);

    /// <summary>The firm paid and is live: the receipt, and what happens now. Without <paramref name="shopUrl"/>, the firm does not sell in its portal.</summary>
    public static EmailMessage FirmLive(string platform, string firmName, string to, string receipt, int sandboxAccounts, Uri adminUrl, Uri? shopUrl) =>
        new(
            to,
            $"{firmName} is live",
            $"""
            Hi,

            {firmName} is live.{(shopUrl is null ? "" : " Your shop takes real payments from now on.")} New challenges count against your slots.

            What happens now:

            1. {(shopUrl is null ? "Start challenges for your traders from your own systems or the admin panel." : $"Your shop is open at {shopUrl}. Share it with your traders.")}
            2. {(sandboxAccounts == 0 ? "You had no test accounts left from the sandbox." : sandboxAccounts == 1 ? "Your test account from the sandbox has ended, and its trader got an email." : $"Your {sandboxAccounts} test accounts from the sandbox have ended, and their traders got an email.")}
            3. Each month is charged to your card some days before it starts. Your slots, charges and invoices are under Plan and billing.

            {receipt}

            {adminUrl}

            {platform}
            """);

    /// <summary>To our staff: a firm sent its application and waits for our review.</summary>
    public static EmailMessage ApplicationSubmitted(string platform, string firmName, string firmId, string to, Uri reviewUrl) =>
        new(
            to,
            $"{firmName} is waiting for review",
            $"""
            Hi,

            {firmName} ({firmId}) sent its application to go live. Review it here:

            {reviewUrl}

            {platform}
            """);

    /// <summary>We need the firm to change its application before we can approve it.</summary>
    public static EmailMessage ChangesRequested(string platform, string firmName, string to, string message, Uri goLiveUrl) =>
        new(
            to,
            $"Changes needed for {firmName}",
            $"""
            Hi,

            We have looked at the application for {firmName} and need a few changes before we can approve it:

            {message}

            Change your application and send it again in your admin panel. You do not pay the deposit again.

            {goLiveUrl}

            {platform}
            """);

    /// <summary>The firm is approved and can go live by paying.</summary>
    public static EmailMessage ApplicationApproved(string platform, string firmName, string to, string? message, Uri goLiveUrl) =>
        new(
            to,
            $"{firmName} is approved",
            $"""
            Hi,

            {firmName} is approved to go live.{(message is null ? "" : $" {message}")} Choose your slots and pay to go live in your admin panel. The deposit you paid is taken off the startup fee.

            {goLiveUrl}

            {platform}
            """);

    /// <summary>The firm was not approved, and cannot go live.</summary>
    public static EmailMessage ApplicationRejected(string platform, string firmName, string to, string message) =>
        new(
            to,
            $"{firmName} was not approved",
            $"""
            Hi,

            We have reviewed the application for {firmName}, and we cannot approve it:

            {message}

            The firm cannot go live on {platform}. The deposit paid for the review, which we did by hand, so it is not paid back. Reply to this email if you have questions.

            {platform}
            """);

    /// <summary>We suspended the firm.</summary>
    public static EmailMessage FirmSuspended(string platform, string firmName, string to, string reason, Uri adminUrl) =>
        new(
            to,
            $"{firmName} is suspended",
            $"""
            Hi,

            We have suspended {firmName} on {platform}:

            {reason}

            Until we lift the suspension, no new challenges can start, your shop is closed, and your traders' accounts are paused: they cannot open new trades, but they can close the ones they have, and their days do not count. Reply to this email to talk to us.

            {adminUrl}

            {platform}
            """);

    /// <summary>We lifted the firm's suspension.</summary>
    public static EmailMessage SuspensionLifted(string platform, string firmName, string to, Uri adminUrl) =>
        new(
            to,
            $"{firmName} is no longer suspended",
            $"""
            Hi,

            We have lifted the suspension of {firmName}, so your challenges, your shop and your traders' accounts go on as before.

            {adminUrl}

            {platform}
            """);

    /// <summary>An administrator forgot the password for the firm's admin panel.</summary>
    public static EmailMessage ResetAdminPassword(string platform, string firmName, string to, Uri link, TimeSpan lifetime) =>
        new(
            to,
            $"Choose a new password for {firmName}'s admin panel",
            $"""
            Hi,

            Open this link to choose a new password for the admin panel of {firmName} on {platform}:

            {link}

            The link works once, within {Lifetime(lifetime)}. If you did not ask for a new password, you can ignore this email, and your password stays as it is.

            {platform}
            """);

    /// <summary>One of our staff forgot the password for our admin view.</summary>
    public static EmailMessage ResetStaffPassword(string platform, string to, Uri link, TimeSpan lifetime) =>
        new(
            to,
            $"Choose a new password for {platform}'s admin view",
            $"""
            Hi,

            Open this link to choose a new password for our admin view of {platform}:

            {link}

            The link works once, within {Lifetime(lifetime)}. If you did not ask for a new password, you can ignore this email, and your password stays as it is.

            {platform}
            """);

    /// <summary>Someone asked the platform where to log in: a link that logs them in to each firm they administer.</summary>
    public static EmailMessage LoginHelp(string platform, string to, IReadOnlyList<(string Name, Uri Link, Uri Login)> firms, TimeSpan lifetime)
    {
        var list = string.Join("\n\n", firms.Select(f => $"{f.Name}\n{f.Link}\n(Its admin panel is at {f.Login}, worth a bookmark.)"));
        return new(
            to,
            firms.Count == 1 ? $"Log in to {firms[0].Name}" : $"Log in to your firms on {platform}",
            $"""
            Hi,

            Someone asked where to log in to {platform} with this email. Open a link to log in to the admin panel of the firm:

            {list}

            Each link works once, within {Lifetime(lifetime)}. If you did not ask for this, you can ignore this email.

            {platform}
            """);
    }

    public static EmailMessage InviteAdmin(string platform, string firmName, string invitedBy, string to, Uri link, TimeSpan lifetime) =>
        new(
            to,
            $"You are invited to administer {firmName}",
            $"""
            Hi,

            {invitedBy} invites you to administer {firmName} on {platform}. Open this link to choose your password:

            {link}

            The link works once, within {lifetime.TotalDays:0} days. If you did not expect this, you can ignore this email.

            {platform}
            """);
}
