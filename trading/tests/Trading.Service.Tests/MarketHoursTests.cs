using System.Net;
using System.Text.Json;

using Trading.Engine;
using Trading.Service.Configuration;
using Trading.Service.Feeds;
using Trading.Service.Tests.Support;

namespace Trading.Service.Tests;

/// <summary>Trading hours from the configuration, through the trader's API (ADR 0050).</summary>
public sealed class MarketHoursTests
{
    private const string AccountId = "T1";
    private const string Hours = $"/api/accounts/{AccountId}/market-hours";

    // Saturday 10 October 2026, when the forex week is closed.
    private static readonly DateTimeOffset Saturday = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TheTraderSeesWhenEachMarketIsOpen()
    {
        // The test clock starts on Monday 5 October 2026 at 08:00 UTC.
        using var factory = new ServiceFactory();
        using var client = await factory.CreateTraderClientAsync(AccountId);

        var hours = await client.GetJsonAsync(Hours);

        Assert.Equal(23, hours.GetArrayLength());
        var eurusd = Of(hours, "EURUSD");
        Assert.True(eurusd.GetProperty("isOpen").GetBoolean());
        Assert.Equal(Utc(2026, 10, 9, 21), eurusd.GetProperty("nextChange").GetDateTimeOffset());
        Assert.Equal(
            [(Utc(2026, 10, 4, 21), Utc(2026, 10, 9, 21)), (Utc(2026, 10, 11, 21), Utc(2026, 10, 16, 21))],
            eurusd.GetProperty("sessions").EnumerateArray().Select(s => (s.GetProperty("opens").GetDateTimeOffset(), s.GetProperty("closes").GetDateTimeOffset())));

        // 22:00 in Berlin, and the next day's session after it.
        var dax = Of(hours, "DE40");
        Assert.Equal(Utc(2026, 10, 5, 20), dax.GetProperty("nextChange").GetDateTimeOffset());
        Assert.Equal(6, dax.GetProperty("sessions").GetArrayLength());

        var bitcoin = Of(hours, "BTCUSD");
        Assert.True(bitcoin.GetProperty("isOpen").GetBoolean());
        Assert.Equal(JsonValueKind.Null, bitcoin.GetProperty("nextChange").ValueKind);
        Assert.Equal(JsonValueKind.Null, bitcoin.GetProperty("sessions").ValueKind);
    }

    [Fact]
    public async Task OrdersAreRefusedWhileTheMarketIsClosed()
    {
        using var factory = new ServiceFactory();
        factory.Time.SetUtcNow(Saturday);
        using var client = await factory.CreateTraderClientAsync(AccountId);
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        await factory.PushQuoteAsync("BTCUSD", 65000.00m, 65030.00m);

        var refused = await client.PostJsonAsync($"/api/accounts/{AccountId}/orders", MarketBuy("O1", "EURUSD"), HttpStatusCode.UnprocessableEntity);
        var bought = await client.PostJsonAsync($"/api/accounts/{AccountId}/orders", MarketBuy("O2", "BTCUSD"));
        var eurusd = Of(await client.GetJsonAsync(Hours), "EURUSD");

        Assert.Equal("MarketClosed", refused.GetProperty("reason").GetString());
        Assert.Equal(["PositionOpened"], bought.GetProperty("events").EventKinds());
        Assert.False(eurusd.GetProperty("isOpen").GetBoolean());
        Assert.Equal(Utc(2026, 10, 11, 21), eurusd.GetProperty("nextChange").GetDateTimeOffset());
    }

    // Capital.com quotes UK100 after ICE in London closes at 21:00 there, and pauses only five minutes a day.
    [Fact]
    public async Task AFeedQuotesInItsOwnHours()
    {
        var evening = new DateTimeOffset(2026, 10, 5, 20, 30, 0, TimeSpan.Zero);
        using var capital = new ServiceFactory(feed: new ManualPriceFeed(PriceFeedOptions.CapitalComProvider));
        capital.Time.SetUtcNow(evening);
        using var exchange = new ServiceFactory();
        exchange.Time.SetUtcNow(evening);
        using var capitalClient = await capital.CreateTraderClientAsync(AccountId);
        using var exchangeClient = await exchange.CreateTraderClientAsync(AccountId);

        var capitalHours = await capitalClient.GetJsonAsync(Hours);
        var exchangeHours = await exchangeClient.GetJsonAsync(Hours);

        var uk100 = Of(capitalHours, "UK100");
        Assert.True(uk100.GetProperty("isOpen").GetBoolean());
        Assert.Equal(Utc(2026, 10, 5, 21), uk100.GetProperty("nextChange").GetDateTimeOffset());
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 21, 5, 0, TimeSpan.Zero), uk100.GetProperty("sessions")[1].GetProperty("opens").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Array, Of(capitalHours, "BTCUSD").GetProperty("sessions").ValueKind);
        Assert.False(Of(exchangeHours, "UK100").GetProperty("isOpen").GetBoolean());
        Assert.Equal(JsonValueKind.Null, Of(exchangeHours, "BTCUSD").GetProperty("sessions").ValueKind);
    }

    [Fact]
    public async Task AFeedCanQuoteASymbolAroundTheClock()
    {
        using var factory = new ServiceFactory(
            settings: new Dictionary<string, string> { ["Trading:TradingHoursByFeed:CapitalCom:UK100"] = "" },
            feed: new ManualPriceFeed(PriceFeedOptions.CapitalComProvider));
        factory.Time.SetUtcNow(Saturday);
        using var client = await factory.CreateTraderClientAsync(AccountId);

        var uk100 = Of(await client.GetJsonAsync(Hours), "UK100");

        Assert.True(uk100.GetProperty("isOpen").GetBoolean());
        Assert.Equal(JsonValueKind.Null, uk100.GetProperty("sessions").ValueKind);
    }

    [Fact]
    public async Task MadeUpPricesAreAlwaysOpen()
    {
        using var factory = new ServiceFactory(feed: new ManualPriceFeed { FollowsTradingHours = false });
        factory.Time.SetUtcNow(Saturday);
        using var client = await factory.CreateTraderClientAsync(AccountId);
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);

        var bought = await client.PostJsonAsync($"/api/accounts/{AccountId}/orders", MarketBuy("O1", "EURUSD"));
        var hours = await client.GetJsonAsync(Hours);

        Assert.Equal(["PositionOpened"], bought.GetProperty("events").EventKinds());
        Assert.All(hours.EnumerateArray(), h => Assert.True(h.GetProperty("isOpen").GetBoolean()));
        Assert.All(hours.EnumerateArray(), h => Assert.Equal(JsonValueKind.Null, h.GetProperty("sessions").ValueKind));
    }

    // Wrong hours stop the start whatever the feed, so a mistake shows before a real feed is used.
    [Theory]
    [InlineData("Trading:Instruments:0:TradingHours", "Nope", "refers to trading hours 'Nope'")]
    [InlineData("Trading:TradingHours:Forex:Sessions:0", "Sunday 17:00 - Fri 17:00", "is not like 'Sun 17:00 - Fri 17:00'")]
    [InlineData("Trading:TradingHours:Forex:Sessions:0", "Sun 25:00 - Fri 17:00", "a time that does not exist")]
    [InlineData("Trading:TradingHours:Forex:Closures:0", "25 December", "is not like '2026-12-25'")]
    [InlineData("Trading:TradingHours:Forex:TimeZone", "Mars/Olympus", "Trading:TradingHours:Forex: time zone 'Mars/Olympus' is unknown")]
    [InlineData("Trading:TradingHoursByFeed:Capitalcom:UK100", "Forex", "'Capitalcom' is not a price feed")]
    [InlineData("Trading:TradingHoursByFeed:CapitalCom:FTSE", "Forex", "'FTSE' is not an instrument")]
    [InlineData("Trading:TradingHoursByFeed:CapitalCom:UK100", "Nope", "UK100 refers to trading hours 'Nope'")]
    public void WrongTradingHoursStopTheStart(string key, string value, string expected)
    {
        using var factory = new ServiceFactory(settings: new Dictionary<string, string> { [key] = value }, feed: new ManualPriceFeed { FollowsTradingHours = false });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(expected, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ClosureCanBeAWholeDayOrAPart()
    {
        var options = new TradingHoursOptions
        {
            TimeZone = "America/New_York",
            Sessions = ["Sun 17:00 - Fri 17:00"],
            Closures = ["2026-12-25", "2026-12-24 13:15 - 2026-12-27 18:00"],
        };

        var hours = options.ToTradingHours();

        Assert.Equal([new(new DateTime(2026, 12, 25), new DateTime(2026, 12, 26)), new TradingClosure(new DateTime(2026, 12, 24, 13, 15, 0), new DateTime(2026, 12, 27, 18, 0, 0))], hours.Closures!);
        Assert.Equal(new TradingSession(DayOfWeek.Sunday, new TimeOnly(17, 0), DayOfWeek.Friday, new TimeOnly(17, 0)), Assert.Single(hours.Sessions));
    }

    private static JsonElement Of(JsonElement hours, string symbol) =>
        hours.EnumerateArray().Single(h => h.GetProperty("symbol").GetString() == symbol);

    private static object MarketBuy(string orderId, string symbol) => new { orderId, symbol, side = "Buy", type = "Market", volume = 0.01m };

    private static DateTimeOffset Utc(int year, int month, int day, int hour) => new(year, month, day, hour, 0, 0, TimeSpan.Zero);
}
