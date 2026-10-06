using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Prop.Api.Email;
using Prop.Api.Tests.Support;

namespace Prop.Api.Tests;

/// <summary>
/// The notification emails as the firm sees them under Notifications before they are sent: each kind in the firm's name and
/// look and about its own challenges, with sample data, and nothing sent, queued or saved.
/// </summary>
public sealed class NotificationPreviewTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly string[] TeamKinds =
        [NotificationKinds.FirmSale, NotificationKinds.FirmFundingAwaited, NotificationKinds.FirmPayoutRequested, NotificationKinds.FirmSupport];

    [Fact]
    public async Task EveryEmailHasAPreviewInTheFirmsNameAndLook()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");
        using var colors = await admin.PutAsJsonAsync(
            Url("admin/firm/branding"), new { colors = new Dictionary<string, string> { ["accent"] = "#112233" } }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, colors.StatusCode);

        var previews = new List<JsonElement>();
        foreach (var kind in NotificationKinds.All)
        {
            previews.Add(await admin.GetFromJsonAsync<JsonElement>(Url($"admin/firm/email-settings/{kind}/preview"), TestContext.Current.CancellationToken));
        }

        Assert.Equal(NotificationKinds.All, previews.Select(p => p.GetProperty("kind").GetString()));
        Assert.All(previews, preview =>
        {
            var kind = preview.GetProperty("kind").GetString()!;
            var html = preview.GetProperty("html").GetString()!;
            var text = preview.GetProperty("text").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(preview.GetProperty("subject").GetString()));
            Assert.StartsWith("<!doctype html>", html, StringComparison.Ordinal);
            Assert.StartsWith("Hi,", text, StringComparison.Ordinal);
            if (TeamKinds.Contains(kind))
            {
                // The team's emails name the sample trader and link to the firm's admin panel.
                Assert.Equal("Team", preview.GetProperty("audience").GetString());
                Assert.Contains("Alex Example (alex@example.com)", text, StringComparison.Ordinal);
                Assert.Contains("http://acme.localhost:3002/admin/", text, StringComparison.Ordinal);
            }
            else
            {
                // The trader's emails come in the firm's name and color, and link to its portal.
                Assert.Equal("Trader", preview.GetProperty("audience").GetString());
                Assert.Contains("Firm acme", text, StringComparison.Ordinal);
                Assert.Contains("Firm acme", html, StringComparison.Ordinal);
                Assert.Contains("background:#112233", html, StringComparison.Ordinal);
                Assert.Contains("http://acme.localhost:3002/", text, StringComparison.Ordinal);
            }
        });
    }

    // Each email is about the firm's first challenge that has the phases it is about, and a sale is at the firm's price.
    [Fact]
    public async Task ThePreviewsAreAboutTheFirmsChallengesAndASamplePayout()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");
        await PropFactory.WaitUntilProvisionedAsync(admin);
        await SaveChallengeAsync(admin, "one-step", "a-one-step-25k", "Starter 25K", 25_000m);
        await SaveChallengeAsync(admin, "two-step", "gold-50k", "Gold 50K", 50_000m);
        using var price = await admin.PutAsJsonAsync(
            Url("admin/challenges/a-one-step-25k/price"), new { amount = 249m, currency = "USD", forSale = true }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, price.StatusCode);

        var sale = await PreviewAsync(admin, NotificationKinds.FirmSale);
        var payout = await PreviewAsync(admin, NotificationKinds.FirmPayoutRequested);

        Assert.Equal("New sale: Starter 25K for 249.00 USD", sale.GetProperty("subject").GetString());
        Assert.Contains("Alex Example (alex@example.com) bought Starter 25K for 249.00 USD in your portal (order 1001)", sale.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Equal("Payout request of 1,600.00 USD from Alex Example", payout.GetProperty("subject").GetString());
        Assert.Contains("80% of a profit of 2,000.00 USD", payout.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Equal("You passed your Starter 25K", (await PreviewAsync(admin, NotificationKinds.TraderPassed)).GetProperty("subject").GetString());
        Assert.Equal("You passed Phase 1 of your Gold 50K", (await PreviewAsync(admin, NotificationKinds.TraderStagePassed)).GetProperty("subject").GetString());
        Assert.Equal("Your payout of 1,600.00 USD is approved", (await PreviewAsync(admin, NotificationKinds.TraderPayouts)).GetProperty("subject").GetString());
    }

    [Fact]
    public async Task APreviewSendsQueuesAndSavesNothing()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.LogInAsAdminAsync();
        var outbox = await factory.ScalarAsync("select count(*) from email_outbox");
        var tickets = await factory.ScalarAsync("select count(*) from support_tickets");

        foreach (var kind in NotificationKinds.All)
        {
            await PreviewAsync(admin, kind);
        }

        Assert.Equal(outbox, await factory.ScalarAsync("select count(*) from email_outbox"));
        Assert.Equal(tickets, await factory.ScalarAsync("select count(*) from support_tickets"));
        Assert.DoesNotContain(factory.Emails.Sent, e => e.To == Notifications.SampleTraderEmail || e.Subject.Contains("Alex Example", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnEmailThatDoesNotExistHasNoPreview()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.LogInAsAdminAsync();

        using var unknown = await admin.GetAsync(Url("admin/firm/email-settings/welcome/preview"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task OnlyAdministratorsSeeThePreviews()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var accountId = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        using var trader = await factory.LogInAsTraderAsync(accountId);
        using var nobody = factory.CreatePortalClient();

        using var asTrader = await trader.GetAsync(Url($"admin/firm/email-settings/{NotificationKinds.TraderFunded}/preview"), TestContext.Current.CancellationToken);
        using var loggedOut = await nobody.GetAsync(Url($"admin/firm/email-settings/{NotificationKinds.TraderFunded}/preview"), TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (asTrader.StatusCode, loggedOut.StatusCode));
    }

    private static Uri Url(string path) => new($"/api/portal/{path}", UriKind.Relative);

    private static async Task<JsonElement> PreviewAsync(HttpClient admin, string kind)
    {
        using var response = await admin.GetAsync(Url($"admin/firm/email-settings/{kind}/preview"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    // Saves a challenge from the template with its own id, name and size.
    private static async Task SaveChallengeAsync(HttpClient admin, string template, string id, string name, decimal size)
    {
        var templates = await admin.GetFromJsonAsync<JsonElement>(Url("admin/challenge-templates"), TestContext.Current.CancellationToken);
        var definition = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            templates.EnumerateArray().Single(t => t.GetProperty("id").GetString() == template).GetProperty("definition").GetRawText())!;
        definition["id"] = JsonSerializer.SerializeToElement(id);
        definition["name"] = JsonSerializer.SerializeToElement(name);
        definition["initialBalance"] = JsonSerializer.SerializeToElement(size);
        using var saved = await admin.PutAsJsonAsync(Url($"admin/challenges/{id}"), definition, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }
}
