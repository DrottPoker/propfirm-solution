using System.Text.RegularExpressions;

using Prop.Api.Email;

namespace Prop.Api.Tests.Support;

/// <summary>Keeps the emails the platform sends, instead of sending them. Can act as a mail server that is down.</summary>
internal sealed partial class FakeEmailSender : IEmailSender
{
    private readonly Lock _lock = new();
    private readonly List<EmailMessage> _sent = [];
    private int _failures;
    private string? _failingRecipient;

    public IReadOnlyList<EmailMessage> Sent
    {
        get
        {
            lock (_lock)
            {
                return [.. _sent];
            }
        }
    }

    /// <summary>The next emails cannot be sent, or only the next ones to <paramref name="to"/>.</summary>
    public void FailNext(int emails, string? to = null)
    {
        lock (_lock)
        {
            _failures = emails;
            _failingRecipient = to;
        }
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_failures > 0 && (_failingRecipient is null || string.Equals(_failingRecipient, message.To, StringComparison.OrdinalIgnoreCase)))
            {
                _failures--;
                throw new EmailNotSentException("The fake mail server is down.");
            }

            _sent.Add(message);
            return Task.CompletedTask;
        }
    }

    /// <summary>Waits for an email to the address with the subject, since queued emails are sent in the background.</summary>
    public async Task<EmailMessage> WaitForAsync(string to, string subject)
    {
        EmailMessage? found = null;
        await Eventually.ThatAsync(
            () => (found = Sent.LastOrDefault(e => e.To == to && e.Subject.Contains(subject, StringComparison.Ordinal))) is not null,
            $"the email \"{subject}\" to {to}");
        return found!;
    }

    /// <summary>The token in the link of the newest email to the address.</summary>
    public string TokenFor(string to) => TokenIn(Sent.Last(e => e.To == to));

    public static string TokenIn(EmailMessage email) => Token().Match(email.Body).Groups[1].Value;

    [GeneratedRegex(@"token=([A-Za-z0-9_\-]+)")]
    private static partial Regex Token();
}
