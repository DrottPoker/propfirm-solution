using System.Net;
using System.Text.Json;

using Trading.Service.Tests.Support;

namespace Trading.Service.Tests;

/// <summary>The order tools through the trader's API: partial close, close all, modified orders and trailing stops (ADR 0051).</summary>
public sealed class OrderToolsApiTests
{
    private const string AccountId = "T1";
    private const string Orders = $"/api/accounts/{AccountId}/orders";
    private const string Positions = $"/api/accounts/{AccountId}/positions";

    [Fact]
    public async Task PartOfAPositionIsClosedAndAnEmptyBodyClosesTheRest()
    {
        using var factory = new ServiceFactory();
        using var client = await ReadyAsync(factory);
        await client.PostJsonAsync(Orders, Market("P1", 1.00m));

        var part = await client.PostJsonAsync($"{Positions}/P1/close", new { volume = 0.40m });
        var refused = await client.PostJsonAsync($"{Positions}/P1/close", new { volume = 0.005m }, HttpStatusCode.UnprocessableEntity);
        var rest = await client.PostJsonAsync($"{Positions}/P1/close");

        var partEvent = part.GetProperty("events")[0].GetProperty("event");
        Assert.Equal(("PositionPartiallyClosed", 0.40m, 0.60m), (partEvent.GetProperty("kind").GetString(), partEvent.GetProperty("volume").GetDecimal(), partEvent.GetProperty("remainingVolume").GetDecimal()));
        Assert.Equal("InvalidVolume", refused.GetProperty("reason").GetString());
        Assert.Equal(0.60m, rest.GetProperty("events")[0].GetProperty("event").GetProperty("volume").GetDecimal());
    }

    [Fact]
    public async Task EveryPositionOrOneSymbolsAreClosedAtOnce()
    {
        using var factory = new ServiceFactory();
        using var client = await ReadyAsync(factory);
        await factory.PushQuoteAsync("XAUUSD", 2650.00m, 2650.30m);
        await client.PostJsonAsync(Orders, Market("P1", 1.00m));
        await client.PostJsonAsync(Orders, Market("P2", 0.10m, "XAUUSD"));
        await client.PostJsonAsync(Orders, Market("P3", 1.00m));

        var gold = await client.PostJsonAsync($"{Positions}/close-all", new { symbol = "XAUUSD" });
        var all = await client.PostJsonAsync($"{Positions}/close-all");
        var none = await client.PostJsonAsync($"{Positions}/close-all", expected: HttpStatusCode.NotFound);

        Assert.Equal(["P2"], ClosedIds(gold));
        Assert.Equal(["P1", "P3"], ClosedIds(all));
        Assert.Equal("UnknownPosition", none.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task APendingOrderIsMoved()
    {
        using var factory = new ServiceFactory();
        using var client = await ReadyAsync(factory);
        await client.PostJsonAsync(Orders, new { orderId = "O1", symbol = "EURUSD", side = "Buy", type = "Limit", volume = 1.00m, price = 1.07000m });

        var moved = await client.PutJsonAsync($"{Orders}/O1", new { price = 1.07500m, stopLoss = 1.07000m, takeProfit = (decimal?)null });
        var refused = await client.PutJsonAsync($"{Orders}/O1", new { price = 1.09000m, stopLoss = (decimal?)null, takeProfit = (decimal?)null }, HttpStatusCode.UnprocessableEntity);
        var order = (await client.GetJsonAsync($"/api/accounts/{AccountId}")).GetProperty("orders")[0];

        Assert.Equal(["OrderModified"], moved.GetProperty("events").EventKinds());
        Assert.Equal("InvalidPrice", refused.GetProperty("reason").GetString());
        Assert.Equal((1.07500m, 1.07000m), (order.GetProperty("price").GetDecimal(), order.GetProperty("stopLoss").GetDecimal()));
    }

    [Fact]
    public async Task ATrailingStopIsSetWithTheOrderAndTheStops()
    {
        using var factory = new ServiceFactory();
        using var client = await ReadyAsync(factory);

        var opened = await client.PostJsonAsync(Orders, new { orderId = "P1", symbol = "EURUSD", side = "Buy", type = "Market", volume = 1.00m, stopLoss = 1.07900m, trailingStop = true });
        await factory.PushQuoteAsync("EURUSD", 1.08300m, 1.08310m);
        var trailed = (await client.GetJsonAsync($"/api/accounts/{AccountId}")).GetProperty("positions")[0];
        var off = await client.PutJsonAsync($"{Positions}/P1/stops", new { stopLoss = 1.08200m, takeProfit = (decimal?)null, trailingStop = false });

        // The group lowers the bid by a point, so the stop loss trails 9.9 pips behind the bid the account sees.
        Assert.Equal(0.00099m, opened.GetProperty("events")[0].GetProperty("event").GetProperty("trailingDistance").GetDecimal());
        Assert.Equal((1.08200m, 0.00099m), (trailed.GetProperty("stopLoss").GetDecimal(), trailed.GetProperty("trailingDistance").GetDecimal()));
        Assert.Equal(JsonValueKind.Null, off.GetProperty("events")[0].GetProperty("event").GetProperty("trailingDistance").ValueKind);
    }

    private static async Task<HttpClient> ReadyAsync(ServiceFactory factory)
    {
        var client = await factory.CreateTraderClientAsync(AccountId);
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        return client;
    }

    private static object Market(string orderId, decimal volume, string symbol = "EURUSD") =>
        new { orderId, symbol, side = "Buy", type = "Market", volume };

    private static List<string?> ClosedIds(JsonElement response) =>
        [.. response.GetProperty("events").EnumerateArray()
            .Select(e => e.GetProperty("event"))
            .Where(e => e.GetProperty("kind").GetString() == "PositionClosed")
            .Select(e => e.GetProperty("positionId").GetString())];
}
