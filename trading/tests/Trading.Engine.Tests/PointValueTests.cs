using Trading.Engine.Tests.Support;

namespace Trading.Engine.Tests;

public sealed class PointValueTests
{
    [Fact]
    public void PointOfAUsdQuotedPairIsWorthTheContractTimesThePoint()
    {
        var driver = new EngineDriver();
        driver.CreateAccount();

        // No price is needed when the quote currency is the account currency.
        Assert.Equal(new PointValue("EURUSD", "USD", 1.00000m), driver.PointValue());
        // 100 oz x 0.01
        Assert.Equal(1.00m, driver.PointValue("XAUUSD")!.PerLot);
    }

    [Fact]
    public void PointValueMatchesTheProfitTheEngineBooks()
    {
        var driver = new EngineDriver();
        driver.CreateAccount();
        driver.Quote(150.000m, 150.020m, "USDJPY");
        driver.Buy(2.00m, symbol: "USDJPY");

        // 500 points up, valued at the same conversion rate as the profit.
        driver.Quote(150.520m, 150.540m, "USDJPY");
        var pointValue = driver.PointValue("USDJPY")!;
        var position = Assert.Single(driver.Account().Positions);

        Assert.Equal("USD", pointValue.Currency);
        Assert.Equal(position.Profit, Math.Round(500m * 2.00m * pointValue.PerLot, 2, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void ThereIsNoPointValueWithoutAConversionRate()
    {
        // EURGBP needs GBPUSD, which has no price yet.
        var driver = new EngineDriver();
        driver.CreateAccount();
        driver.Quote(0.86400m, 0.86410m, "EURGBP");

        Assert.Null(driver.PointValue("EURGBP"));

        driver.Quote(1.25000m, 1.25010m, "GBPUSD");
        Assert.Equal(1.250050m, driver.PointValue("EURGBP")!.PerLot);
    }

    [Fact]
    public void UnknownAccountsAndSymbolsTheGroupCannotTradeHaveNoPointValue()
    {
        var driver = new EngineDriver(TestMarket.Configuration(tradableSymbols: ["EURUSD"]));
        driver.CreateAccount();

        Assert.Null(driver.PointValue(accountId: "missing"));
        Assert.Null(driver.PointValue("UNKNOWN"));
        Assert.Null(driver.PointValue("GBPUSD"));
    }
}
