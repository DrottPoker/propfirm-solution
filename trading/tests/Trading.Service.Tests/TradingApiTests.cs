using System.Net;

using Trading.Service.Tests.Support;

namespace Trading.Service.Tests;

public sealed class TradingApiTests
{
    private const string AccountId = "T1";
    private const string Orders = $"/api/accounts/{AccountId}/orders";

    [Fact]
    public async Task MarketOrderFillsAtTheAskTheAccountSees()
    {
        using var factory = new ServiceFactory();
        using var client = await factory.CreateTraderClientAsync(AccountId);
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        var ask = (await client.PriceAsync(AccountId, "EURUSD")).GetProperty("ask").GetDecimal();

        var response = await client.PostJsonAsync(Orders, MarketBuy("O1"));

        var opened = response.GetProperty("events")[0].GetProperty("event");
        Assert.Equal("PositionOpened", opened.GetProperty("kind").GetString());
        Assert.Equal(ask, opened.GetProperty("openPrice").GetDecimal());
        var account = await client.GetJsonAsync($"/api/accounts/{AccountId}");
        Assert.Equal("O1", account.GetProperty("positions")[0].GetProperty("positionId").GetString());
    }

    [Fact]
    public async Task EventsKeepTheAccountHistoryInOrder()
    {
        using var factory = new ServiceFactory();
        using var client = await factory.CreateTraderClientAsync(AccountId);
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        await client.PostJsonAsync(Orders, MarketBuy("O1"));
        await client.PostJsonAsync($"/api/accounts/{AccountId}/positions/O1/close");

        var events = await client.GetJsonAsync($"/api/accounts/{AccountId}/events");

        Assert.Equal(["AccountCreated", "PositionOpened", "PositionClosed"], events.EventKinds());
        var sequences = events.EnumerateArray().Select(e => e.GetProperty("sequence").GetInt64()).ToList();
        Assert.Equal(sequences.Order(), sequences);

        var afterFirst = await client.GetJsonAsync($"/api/accounts/{AccountId}/events?after={sequences[0]}");
        Assert.Equal(2, afterFirst.GetArrayLength());
    }

    [Fact]
    public async Task PendingOrdersCanBeCancelledAndStopsModified()
    {
        using var factory = new ServiceFactory();
        using var client = await factory.CreateTraderClientAsync(AccountId);
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);

        var placed = await client.PostJsonAsync(Orders, new { orderId = "L1", symbol = "EURUSD", side = "Buy", type = "Limit", volume = 1.00m, price = 1.07000m });
        var cancelled = await client.DeleteJsonAsync($"{Orders}/L1");
        await client.PostJsonAsync(Orders, MarketBuy("O1"));
        var modified = await client.PutJsonAsync($"/api/accounts/{AccountId}/positions/O1/stops", new { stopLoss = 1.07000m, takeProfit = (decimal?)null });

        Assert.Equal(["OrderPlaced"], placed.GetProperty("events").EventKinds());
        Assert.Equal(["OrderCancelled"], cancelled.GetProperty("events").EventKinds());
        Assert.Equal(["PositionModified"], modified.GetProperty("events").EventKinds());
    }

    [Fact]
    public async Task RejectionsUseMatchingStatusCodesAndGiveTheReason()
    {
        using var factory = new ServiceFactory();
        using var client = await factory.CreateTraderClientAsync(AccountId);

        var noPrice = await client.PostJsonAsync(Orders, MarketBuy("O1"), HttpStatusCode.UnprocessableEntity);
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        await client.PostJsonAsync(Orders, MarketBuy("O1"));
        var duplicate = await client.PostJsonAsync(Orders, MarketBuy("O1"), HttpStatusCode.Conflict);

        Assert.Equal("NoPrice", noPrice.GetProperty("reason").GetString());
        Assert.Equal("DuplicateId", duplicate.GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData("/api/accounts/missing")]
    [InlineData("/api/accounts/missing/prices")]
    [InlineData("/api/accounts/missing/instruments")]
    [InlineData("/api/accounts/missing/events")]
    [InlineData("/api/accounts/missing/candles/EURUSD?timeframe=M1")]
    [InlineData("/api/accounts/missing/instruments/EURUSD/point-value")]
    public async Task ReadsForAccountsTheTraderDoesNotOwnReturnNotFound(string url)
    {
        using var factory = new ServiceFactory();
        using var client = await factory.CreateTraderClientAsync(AccountId);

        using var response = await client.GetAsync(new Uri(url, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FloorBreachLiquidatesAndDisablesTheAccount()
    {
        using var factory = new ServiceFactory();
        using var client = await factory.CreateTraderClientAsync(AccountId);
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        await client.PostJsonAsync(Orders, new { orderId = "O1", symbol = "EURUSD", side = "Buy", type = "Market", volume = 10.00m });

        // Spread and commission put equity below the starting balance, so this floor is already breached
        var response = await client.PutJsonAsync(
            $"/api/admin/v1/accounts/{AccountId}/floors/max-loss",
            new { rule = new { kind = "FixedFloor", level = 100_000m } });

        Assert.Equal(
            ["EquityFloorSet", "EquityFloorBreached", "PositionClosed", "AccountDisabled"],
            response.GetProperty("events").EventKinds());
        var account = await client.GetJsonAsync($"/api/accounts/{AccountId}");
        Assert.Equal("Disabled", account.GetProperty("status").GetString());
    }

    [Fact]
    public async Task InstrumentsShowTheTradingConditionsOfTheGroup()
    {
        using var factory = new ServiceFactory();
        using var client = await factory.CreateTraderClientAsync(AccountId);

        var instruments = await client.GetJsonAsync($"/api/accounts/{AccountId}/instruments");

        var eurUsd = instruments.EnumerateArray().Single(i => i.GetProperty("symbol").GetString() == "EURUSD");
        Assert.True(eurUsd.GetProperty("leverage").GetInt32() > 0);
        Assert.True(eurUsd.GetProperty("spreadMarkupPoints").GetInt32() >= 0);
        Assert.True(eurUsd.GetProperty("commissionPerLotPerSide").GetDecimal() >= 0m);
        Assert.Equal(5, eurUsd.GetProperty("digits").GetInt32());
    }

    [Fact]
    public async Task PointValueIsInTheAccountCurrencyOnceThereIsAConversionRate()
    {
        using var factory = new ServiceFactory();
        using var client = await factory.CreateTraderClientAsync(AccountId);
        var url = $"/api/accounts/{AccountId}/instruments/USDJPY/point-value";

        // JPY needs a USDJPY price to become USD.
        using (var before = await client.GetAsync(new Uri(url, UriKind.Relative), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, before.StatusCode);
        }

        await factory.PushQuoteAsync("USDJPY", 150.000m, 150.020m);
        var usdJpy = await client.GetJsonAsync(url);
        var eurUsd = await client.GetJsonAsync($"/api/accounts/{AccountId}/instruments/EURUSD/point-value");

        Assert.Equal("USD", usdJpy.GetProperty("currency").GetString());
        // 100 000 x 0.001 JPY at the mid rate 150.010
        Assert.Equal(0.6666m, Math.Round(usdJpy.GetProperty("perLot").GetDecimal(), 4));
        Assert.Equal(1m, eurUsd.GetProperty("perLot").GetDecimal());
    }

    [Fact]
    public async Task CandlesUseTheBidTheAccountSees()
    {
        using var factory = new ServiceFactory();
        using var client = await factory.CreateTraderClientAsync(AccountId);
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        var bid = (await client.PriceAsync(AccountId, "EURUSD")).GetProperty("bid").GetDecimal();

        var candles = await client.GetJsonAsync($"/api/accounts/{AccountId}/candles/EURUSD?timeframe=M1&count=10");

        var candle = Assert.Single(candles.EnumerateArray());
        Assert.Equal(bid, candle.GetProperty("close").GetDecimal());
        Assert.Equal(factory.Time.GetUtcNow(), candle.GetProperty("time").GetDateTimeOffset());
    }

    private static object MarketBuy(string orderId) =>
        new { orderId, symbol = "EURUSD", side = "Buy", type = "Market", volume = 1.00m };
}
