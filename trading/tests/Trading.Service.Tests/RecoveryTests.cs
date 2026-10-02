using Trading.Service.Tests.Support;

namespace Trading.Service.Tests;

/// <summary>Restarts of the whole service on the same journal.</summary>
public sealed class RecoveryTests
{
    private const string AccountId = "T1";

    [Fact]
    public async Task StateSurvivesARestart()
    {
        var journal = new InMemoryJournal();
        long lastSequence;
        using (var first = new ServiceFactory(journal))
        {
            using var client = first.CreateClient();
            await TradeAsync(first, client);
            lastSequence = (await client.GetJsonAsync($"/api/accounts/{AccountId}/events")).EnumerateArray().Last().GetProperty("sequence").GetInt64();
        }

        using var second = new ServiceFactory(journal);
        using var restarted = second.CreateClient();

        var account = await restarted.GetJsonAsync($"/api/accounts/{AccountId}");
        Assert.Equal("O1", account.GetProperty("positions")[0].GetProperty("positionId").GetString());
        Assert.Equal("max-loss", account.GetProperty("floors")[0].GetProperty("floorId").GetString());
        Assert.Equal(
            ["AccountCreated", "PositionOpened", "EquityFloorSet"],
            (await restarted.GetJsonAsync($"/api/accounts/{AccountId}/events")).EventKinds());

        // Sequence numbers continue where they stopped.
        await second.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        var closed = await restarted.PostJsonAsync($"/api/accounts/{AccountId}/positions/O1/close");
        Assert.Equal(lastSequence + 1, closed.GetProperty("events")[0].GetProperty("sequence").GetInt64());
    }

    [Fact]
    public async Task StateSurvivesACrash()
    {
        using var first = new ServiceFactory();
        using var client = first.CreateClient();
        await TradeAsync(first, client);
        var before = await client.GetJsonAsync($"/api/accounts/{AccountId}");

        // Everything a client got an answer for is stored, so a copy now is what a crash leaves behind.
        using var second = new ServiceFactory(first.Journal.Clone());
        using var restarted = second.CreateClient();

        var after = await restarted.GetJsonAsync($"/api/accounts/{AccountId}");
        Assert.Equal(before.GetProperty("balance").GetDecimal(), after.GetProperty("balance").GetDecimal());
        Assert.Equal(before.GetProperty("positions").GetRawText(), after.GetProperty("positions").GetRawText());
    }

    [Fact]
    public async Task DevelopmentAccountIsNotRecreated()
    {
        var journal = new InMemoryJournal();
        using (var first = new ServiceFactory(journal))
        {
            using var client = first.CreateClient();
            await first.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
            await client.PostJsonAsync("/api/accounts/demo/orders", new { orderId = "D1", symbol = "EURUSD", side = "Buy", type = "Market", volume = 1.00m });
        }

        using var second = new ServiceFactory(journal);
        using var restarted = second.CreateClient();

        var demo = await restarted.GetJsonAsync("/api/accounts/demo");
        Assert.Equal("D1", demo.GetProperty("positions")[0].GetProperty("positionId").GetString());
    }

    [Fact]
    public async Task ChartsAreRebuiltFromRecordedPrices()
    {
        var journal = new InMemoryJournal();
        using (var first = new ServiceFactory(journal))
        {
            first.CreateClient().Dispose();
            await first.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        }

        using var second = new ServiceFactory(journal);
        using var restarted = second.CreateClient();
        await restarted.CreateAccountAsync(AccountId);

        await Eventually.ThatAsync(
            async () => (await restarted.GetJsonAsync($"/api/accounts/{AccountId}/candles/EURUSD?timeframe=M1")).GetArrayLength() == 1,
            "the charts to be rebuilt");
    }

    [Fact]
    public async Task DamagedJournalStopsTheStart()
    {
        using var first = new ServiceFactory();
        using var client = first.CreateClient();
        await TradeAsync(first, client);
        var damaged = first.Journal.Clone();
        damaged.RemoveLastEvent();

        using var second = new ServiceFactory(damaged);
        var exception = Assert.ThrowsAny<Exception>(() => second.CreateClient());

        Assert.Contains("not deterministic or the journal is damaged", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangedConfigurationWithInputsToReplayStopsTheStart()
    {
        using var first = new ServiceFactory();
        using var client = first.CreateClient();
        await TradeAsync(first, client);

        using var second = new ServiceFactory(first.Journal.Clone(), ChangedMarkup);
        var exception = Assert.ThrowsAny<Exception>(() => second.CreateClient());

        Assert.Contains("configuration has changed", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangedConfigurationAfterACleanStopIsAccepted()
    {
        var journal = new InMemoryJournal();
        using (var first = new ServiceFactory(journal))
        {
            using var client = first.CreateClient();
            await TradeAsync(first, client);
        }

        using var second = new ServiceFactory(journal, ChangedMarkup);
        using var restarted = second.CreateClient();

        var instruments = await restarted.GetJsonAsync($"/api/accounts/{AccountId}/instruments");
        Assert.Equal(5, instruments.EnumerateArray().First(i => i.GetProperty("symbol").GetString() == "EURUSD").GetProperty("spreadMarkupPoints").GetInt32());
    }

    private static readonly Dictionary<string, string> ChangedMarkup = new() { ["Trading:Groups:0:Symbols:0:SpreadMarkupPoints"] = "5" };

    private static async Task TradeAsync(ServiceFactory factory, HttpClient client)
    {
        await client.CreateAccountAsync(AccountId);
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        await client.PostJsonAsync($"/api/accounts/{AccountId}/orders", new { orderId = "O1", symbol = "EURUSD", side = "Buy", type = "Market", volume = 1.00m });
        await client.PutJsonAsync($"/api/admin/accounts/{AccountId}/floors/max-loss", new { rule = new { kind = "FixedFloor", level = 90_000m } });
    }
}
