using MailKit.Net.Smtp;
using MailKit.Security;

using Microsoft.Extensions.Options;

using MimeKit;

using Prop.Api.Configuration;

namespace Prop.Api.Email;

/// <summary>An email from the platform, in plain text.</summary>
internal sealed record EmailMessage(string To, string Subject, string Body);

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
        mime.From.Add(new MailboxAddress(email.FromName, email.From));
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
