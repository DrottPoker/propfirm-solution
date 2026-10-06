using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Engine.Tests.Support;

namespace Trading.Engine.Tests;

/// <summary>EURUSD follows the forex week. Gold has no trading hours, so it is always open.</summary>
public sealed class MarketClosedTests
{
    // From the driver's start on Monday 08:00 UTC to Saturday 12:00 UTC.
    private static readonly TimeSpan ToSaturday = TimeSpan.FromHours(124);

    [Fact]
    public void OrdersAreRefusedWhileTheMarketIsClosed()
    {
        var driver = Ready();
        driver.Quote(1.08000m, 1.08010m, after: ToSaturday);

        Assert.Equal(RejectReason.MarketClosed, EventAssert.Rejected(driver.Buy(1.00m)));
        Assert.Equal(RejectReason.MarketClosed, EventAssert.Rejected(driver.Pending(OrderType.Limit, Side.Buy, 1.00m, 1.07000m)));
    }

    [Fact]
    public void ClosesAndStopChangesAreRefusedWhileTheMarketIsClosed()
    {
        var driver = Ready();
        driver.Buy(1.00m, "P1");
        driver.Quote(1.08000m, 1.08010m, after: ToSaturday);

        Assert.Equal(RejectReason.MarketClosed, EventAssert.Rejected(driver.Close("P1")));
        Assert.Equal(RejectReason.MarketClosed, EventAssert.Rejected(driver.Modify("P1", 1.07000m, null)));
        Assert.Single(driver.Account().Positions);
    }

    [Fact]
    public void ClosedMarketIsTheReasonEvenWhenThePriceIsOld()
    {
        var driver = Ready();

        Assert.Equal(RejectReason.MarketClosed, EventAssert.Rejected(driver.Apply(t => new PlaceOrder(t, EngineDriver.AccountId, "O1", "EURUSD", Side.Buy, OrderType.Market, 1.00m), ToSaturday)));
    }

    [Fact]
    public void PendingOrdersCanBeCancelledWhileTheMarketIsClosed()
    {
        var driver = Ready();
        driver.Pending(OrderType.Limit, Side.Buy, 1.00m, 1.07000m, "O1");
        driver.Quote(1.08000m, 1.08010m, after: ToSaturday);

        Assert.IsType<OrderCancelled>(Assert.Single(driver.CancelOrder("O1")));
    }

    [Fact]
    public void MarketWithoutTradingHoursIsAlwaysOpen()
    {
        var driver = Ready();
        driver.Quote(2650.00m, 2650.30m, "XAUUSD", ToSaturday);

        Assert.IsType<PositionOpened>(Assert.Single(driver.Buy(0.10m, symbol: "XAUUSD")));
    }

    [Fact]
    public void OrdersAreTakenAgainWhenTheMarketOpens()
    {
        var driver = Ready();

        // Sunday 21:00 UTC is 17:00 in New York, when the forex week opens.
        driver.Quote(1.08100m, 1.08110m, after: ToSaturday + TimeSpan.FromHours(33));

        Assert.IsType<PositionOpened>(Assert.Single(driver.Buy(1.00m)));
    }

    [Fact]
    public void PricesStillCountWhileTheMarketIsClosed()
    {
        // A price that comes in after the close is real, so it still moves equity and can hit a stop.
        var driver = Ready();
        driver.Buy(1.00m, "P1", stopLoss: 1.07900m);

        var events = driver.Quote(1.07800m, 1.07810m, after: ToSaturday);

        Assert.Equal(CloseReason.StopLoss, Assert.IsType<PositionClosed>(Assert.Single(events)).Reason);
    }

    [Fact]
    public void ClosingTheAccountUsesTheLastPriceWhileTheMarketIsClosed()
    {
        var driver = Ready();
        driver.Buy(1.00m, "P1");

        var events = driver.Apply(t => new CloseAccount(t, EngineDriver.AccountId), ToSaturday);

        Assert.Equal(1.08000m, events.OfType<PositionClosed>().Single().ClosePrice);
        Assert.Equal(AccountStatus.Disabled, driver.Account().Status);
    }

    private static EngineDriver Ready()
    {
        var driver = new EngineDriver(TestMarket.Configuration(instruments: [TestMarket.EurUsd with { TradingHours = TradingHoursTests.Forex }, TestMarket.XauUsd]));
        driver.CreateAccount();
        driver.Quote(1.08000m, 1.08010m);
        return driver;
    }
}
