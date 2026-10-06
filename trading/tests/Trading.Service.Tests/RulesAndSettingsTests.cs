using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.SignalR.Client;

using Trading.Service.Identity;
using Trading.Service.Tests.Support;

namespace Trading.Service.Tests;

/// <summary>The account's rules from the firm's system, and the trader's settings kept on their login (ADR 0052).</summary>
public sealed class RulesAndSettingsTests
{
    private const string AccountId = "T1";
    private const string Rules = $"/api/admin/v1/accounts/{AccountId}/rules";
    private const string Settings = "/api/me/settings";

    [Fact]
    public async Task TheFirmTellsTheRulesAndTheTraderSeesThemAtOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new ServiceFactory();
        using var client = await factory.CreateTraderClientAsync(AccountId);
        var before = await client.GetJsonAsync($"/api/accounts/{AccountId}/rules");

        await using var connection = factory.CreateHubConnection(await factory.LoginCookieAsync(ServiceFactory.EmailOf(AccountId), ServiceFactory.TraderPassword));
        var pushed = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<JsonElement>("Rules", rules => pushed.TrySetResult(rules));
        await connection.StartAsync(cancellationToken);
        await connection.InvokeAsync("Subscribe", AccountId, cancellationToken);

        var passBy = new DateTimeOffset(2026, 10, 30, 22, 0, 0, TimeSpan.Zero);
        await client.PutJsonAsync(Rules, new { funded = false, tradingDaysRequired = 4, tradingDaysCounted = 1, passBy, openPositionBy = passBy.AddDays(-20) });
        var after = await client.GetJsonAsync($"/api/accounts/{AccountId}/rules");

        Assert.Equal(JsonValueKind.Null, before.GetProperty("tradingDaysRequired").ValueKind);
        Assert.Equal((4, 1), (after.GetProperty("tradingDaysRequired").GetInt32(), after.GetProperty("tradingDaysCounted").GetInt32()));
        Assert.Equal(passBy, after.GetProperty("passBy").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("consistencyPercent").ValueKind);
        Assert.Equal(4, (await Eventually.Within(pushed.Task)).GetProperty("tradingDaysRequired").GetInt32());
    }

    [Fact]
    public async Task RulesAreCheckedAndOnlyTheFirmsOwn()
    {
        using var factory = new ServiceFactory(settings: SecondFirm.Settings);
        using var client = await factory.CreateTraderClientAsync(AccountId);
        using var other = factory.CreateAdminClient(SecondFirm.ApiKey);

        var negative = await client.PutJsonAsync(Rules, new { tradingDaysCounted = -1 }, HttpStatusCode.UnprocessableEntity);
        var consistency = await client.PutJsonAsync(Rules, new { consistencyPercent = 120 }, HttpStatusCode.UnprocessableEntity);
        await other.PutJsonAsync(Rules, new { tradingDaysRequired = 4 }, HttpStatusCode.NotFound);

        Assert.Contains("negative", negative.GetProperty("title").GetString(), StringComparison.Ordinal);
        Assert.Contains("consistency", consistency.GetProperty("title").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingsFollowTheTraderAndNobodyElse()
    {
        using var factory = new ServiceFactory();
        using var trader = await factory.CreateTraderClientAsync(AccountId);
        using var other = await factory.CreateTraderClientAsync("T2");

        await trader.PutJsonAsync($"{Settings}/trading.favorites", "[\"EURUSD\",\"XAUUSD\"]", HttpStatusCode.NoContent);
        await trader.PutJsonAsync($"{Settings}/trading.chartVolume", "off", HttpStatusCode.NoContent);
        await trader.PutJsonAsync($"{Settings}/trading.chartVolume", "on", HttpStatusCode.NoContent);
        await trader.PutJsonAsync($"{Settings}/trading.fillSound", "on", HttpStatusCode.NoContent);
        await trader.SendJsonAsync(HttpMethod.Delete, $"{Settings}/trading.fillSound", null, HttpStatusCode.NoContent);

        // A fresh login, as on another device.
        using var again = await factory.LoginAsync(ServiceFactory.EmailOf(AccountId), ServiceFactory.TraderPassword);
        var settings = (await again.GetJsonAsync(Settings)).GetProperty("settings");
        var others = (await other.GetJsonAsync(Settings)).GetProperty("settings");

        Assert.Equal(["trading.chartVolume", "trading.favorites"], settings.EnumerateObject().Select(s => s.Name).Order(StringComparer.Ordinal));
        Assert.Equal("[\"EURUSD\",\"XAUUSD\"]", settings.GetProperty("trading.favorites").GetString());
        Assert.Equal("on", settings.GetProperty("trading.chartVolume").GetString());
        Assert.Empty(others.EnumerateObject());
    }

    [Fact]
    public async Task SettingsHaveLimits()
    {
        using var factory = new ServiceFactory();
        using var trader = await factory.CreateTraderClientAsync(AccountId);
        using var anonymous = factory.CreateClient();

        await trader.PutJsonAsync($"{Settings}/no spaces", "x", HttpStatusCode.UnprocessableEntity);
        await trader.PutJsonAsync($"{Settings}/trading.big", new string('x', SettingsEndpoints.MaxValueLength), HttpStatusCode.RequestEntityTooLarge);
        for (var i = 0; i < SettingsEndpoints.MaxSettings; i++)
        {
            await trader.PutJsonAsync($"{Settings}/trading.key{i}", i, HttpStatusCode.NoContent);
        }

        await trader.PutJsonAsync($"{Settings}/trading.oneMore", 1, HttpStatusCode.UnprocessableEntity);
        await trader.PutJsonAsync($"{Settings}/trading.key0", 99, HttpStatusCode.NoContent);
        using var response = await anonymous.GetAsync(new Uri(Settings, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
