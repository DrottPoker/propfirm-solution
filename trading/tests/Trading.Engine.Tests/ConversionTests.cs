using Trading.Engine.Events;
using Trading.Engine.Tests.Support;

namespace Trading.Engine.Tests;

public sealed class ConversionTests
{
    [Fact]
    public void ProfitInJpyIsConvertedWithTheCurrentRate()
    {
        var driver = new EngineDriver();
        driver.CreateAccount();
        driver.Quote(150.000m, 150.020m, "USDJPY");

        var opened = EventAssert.Single<PositionOpened>(driver.Buy(1.00m, symbol: "USDJPY"));
        driver.Quote(150.520m, 150.540m, "USDJPY");

        Assert.Equal(150.020m, opened.OpenPrice);
        var position = Assert.Single(driver.Account().Positions);
        // 50 000 JPY at the mid rate 150.530
        Assert.Equal(332.16m, position.Profit);
        // Base currency is the account currency
        Assert.Equal(1_000.00m, position.Margin);
    }

    [Fact]
    public void CrossUsesQuoteCurrencyRateForProfitAndBaseCurrencyRateForMargin()
    {
        var driver = new EngineDriver();
        driver.CreateAccount();
        driver.Quote(1.08000m, 1.08010m, "EURUSD");
        driver.Quote(1.25000m, 1.25010m, "GBPUSD");
        driver.Quote(0.86400m, 0.86410m, "EURGBP");

        driver.Buy(1.00m, symbol: "EURGBP");
        var opened = Assert.Single(driver.Account().Positions);
        Assert.Equal(1_080.05m, opened.Margin);
        Assert.Equal(-12.50m, opened.Profit);

        // Only the conversion rate moves
        driver.Quote(1.26000m, 1.26010m, "GBPUSD");
        Assert.Equal(-12.60m, Assert.Single(driver.Account().Positions).Profit);

        driver.Quote(0.86500m, 0.86510m, "EURGBP");
        Assert.Equal(113.40m, Assert.Single(driver.Account().Positions).Profit);
    }

    [Fact]
    public void OrderIsRejectedWithoutAConversionRate()
    {
        // GBPUSD is configured but has no price yet
        var driver = new EngineDriver();
        driver.CreateAccount();
        driver.Quote(1.08000m, 1.08010m, "EURUSD");
        driver.Quote(0.86400m, 0.86410m, "EURGBP");

        Assert.Equal(RejectReason.NoConversionRate, EventAssert.Rejected(driver.Buy(1.00m, symbol: "EURGBP")));
    }

    [Fact]
    public void GoldUsesItsContractSizeAndTheGroupLeverage()
    {
        var driver = new EngineDriver();
        driver.CreateAccount();
        driver.Quote(2650.00m, 2650.30m, "XAUUSD");

        driver.Buy(0.10m, symbol: "XAUUSD");
        // 0.10 lot x 100 oz x 2650.15 / 30
        Assert.Equal(883.38m, Assert.Single(driver.Account().Positions).Margin);

        driver.Quote(2660.00m, 2660.30m, "XAUUSD");
        Assert.Equal(97.00m, Assert.Single(driver.Account().Positions).Profit);
    }
}
