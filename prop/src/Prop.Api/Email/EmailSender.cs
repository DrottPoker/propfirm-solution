using MailKit.Net.Smtp;
using MailKit.Security;

using Microsoft.Extensions.Options;

using MimeKit;

using Prop.Api.Configuration;

namespace Prop.Api.Email;

/// <summary>An email from the platform, in plain text. <paramref name="FromName"/> replaces the platform's name as sender, for example with a firm's.</summary>
internal sealed record EmailMessage(string To, string Subject, string Body, string? FromName = null);

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
        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.Body };

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

/// <summary>The platform's emails. Plain text, so they read the same in every mail program.</summary>
internal static class PlatformEmails
{
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

    /// <summary>
    /// A buyer's invitation to the firm's portal after paying for a challenge there. Sent in the firm's name, since
    /// the trader bought from the firm.
    /// </summary>
    public static EmailMessage InviteBuyer(string firmName, string challengeName, string to, Uri link, TimeSpan lifetime) =>
        new(
            to,
            $"Your {challengeName} with {firmName} is starting",
            $"""
            Hi,

            Thank you for buying {challengeName} from {firmName}. Your challenge is being set up now. Open this link to choose a password for {firmName}'s portal, where you follow your challenge and open the trading terminal:

            {link}

            The link works once, within {lifetime.TotalDays:0} days. If you did not buy this, you can ignore this email.

            {firmName}
            """,
            firmName);

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
    public static EmailMessage ChangesRequested(string platform, string firmName, string to, string message, Uri verificationUrl) =>
        new(
            to,
            $"Changes needed for {firmName}",
            $"""
            Hi,

            We have looked at the application for {firmName} and need a few changes before we can approve it:

            {message}

            Change your application and send it again in your admin panel. You do not pay the deposit again.

            {verificationUrl}

            {platform}
            """);

    /// <summary>The firm is approved and can go live by paying.</summary>
    public static EmailMessage ApplicationApproved(string platform, string firmName, string to, string? message, Uri billingUrl) =>
        new(
            to,
            $"{firmName} is approved",
            $"""
            Hi,

            {firmName} is approved to go live.{(message is null ? "" : $" {message}")} Choose your slots and pay to go live in your admin panel. The deposit you paid is taken off the startup fee.

            {billingUrl}

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

            The firm cannot go live on {platform}, and the deposit is not paid back. Reply to this email if you have questions.

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
