using System.Net;
using System.Text.Json;

using Trading.Service.Tests.Support;

namespace Trading.Service.Tests;

/// <summary>The trader's own limits and lock through the trader's API, and the firm's trading day (ADR 0054).</summary>
public sealed class OwnLimitsApiTests
{
    private const string AccountId = "T1";
    private const string Account = $"/api/accounts/{AccountId}";
    private const string TradingDay = $"/api/admin/v1/accounts/{AccountId}/trading-day";

    [Fact]
    public async Task TheTraderSetsLimitsAndAStricterOneAppliesAtOnceAndALooserOneTomorrow()
    {
        using var factory = new ServiceFactory();
        using var client = await ReadyAsync(factory);
        await client.PutJsonAsync(TradingDay, new { timeZone = "Europe/Stockholm", startsAt = "00:00:00" });

        await client.PutJsonAsync($"{Account}/limits", new { dailyLoss = 1_500m, dailyTarget = 1_000m, maxTrades = 6 });
        var set = await client.PutJsonAsync($"{Account}/limits", new { dailyLoss = 2_000m, dailyTarget = 1_000m, maxTrades = (int?)null });
        var limits = (await client.GetJsonAsync(Account)).GetProperty("ownLimits");

        Assert.Equal("OwnLimitsSet", set.GetProperty("events")[0].GetProperty("event").GetProperty("kind").GetString());
        Assert.Equal((1_500m, 6), (limits.GetProperty("limits").GetProperty("dailyLoss").GetDecimal(), limits.GetProperty("limits").GetProperty("maxTrades").GetInt32()));
        Assert.Equal((2_000m, JsonValueKind.Null), (limits.GetProperty("pending").GetProperty("dailyLoss").GetDecimal(), limits.GetProperty("pending").GetProperty("maxTrades").ValueKind));
        Assert.Equal((98_500m, 101_000m), (limits.GetProperty("lossLevel").GetDecimal(), limits.GetProperty("targetLevel").GetDecimal()));
        Assert.Equal(("Europe/Stockholm", new DateTimeOffset(2026, 10, 5, 22, 0, 0, TimeSpan.Zero)), (limits.GetProperty("tradingDay").GetProperty("timeZone").GetString(), limits.GetProperty("nextDayStart").GetDateTimeOffset()));
        Assert.Equal(JsonValueKind.Null, limits.GetProperty("lock").ValueKind);
    }

    [Fact]
    public async Task TheTraderLocksTheRestOfTheDayAndARetryChangesNothing()
    {
        using var factory = new ServiceFactory();
        using var client = await ReadyAsync(factory);
        await client.PostJsonAsync($"{Account}/orders", new { orderId = "P1", symbol = "EURUSD", side = "Buy", type = "Market", volume = 1.00m });

        var locked = await client.PostJsonAsync($"{Account}/lock", new { closePositions = true });
        var again = await client.PostJsonAsync($"{Account}/lock", new { closePositions = true });
        var refused = await client.PostJsonAsync($"{Account}/orders", new { orderId = "P2", symbol = "EURUSD", side = "Buy", type = "Market", volume = 1.00m }, HttpStatusCode.UnprocessableEntity);
        var account = await client.GetJsonAsync(Account);

        Assert.Equal(
            ["PositionClosed", "TradingLocked"],
            locked.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("event").GetProperty("kind").GetString()));
        Assert.Empty(again.GetProperty("events").EnumerateArray());
        Assert.Equal("AccountLocked", refused.GetProperty("reason").GetString());
        Assert.Equal("Trader", account.GetProperty("ownLimits").GetProperty("lock").GetProperty("reason").GetString());
        Assert.Empty(account.GetProperty("positions").EnumerateArray());
    }

    [Fact]
    public async Task LimitsAndTheTradingDayAreChecked()
    {
        using var factory = new ServiceFactory(settings: SecondFirm.Settings);
        using var client = await ReadyAsync(factory);
        using var other = factory.CreateAdminClient(SecondFirm.ApiKey);

        var negative = await client.PutJsonAsync($"{Account}/limits", new { dailyLoss = -5m }, HttpStatusCode.UnprocessableEntity);
        var zone = await client.PutJsonAsync(TradingDay, new { timeZone = "Mars/Olympus", startsAt = "00:00:00" }, HttpStatusCode.UnprocessableEntity);
        await other.PutJsonAsync(TradingDay, new { timeZone = "Europe/Stockholm", startsAt = "00:00:00" }, HttpStatusCode.NotFound);

        Assert.Equal("InvalidLimits", negative.GetProperty("reason").GetString());
        Assert.Equal("InvalidTradingDay", zone.GetProperty("reason").GetString());
    }

    private static async Task<HttpClient> ReadyAsync(ServiceFactory factory)
    {
        var client = await factory.CreateTraderClientAsync(AccountId);
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        return client;
    }
}
