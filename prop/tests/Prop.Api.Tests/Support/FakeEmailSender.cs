using System.Text.RegularExpressions;

using Prop.Api.Email;

namespace Prop.Api.Tests.Support;

/// <summary>Keeps the emails the platform sends, instead of sending them. Can act as a mail server that is down.</summary>
internal sealed partial class FakeEmailSender : IEmailSender
{
    private readonly Lock _lock = new();
    private readonly List<EmailMessage> _sent = [];
    private int _failures;

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

    /// <summary>The next emails cannot be sent.</summary>
    public void FailNext(int emails)
    {
        lock (_lock)
        {
            _failures = emails;
        }
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_failures > 0)
            {
                _failures--;
                throw new EmailNotSentException("The fake mail server is down.");
            }

            _sent.Add(message);
            return Task.CompletedTask;
        }
    }

    /// <summary>The token in the link of the newest email to the address.</summary>
    public string TokenFor(string to) => TokenIn(Sent.Last(e => e.To == to));

    public static string TokenIn(EmailMessage email) => Token().Match(email.Body).Groups[1].Value;

    [GeneratedRegex(@"token=([A-Za-z0-9_\-]+)")]
    private static partial Regex Token();
}
