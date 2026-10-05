using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Prop.Api.Tests.Support;

namespace Prop.Api.Tests;

/// <summary>A sandbox nobody uses closes after a warning, and opens again when an administrator comes back (ADR 0045).</summary>
public sealed class IdleSandboxTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Owner = "owner@firm.test";

    [Fact]
    public async Task ASandboxNobodyUsesIsWarnedClosedAndOpenedAgain()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme", Owner);
        await PropFactory.WaitUntilProvisionedAsync(admin);
        using var firmApi = factory.CreateFirmClient(await NewApiKeyAsync(admin));

        // 54 days without the admin panel: a week before the 60, the administrators are warned.
        await factory.AdvanceUntilAsync(TimeSpan.FromDays(54), () => Warned(factory), "the warning", TimeSpan.FromHours(1));
        var warning = factory.Emails.Sent.Single(e => e.To == Owner && e.Subject.StartsWith("The sandbox of Firm acme closes on", StringComparison.Ordinal));

        // The firm API keeps no sandbox open. A week later it closes: the test account ends, and no other starts.
        using var startedBefore = await firmApi.PostAsJsonAsync(FirmUrl("accounts"), new { email = Owner, challengeId = "two-step-100k" }, TestContext.Current.CancellationToken);
        var accountId = (await startedBefore.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
        await factory.AdvanceUntilAsync(TimeSpan.FromDays(7), () => Closed(factory), "the sandbox to close", TimeSpan.FromHours(1));
        var closed = factory.Emails.Sent.Single(e => e.To == Owner && e.Subject == "The sandbox of Firm acme is closed");
        var status = await factory.ScalarAsync($"select status from challenge_accounts where id = '{accountId}'");
        using var refused = await firmApi.PostAsJsonAsync(FirmUrl("accounts"), new { email = Owner, challengeId = "two-step-100k" }, TestContext.Current.CancellationToken);
        var refusal = await refused.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        // Coming back to the admin panel opens it again. The session from before has expired, so the owner logs in.
        using var back = await LogInAsync(admin);
        using var started = await firmApi.PostAsJsonAsync(FirmUrl("accounts"), new { email = Owner, challengeId = "two-step-100k" }, TestContext.Current.CancellationToken);

        Assert.Contains("to keep it open", warning.Body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Created, startedBefore.StatusCode);
        Assert.Contains("Its test account has ended.", closed.Body, StringComparison.Ordinal);
        Assert.Equal("Cancelled", status);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.StartsWith("The sandbox is closed", refusal.GetProperty("title").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
    }

    [Fact]
    public async Task UsingTheAdminPanelKeepsTheSandboxOpen()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme", Owner);
        await PropFactory.WaitUntilProvisionedAsync(admin);

        await factory.AdvanceUntilAsync(TimeSpan.FromDays(54), () => Warned(factory), "the warning", TimeSpan.FromHours(1));
        (await LogInAsync(admin)).Dispose();
        await factory.AdvanceAsync(TimeSpan.FromDays(7));
        await factory.AdvanceAsync(TimeSpan.FromHours(2));

        Assert.IsType<DBNull>(await factory.ScalarAsync("select sandbox_closed_at from firms where id = 'acme'"));
        Assert.DoesNotContain(factory.Emails.Sent, e => e.Subject.EndsWith("is closed", StringComparison.Ordinal));
    }

    private static bool Warned(PropFactory factory) => factory.Emails.Sent.Any(e => e.To == Owner && e.Subject.StartsWith("The sandbox of Firm acme closes on", StringComparison.Ordinal));

    private static bool Closed(PropFactory factory) => factory.Emails.Sent.Any(e => e.To == Owner && e.Subject == "The sandbox of Firm acme is closed");

    private static Uri Url(string path) => new($"/api/portal/{path}", UriKind.Relative);

    private static Task<HttpResponseMessage> LogInAsync(HttpClient admin) =>
        admin.PostAsJsonAsync(Url("admin/login"), new { email = Owner, password = PropFactory.SignupPassword }, TestContext.Current.CancellationToken);

    private static Uri FirmUrl(string path) => new($"/api/firm/v1/{path}", UriKind.Relative);

    private static async Task<string> NewApiKeyAsync(HttpClient admin)
    {
        using var response = await admin.PostAsync(Url("admin/firm/api-key"), null, TestContext.Current.CancellationToken);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("apiKey").GetString()!;
    }
}
