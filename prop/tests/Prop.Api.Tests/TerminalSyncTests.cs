using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Prop.Api.Firms;
using Prop.Api.Tests.Support;
using Prop.Api.Trading;

namespace Prop.Api.Tests;

/// <summary>
/// What the terminal shows the firms' traders (ADR 0058): the terminal profile of each firm that signed up, how its
/// orders start as the firm chooses, and the traders' names.
/// </summary>
public sealed class TerminalSyncTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task ASignedUpFirmsTradersLogInThroughItsPortalAndFindItsSupportAndTerms()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");
        await PropFactory.WaitUntilProvisionedAsync(admin);

        var first = await ProfileAsync(factory, "acme", _ => true);
        await SetTermsAsync(admin, "https://acme.test/terms");
        var withTerms = await ProfileAsync(factory, "acme", p => p.Links.Terms is not null);

        Assert.Equal((TerminalKind.Prop, TerminalModules.All, false), (first.Kind, first.Modules, first.PasswordLogin));
        Assert.Equal(new TerminalLinks(null, new Uri("http://acme.localhost:3002/support"), null, null, null), first.Links);
        Assert.Equal(new Uri("https://acme.test/terms"), withTerms.Links.Terms);
    }

    [Fact]
    public async Task TheFirmChoosesHowOrdersStartAndKeepsItWhenItsPortalChanges()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");
        await PropFactory.WaitUntilProvisionedAsync(admin);
        await ProfileAsync(factory, "acme", p => !p.PasswordLogin);

        var before = await admin.GetFromJsonAsync<JsonElement>(Url("admin/terminal"), TestContext.Current.CancellationToken);
        using var tooBig = await SaveAsync(admin, new { confirmOrders = true, startingSize = "Lots", startingValue = 5_000m });
        using var tooPrecise = await SaveAsync(admin, new { confirmOrders = true, startingSize = "Lots", startingValue = 0.105m });
        using var noShare = await SaveAsync(admin, new { confirmOrders = true, startingSize = "RiskOfRoom", startingValue = (decimal?)null });
        using var saved = await SaveAsync(admin, new { confirmOrders = true, startingSize = "Lots", startingValue = 0.1m });
        var after = await saved.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        await SetTermsAsync(admin, "https://acme.test/terms");
        var profile = await ProfileAsync(factory, "acme", p => p.Links.Terms is not null);

        Assert.Equal((false, "Smallest", JsonValueKind.Null), (before.GetProperty("confirmOrders").GetBoolean(), before.GetProperty("startingSize").GetString(), before.GetProperty("startingValue").ValueKind));
        Assert.Equal(
            [HttpStatusCode.UnprocessableEntity, HttpStatusCode.UnprocessableEntity, HttpStatusCode.UnprocessableEntity, HttpStatusCode.OK],
            [tooBig.StatusCode, tooPrecise.StatusCode, noShare.StatusCode, saved.StatusCode]);
        Assert.Equal((true, "Lots", 0.1m), (after.GetProperty("confirmOrders").GetBoolean(), after.GetProperty("startingSize").GetString(), after.GetProperty("startingValue").GetDecimal()));
        Assert.Equal((true, new TerminalStartingSize(StartingSizeKind.Lots, 0.1m), false), (profile.ConfirmOrders, profile.StartingSize, profile.PasswordLogin));
    }

    // A configured firm's terminal comes from the trading platform's configuration, as its server does.
    [Fact]
    public async Task AConfiguredFirmsTerminalIsLeftAsConfigured()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");
        await PropFactory.WaitUntilProvisionedAsync(admin);

        await ProfileAsync(factory, "acme", _ => true);

        Assert.Null(factory.Trading.ProfileOf("demo-firm"));
    }

    [Fact]
    public async Task TheTerminalShowsTheTradersNameAsThePortalDoes()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var id = (await factory.StartActiveAccountAsync("anna@test.example")).GetProperty("id").GetGuid();
        var userId = factory.Trading.UserIdOf("anna@test.example") ?? throw new InvalidOperationException("Anna has no user on the platform.");
        using var trader = await factory.LogInAsTraderAsync(id);
        using var admin = await factory.LogInAsAdminAsync();

        (await trader.PutAsJsonAsync(Url("me/name"), new { name = "Ana Berg" }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        await NameAsync(factory, userId, "Ana Berg");
        (await admin.PutAsJsonAsync(Url($"admin/accounts/{id}/trader/name"), new { name = "Anna Berg" }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        await NameAsync(factory, userId, "Anna Berg");
    }

    private static Uri Url(string path) => new($"/api/portal/{path}", UriKind.Relative);

    private static Task<HttpResponseMessage> SaveAsync(HttpClient admin, object settings) =>
        admin.PutAsJsonAsync(Url("admin/terminal"), settings, TestContext.Current.CancellationToken);

    private static async Task SetTermsAsync(HttpClient admin, string termsUrl)
    {
        using var response = await admin.PutAsJsonAsync(Url("admin/firm/payments"), new { provider = "Test", termsUrl }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // The profiles are looked at every few seconds on the test's clock.
    private static async Task<TerminalProfile> ProfileAsync(PropFactory factory, string server, Func<TerminalProfile, bool> condition)
    {
        TerminalProfile? profile = null;
        await Eventually.ThatAsync(
            () =>
            {
                factory.Time.Advance(TerminalSync.PollInterval);
                profile = factory.Trading.ProfileOf(server);
                return Task.FromResult(profile is not null && condition(profile));
            },
            $"the terminal profile of {server}");
        return profile!;
    }

    private static Task NameAsync(PropFactory factory, Guid userId, string name) =>
        Eventually.ThatAsync(
            () =>
            {
                factory.Time.Advance(TerminalSync.PollInterval);
                return Task.FromResult(factory.Trading.NameOf(userId) == (true, name));
            },
            $"the terminal to show {name}");
}
