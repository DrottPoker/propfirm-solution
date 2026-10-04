using System.Net;

using Trading.Service.Tests.Support;

namespace Trading.Service.Tests;

/// <summary>The firm's own trading conditions through the admin API: leverage, spread markup, commission and symbols.</summary>
public sealed class TradingConditionsTests
{
    [Fact]
    public async Task TheFirmSeesEveryInstrumentAndItsGroups()
    {
        using var factory = new ServiceFactory();
        using var admin = factory.CreateAdminClient();

        var instruments = await admin.GetJsonAsync("/api/admin/v1/instruments");
        var groups = await admin.GetJsonAsync("/api/admin/v1/groups");

        Assert.Equal(12, instruments.GetArrayLength());
        var silver = instruments.EnumerateArray().Single(i => i.GetProperty("symbol").GetString() == "XAGUSD");
        Assert.Equal((5000m, 3), (silver.GetProperty("contractSize").GetDecimal(), silver.GetProperty("digits").GetInt32()));
        var group = Assert.Single(groups.EnumerateArray());
        Assert.Equal(("standard", "USD", false), (group.GetProperty("id").GetString(), group.GetProperty("currency").GetString(), group.GetProperty("changeable").GetBoolean()));
        Assert.Equal(12, group.GetProperty("symbols").GetArrayLength());
    }

    // The configured group belongs to the configuration, so only a firm created by a partner changes its conditions.
    [Fact]
    public async Task AConfiguredGroupCannotBeChanged()
    {
        using var factory = new ServiceFactory();
        using var admin = factory.CreateAdminClient();

        var refused = await admin.PutJsonAsync(
            "/api/admin/v1/groups/standard/symbols",
            new { symbols = new[] { new { symbol = "EURUSD", leverage = 50, spreadMarkupPoints = 0, commissionPerLotPerSide = 0m } } },
            HttpStatusCode.UnprocessableEntity);

        Assert.Equal("GroupNotChangeable", refused.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ANewFirmChangesItsConditionsAndTradersSeeThemAtOnce()
    {
        using var factory = new ServiceFactory();
        var (acme, trader) = await SecondFirmAsync(factory);
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);

        var changed = await acme.PutJsonAsync(
            "/api/admin/v1/groups/acme-standard/symbols",
            new
            {
                symbols = new[]
                {
                    new { symbol = "EURUSD", leverage = 30, spreadMarkupPoints = 0, commissionPerLotPerSide = 2.5m },
                    new { symbol = "BTCUSD", leverage = 2, spreadMarkupPoints = 0, commissionPerLotPerSide = 0m },
                },
            },
            HttpStatusCode.UnprocessableEntity);
        await acme.PutJsonAsync(
            "/api/admin/v1/groups/acme-standard/symbols",
            new { symbols = new[] { new { symbol = "EURUSD", leverage = 30, spreadMarkupPoints = 0, commissionPerLotPerSide = 2.5m } } });

        var instruments = await trader.GetJsonAsync("/api/accounts/ACME1/instruments");
        var order = await trader.PostJsonAsync("/api/accounts/ACME1/orders", new { orderId = "O1", symbol = "EURUSD", side = "Buy", type = "Market", volume = 1.00m });
        var group = Assert.Single((await acme.GetJsonAsync("/api/admin/v1/groups")).EnumerateArray());

        Assert.Equal("InvalidGroup", changed.GetProperty("reason").GetString());
        var eurusd = Assert.Single(instruments.EnumerateArray());
        Assert.Equal((30, 0, 2.5m), (eurusd.GetProperty("leverage").GetInt32(), eurusd.GetProperty("spreadMarkupPoints").GetInt32(), eurusd.GetProperty("commissionPerLotPerSide").GetDecimal()));
        var opened = order.GetProperty("events")[0].GetProperty("event");
        Assert.Equal((1.08010m, 2.5m), (opened.GetProperty("openPrice").GetDecimal(), opened.GetProperty("commission").GetDecimal()));
        Assert.True(group.GetProperty("changeable").GetBoolean());
    }

    [Fact]
    public async Task ASymbolInUseCannotBeRemoved()
    {
        using var factory = new ServiceFactory();
        var (acme, trader) = await SecondFirmAsync(factory);
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        await trader.PostJsonAsync("/api/accounts/ACME1/orders", new { orderId = "O1", symbol = "EURUSD", side = "Buy", type = "Market", volume = 1.00m });

        var refused = await acme.PutJsonAsync(
            "/api/admin/v1/groups/acme-standard/symbols",
            new { symbols = new[] { new { symbol = "XAUUSD", leverage = 30, spreadMarkupPoints = 10, commissionPerLotPerSide = 3.5m } } },
            HttpStatusCode.Conflict);
        await acme.PutJsonAsync("/api/admin/v1/groups/standard/symbols", new { symbols = Array.Empty<object>() }, HttpStatusCode.NotFound);

        Assert.Equal("SymbolInUse", refused.GetProperty("reason").GetString());
    }

    // A restart replays the change, so the conditions and the open position survive.
    [Fact]
    public async Task ChangedConditionsSurviveACrash()
    {
        using var first = new ServiceFactory();
        var (acme, _) = await SecondFirmAsync(first);
        await acme.PutJsonAsync(
            "/api/admin/v1/groups/acme-standard/symbols",
            new { symbols = new[] { new { symbol = "EURUSD", leverage = 30, spreadMarkupPoints = 0, commissionPerLotPerSide = 2.5m } } });

        using var second = new ServiceFactory(first.Backend.Crashed());
        using var trader = await second.LoginAsync(ServiceFactory.EmailOf("ACME1"), ServiceFactory.TraderPassword, "acme");

        var eurusd = Assert.Single((await trader.GetJsonAsync("/api/accounts/ACME1/instruments")).EnumerateArray());
        Assert.Equal(30, eurusd.GetProperty("leverage").GetInt32());
    }

    private static async Task<(HttpClient Admin, HttpClient Trader)> SecondFirmAsync(ServiceFactory factory)
    {
        using var partner = factory.CreatePartnerClient();
        var apiKey = (await partner.PostJsonAsync("/api/partner/v1/tenants", new { id = "acme", name = "Acme" }, HttpStatusCode.Created)).GetProperty("adminApiKey").GetString()!;
        var admin = factory.CreateAdminClient(apiKey);
        var user = await admin.PostJsonAsync("/api/admin/v1/users", new { email = ServiceFactory.EmailOf("ACME1"), password = ServiceFactory.TraderPassword });
        await admin.PostJsonAsync(
            "/api/admin/v1/accounts",
            new { accountId = "ACME1", groupId = "acme-standard", initialBalance = 100_000m, ownerUserId = user.GetProperty("userId").GetGuid() });
        return (admin, await factory.LoginAsync(ServiceFactory.EmailOf("ACME1"), ServiceFactory.TraderPassword, "acme"));
    }
}
