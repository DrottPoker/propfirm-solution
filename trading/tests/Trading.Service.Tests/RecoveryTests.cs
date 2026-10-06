using Trading.Service.Tests.Support;

namespace Trading.Service.Tests;

/// <summary>Restarts of the whole service on the same storage.</summary>
public sealed class RecoveryTests
{
    private const string AccountId = "T1";

    private static readonly Dictionary<string, string> ChangedMarkup = new() { ["Trading:Groups:0:Symbols:0:SpreadMarkupPoints"] = "5" };

    [Fact]
    public async Task StateSurvivesARestart()
    {
        var backend = new InMemoryBackend();
        long lastSequence;
        using (var first = new ServiceFactory(backend))
        {
            using var client = await TradeAsync(first);
            lastSequence = (await client.GetJsonAsync($"/api/accounts/{AccountId}/events")).EnumerateArray().Last().GetProperty("sequence").GetInt64();
        }

        using var second = new ServiceFactory(backend);
        using var restarted = await second.LoginAsync(ServiceFactory.EmailOf(AccountId), ServiceFactory.TraderPassword);

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
        using var client = await TradeAsync(first);
        var before = await client.GetJsonAsync($"/api/accounts/{AccountId}");

        // Everything a client got an answer for is stored, so a copy now is what a crash leaves behind.
        using var second = new ServiceFactory(first.Backend.Crashed());
        using var restarted = await second.LoginAsync(ServiceFactory.EmailOf(AccountId), ServiceFactory.TraderPassword);

        var after = await restarted.GetJsonAsync($"/api/accounts/{AccountId}");
        Assert.Equal(before.GetProperty("balance").GetDecimal(), after.GetProperty("balance").GetDecimal());
        Assert.Equal(before.GetProperty("positions").GetRawText(), after.GetProperty("positions").GetRawText());
    }

    [Fact]
    public async Task SessionSurvivesARestart()
    {
        var backend = new InMemoryBackend();
        string cookie;
        using (var first = new ServiceFactory(backend))
        {
            (await first.CreateTraderClientAsync(AccountId)).Dispose();
            cookie = await first.LoginCookieAsync(ServiceFactory.EmailOf(AccountId), ServiceFactory.TraderPassword);
        }

        using var second = new ServiceFactory(backend);
        using var client = second.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", cookie);

        var me = await client.GetJsonAsync("/api/auth/me");
        Assert.Equal(AccountId, me.GetProperty("accounts")[0].GetString());
    }

    [Fact]
    public async Task DevelopmentAccountIsNotRecreated()
    {
        var backend = new InMemoryBackend();
        using (var first = new ServiceFactory(backend))
        {
            using var demo = await first.LoginAsync("demo@example.com", "demo-password");
            await first.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
            await demo.PostJsonAsync("/api/accounts/demo/orders", new { orderId = "D1", symbol = "EURUSD", side = "Buy", type = "Market", volume = 1.00m });
        }

        using var second = new ServiceFactory(backend);
        using var restarted = await second.LoginAsync("demo@example.com", "demo-password");

        var account = await restarted.GetJsonAsync("/api/accounts/demo");
        Assert.Equal("D1", account.GetProperty("positions")[0].GetProperty("positionId").GetString());
    }

    [Fact]
    public async Task DamagedJournalStopsTheStart()
    {
        using var first = new ServiceFactory();
        (await TradeAsync(first)).Dispose();
        var damaged = first.Backend.Crashed();
        damaged.Journal.RemoveLastEvent();

        using var second = new ServiceFactory(damaged);
        var exception = Assert.ThrowsAny<Exception>(() => second.CreateClient());

        Assert.Contains("not deterministic or the journal is damaged", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangedConfigurationWithInputsToReplayStopsTheStart()
    {
        using var first = new ServiceFactory();
        (await TradeAsync(first)).Dispose();

        using var second = new ServiceFactory(first.Backend.Crashed(), ChangedMarkup);
        var exception = Assert.ThrowsAny<Exception>(() => second.CreateClient());

        Assert.Contains("configuration has changed", exception.ToString(), StringComparison.Ordinal);
    }

    // For example a new instrument: the inputs after the snapshot give the same events as before, so nothing changed for them.
    [Fact]
    public async Task ChangedConfigurationThatGivesTheSameEventsIsAccepted()
    {
        using var first = new ServiceFactory();
        (await TradeAsync(first)).Dispose();

        using var second = new ServiceFactory(first.Backend.Crashed(), new Dictionary<string, string> { ["Trading:MaxQuoteAge"] = "00:00:10" });
        using var restarted = await second.LoginAsync(ServiceFactory.EmailOf(AccountId), ServiceFactory.TraderPassword);

        var account = await restarted.GetJsonAsync($"/api/accounts/{AccountId}");
        Assert.Equal("O1", account.GetProperty("positions")[0].GetProperty("positionId").GetString());
    }

    [Fact]
    public async Task ChangedConfigurationAfterACleanStopIsAccepted()
    {
        var backend = new InMemoryBackend();
        using (var first = new ServiceFactory(backend))
        {
            (await TradeAsync(first)).Dispose();
        }

        using var second = new ServiceFactory(backend, ChangedMarkup);
        using var restarted = await second.LoginAsync(ServiceFactory.EmailOf(AccountId), ServiceFactory.TraderPassword);

        var instruments = await restarted.GetJsonAsync($"/api/accounts/{AccountId}/instruments");
        Assert.Equal(5, instruments.EnumerateArray().First(i => i.GetProperty("symbol").GetString() == "EURUSD").GetProperty("spreadMarkupPoints").GetInt32());
    }

    private static async Task<HttpClient> TradeAsync(ServiceFactory factory)
    {
        var client = await factory.CreateTraderClientAsync(AccountId);
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        await client.PostJsonAsync($"/api/accounts/{AccountId}/orders", new { orderId = "O1", symbol = "EURUSD", side = "Buy", type = "Market", volume = 1.00m });
        await client.PutJsonAsync($"/api/admin/v1/accounts/{AccountId}/floors/max-loss", new { rule = new { kind = "FixedFloor", level = 90_000m } });
        return client;
    }
}
