using Trading.Engine.Events;
using Trading.Engine.Tests.Support;

namespace Trading.Engine.Tests;

public sealed class MarketOrderTests
{
    [Fact]
    public void BuyFillsAtAskAndChargesCommission()
    {
        var driver = new EngineDriver(TestMarket.Configuration(commission: 3.50m));
        driver.CreateAccount();
        driver.Quote(1.08000m, 1.08010m);

        var opened = EventAssert.Single<PositionOpened>(driver.Buy(1.00m));

        Assert.Equal(1.08010m, opened.OpenPrice);
        Assert.Equal(3.50m, opened.Commission);
        Assert.Equal(99_996.50m, opened.BalanceAfter);

        var account = driver.Account();
        var position = Assert.Single(account.Positions);
        Assert.Equal(1.08000m, position.CurrentPrice);
        Assert.Equal(-10.00m, position.Profit);
        Assert.Equal(1_080.05m, position.Margin);
        Assert.Equal(99_986.50m, account.Equity);
        Assert.Equal(1_080.05m, account.UsedMargin);
        Assert.Equal(98_906.45m, account.FreeMargin);
    }

    [Fact]
    public void SellFillsAtBidAndProfitsWhenPriceFalls()
    {
        var driver = new EngineDriver();
        driver.CreateAccount();
        driver.Quote(1.08000m, 1.08010m);

        var opened = EventAssert.Single<PositionOpened>(driver.Sell(2.00m));
        driver.Quote(1.07950m, 1.07960m);

        Assert.Equal(1.08000m, opened.OpenPrice);
        var position = Assert.Single(driver.Account().Positions);
        Assert.Equal(1.07960m, position.CurrentPrice);
        Assert.Equal(80.00m, position.Profit);
        Assert.Equal(100_080.00m, driver.Account().Equity);
    }

    [Fact]
    public void SpreadMarkupLowersBidByHalfAndRaisesAskByTheRest()
    {
        var driver = new EngineDriver(TestMarket.Configuration(markupPoints: 3));
        driver.CreateAccount();
        driver.Quote(1.08000m, 1.08010m);

        var bought = EventAssert.Single<PositionOpened>(driver.Buy(1.00m, "O1"));
        var sold = EventAssert.Single<PositionOpened>(driver.Sell(1.00m, "O2"));

        Assert.Equal(1.08012m, bought.OpenPrice);
        Assert.Equal(1.07999m, sold.OpenPrice);
    }

    [Fact]
    public void ClosingBooksProfitAndCommissionToBalance()
    {
        var driver = new EngineDriver(TestMarket.Configuration(commission: 3.50m));
        driver.CreateAccount();
        driver.Quote(1.08000m, 1.08010m);
        driver.Buy(1.00m);
        driver.Quote(1.08100m, 1.08110m);

        var closed = EventAssert.Single<PositionClosed>(driver.Close("O1"));

        Assert.Equal(1.08100m, closed.ClosePrice);
        Assert.Equal(90.00m, closed.Profit);
        Assert.Equal(3.50m, closed.Commission);
        Assert.Equal(CloseReason.Manual, closed.Reason);
        Assert.Equal(100_083.00m, closed.BalanceAfter);
        Assert.Empty(driver.Account().Positions);
        Assert.Equal(100_083.00m, driver.Account().Equity);
    }

    [Theory]
    [InlineData(5000, true)]
    [InlineData(5001, false)]
    public void OrdersNeedAFreshPrice(int priceAgeMilliseconds, bool accepted)
    {
        var driver = new EngineDriver();
        driver.CreateAccount();
        driver.Quote(1.08000m, 1.08010m);

        var events = driver.Buy(1.00m, after: TimeSpan.FromMilliseconds(priceAgeMilliseconds));

        if (accepted)
        {
            EventAssert.Single<PositionOpened>(events);
        }
        else
        {
            Assert.Equal(RejectReason.StalePrice, EventAssert.Rejected(events));
        }
    }

    [Fact]
    public void OrderWithoutEnoughFreeMarginIsRejected()
    {
        var driver = new EngineDriver();
        driver.CreateAccount(balance: 1_000m);
        driver.Quote(1.08000m, 1.08010m);

        Assert.Equal(RejectReason.InsufficientMargin, EventAssert.Rejected(driver.Buy(1.00m)));
        Assert.Empty(driver.Account().Positions);
    }
}
