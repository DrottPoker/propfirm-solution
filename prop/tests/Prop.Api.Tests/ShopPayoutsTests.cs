using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Prop.Api.Tests.Support;

using static Prop.Api.Tests.Support.TestAccounts;

namespace Prop.Api.Tests;

/// <summary>
/// What the firm paid out lately, in its shop: off until the firm turns it on, since it makes its own figures public, and
/// then the payouts of the last 30 days and how soon they were paid, once there are any.
/// </summary>
public sealed class ShopPayoutsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task TheShopShowsNoPayoutsUntilTheFirmTurnsItOn()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var anna = await FundedAsync(factory, "anna@test.example", 1001, 8_000m);
        await PayOutAsync(factory, anna);
        using var admin = await factory.LogInAsAdminAsync();

        var settings = await admin.GetFromJsonAsync<JsonElement>(Url("admin/firm"), TestContext.Current.CancellationToken);
        var shop = await ShopAsync(factory);

        Assert.False(settings.GetProperty("shopShowsPayouts").GetBoolean());
        Assert.Equal(JsonValueKind.Null, shop.GetProperty("payouts").ValueKind);
    }

    [Fact]
    public async Task TurnedOnTheShopShowsThePayoutsOfTheLast30DaysOnceThereAreAny()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.LogInAsAdminAsync();
        var turnedOn = await PutAsync(admin, true);
        var beforeAny = await ShopAsync(factory);

        var anna = await FundedAsync(factory, "anna@test.example", 1001, 8_000m);
        await factory.AdvanceAsync(TimeSpan.FromDays(2));
        await PayOutAsync(factory, anna);
        var afterOne = (await ShopAsync(factory)).GetProperty("payouts");
        await factory.AdvanceAsync(TimeSpan.FromDays(31));
        var monthLater = await ShopAsync(factory);

        // Logged in again, since a session does not last a month.
        using var later = await factory.LogInAsAdminAsync();
        var turnedOff = await PutAsync(later, false);

        Assert.True(turnedOn.GetProperty("shopShowsPayouts").GetBoolean());
        Assert.Equal(JsonValueKind.Null, beforeAny.GetProperty("payouts").ValueKind);
        Assert.Equal(1, afterOne.GetProperty("count").GetInt32());
        Assert.Equal([("USD", 6_400m)], afterOne.GetProperty("totals").EnumerateArray().Select(t => (t.GetProperty("currency").GetString(), t.GetProperty("amount").GetDecimal())));
        Assert.Equal(JsonValueKind.Number, afterOne.GetProperty("averageDaysToPay").ValueKind);
        Assert.Equal(JsonValueKind.Null, monthLater.GetProperty("payouts").ValueKind);
        Assert.False(turnedOff.GetProperty("shopShowsPayouts").GetBoolean());
    }

    [Fact]
    public async Task OnlyTheFirmsAdministratorsTurnItOn()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var account = (await factory.StartActiveAccountAsync("anna@test.example", QuickTest)).GetProperty("id").GetGuid();
        using var trader = await factory.LogInAsTraderAsync(account);
        using var visitor = factory.CreatePortalClient();

        using var byTrader = await trader.PutAsJsonAsync(Url("admin/firm/shop-payouts"), new { show = true }, TestContext.Current.CancellationToken);
        using var byVisitor = await visitor.PutAsJsonAsync(Url("admin/firm/shop-payouts"), new { show = true }, TestContext.Current.CancellationToken);
        using var admin = await factory.LogInAsAdminAsync();
        var settings = await admin.GetFromJsonAsync<JsonElement>(Url("admin/firm"), TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (byTrader.StatusCode, byVisitor.StatusCode));
        Assert.False(settings.GetProperty("shopShowsPayouts").GetBoolean());
    }

    private static Uri Url(string path) => new($"/api/portal/{path}", UriKind.Relative);

    private static async Task<JsonElement> ShopAsync(PropFactory factory)
    {
        using var visitor = factory.CreatePortalClient();
        return await visitor.GetFromJsonAsync<JsonElement>(Url("shop"), TestContext.Current.CancellationToken);
    }

    private static async Task<JsonElement> PutAsync(HttpClient admin, bool show)
    {
        using var response = await admin.PutAsJsonAsync(Url("admin/firm/shop-payouts"), new { show }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    /// <summary>The trader asks for a payout, and the firm approves it and marks it as paid.</summary>
    private static async Task PayOutAsync(PropFactory factory, Guid accountId)
    {
        var payoutId = await RequestPayoutAsync(factory, accountId);
        using var firm = factory.CreateFirmClient();
        await firm.PostJsonAsync($"payouts/{payoutId}/approve", null);
        await firm.PostJsonAsync($"payouts/{payoutId}/mark-paid", new { reference = "wire-1" });
    }
}
