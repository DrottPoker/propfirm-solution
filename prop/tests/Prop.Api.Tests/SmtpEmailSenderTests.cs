using System.Net.Http.Json;
using System.Text.Json;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

using Microsoft.Extensions.Options;

using Prop.Api.Configuration;
using Prop.Api.Email;

namespace Prop.Api.Tests;

/// <summary>The real SMTP sender against Mailpit in a container, the mail server used in development. Needs Docker.</summary>
public sealed class SmtpEmailSenderTests : IAsyncLifetime
{
    private const int SmtpPort = 1025;
    private const int WebPort = 8025;

    private readonly IContainer _mailpit = new ContainerBuilder("axllent/mailpit:v1.29")
        .WithPortBinding(SmtpPort, true)
        .WithPortBinding(WebPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(WebPort).ForPath("/readyz")))
        .Build();

    public async ValueTask InitializeAsync() => await _mailpit.StartAsync();

    public async ValueTask DisposeAsync() => await _mailpit.DisposeAsync();

    [Fact]
    public async Task TheEmailReachesTheMailServer()
    {
        var sender = Sender(_mailpit.Hostname, _mailpit.GetMappedPublicPort(SmtpPort));

        await sender.SendAsync(new EmailMessage("owner@firm.test", "Confirm your email", "Open https://app.test/verify?token=abc"), TestContext.Current.CancellationToken);

        using var mailbox = new HttpClient { BaseAddress = new Uri($"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(WebPort)}/") };
        var message = (await mailbox.GetFromJsonAsync<JsonElement>(new Uri("api/v1/messages", UriKind.Relative), TestContext.Current.CancellationToken))
            .GetProperty("messages")[0];
        var full = await mailbox.GetFromJsonAsync<JsonElement>(
            new Uri($"api/v1/message/{message.GetProperty("ID").GetString()}", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal("Confirm your email", message.GetProperty("Subject").GetString());
        Assert.Equal(("no-reply@platform.test", "Prop platform"), (message.GetProperty("From").GetProperty("Address").GetString(), message.GetProperty("From").GetProperty("Name").GetString()));
        Assert.Equal("owner@firm.test", message.GetProperty("To")[0].GetProperty("Address").GetString());
        Assert.Contains("https://app.test/verify?token=abc", full.GetProperty("Text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMailServerThatCannotBeReachedMeansTheEmailWasNotSent()
    {
        // Nothing listens on the port, so the connection is refused.
        var sender = Sender("127.0.0.1", 1);

        await Assert.ThrowsAsync<EmailNotSentException>(() =>
            sender.SendAsync(new EmailMessage("owner@firm.test", "Subject", "Body"), TestContext.Current.CancellationToken));
    }

    private static SmtpEmailSender Sender(string host, int port) =>
        new(Options.Create(new EmailOptions
        {
            From = "no-reply@platform.test",
            FromName = "Prop platform",
            Smtp = new SmtpOptions { Host = host, Port = port, Security = "None" },
        }));
}
