using Trading.Engine.Events;
using Trading.Engine.Tests.Support;

namespace Trading.Engine.Tests;

public sealed class PendingOrderTests
{
    [Fact]
    public void BuyLimitFillsAtAskWhichCanBeBetterThanTheLimit()
    {
        var driver = Ready();
        EventAssert.Single<OrderPlaced>(driver.Pending(OrderType.Limit, Side.Buy, 1.00m, 1.07900m));

        Assert.Empty(driver.Quote(1.07950m, 1.07960m));
        var opened = EventAssert.Single<PositionOpened>(driver.Quote(1.07880m, 1.07890m));

        Assert.Equal(1.07890m, opened.OpenPrice);
        Assert.Empty(driver.Account().Orders);
    }

    [Fact]
    public void BuyStopFillsAtAskEvenAfterAGap()
    {
        var driver = Ready();
        driver.Pending(OrderType.Stop, Side.Buy, 1.00m, 1.08100m);

        var opened = EventAssert.Single<PositionOpened>(driver.Quote(1.08120m, 1.08130m));

        Assert.Equal(1.08130m, opened.OpenPrice);
    }

    [Fact]
    public void SellLimitFillsWhenBidReachesTheLimit()
    {
        var driver = Ready();
        driver.Pending(OrderType.Limit, Side.Sell, 1.00m, 1.08100m);

        var opened = EventAssert.Single<PositionOpened>(driver.Quote(1.08100m, 1.08110m));

        Assert.Equal(1.08100m, opened.OpenPrice);
        Assert.Equal(Side.Sell, opened.Side);
    }

    [Fact]
    public void SellStopFillsAtBidEvenAfterAGap()
    {
        var driver = Ready();
        driver.Pending(OrderType.Stop, Side.Sell, 1.00m, 1.07900m);

        var opened = EventAssert.Single<PositionOpened>(driver.Quote(1.07850m, 1.07860m));

        Assert.Equal(1.07850m, opened.OpenPrice);
    }

    [Fact]
    public void StopLossAndTakeProfitCarryOverToThePosition()
    {
        var driver = Ready();
        driver.Pending(OrderType.Limit, Side.Buy, 1.00m, 1.07900m, stopLoss: 1.07800m, takeProfit: 1.08200m);

        var opened = EventAssert.Single<PositionOpened>(driver.Quote(1.07890m, 1.07900m));

        Assert.Equal(1.07800m, opened.StopLoss);
        Assert.Equal(1.08200m, opened.TakeProfit);
    }

    [Fact]
    public void OrderIsCancelledWhenMarginIsMissingAtTrigger()
    {
        var driver = Ready(balance: 1_000m);
        EventAssert.Single<OrderPlaced>(driver.Pending(OrderType.Limit, Side.Buy, 1.00m, 1.07900m));

        var cancelled = EventAssert.Single<OrderCancelled>(driver.Quote(1.07890m, 1.07900m));

        Assert.Equal(CancelReason.InsufficientMargin, cancelled.Reason);
        Assert.Empty(driver.Account().Positions);
        Assert.Empty(driver.Account().Orders);
    }

    [Fact]
    public void CancelledOrderNeverFills()
    {
        var driver = Ready();
        driver.Pending(OrderType.Limit, Side.Buy, 1.00m, 1.07900m);

        var cancelled = EventAssert.Single<OrderCancelled>(driver.CancelOrder("O1"));

        Assert.Equal(CancelReason.Manual, cancelled.Reason);
        Assert.Empty(driver.Quote(1.07880m, 1.07890m));
    }

    [Theory]
    [InlineData(OrderType.Limit, Side.Buy, "1.08010")]
    [InlineData(OrderType.Limit, Side.Sell, "1.08000")]
    [InlineData(OrderType.Stop, Side.Buy, "1.08010")]
    [InlineData(OrderType.Stop, Side.Sell, "1.08000")]
    [InlineData(OrderType.Limit, Side.Buy, "1.079995")]
    [InlineData(OrderType.Limit, Side.Buy, null)]
    public void PendingPriceMustBeOnTheWaitingSideAndOnTheGrid(OrderType type, Side side, string? price)
    {
        var driver = Ready();

        var events = driver.Pending(type, side, 1.00m, Decimals.ParseOptional(price));

        Assert.Equal(RejectReason.InvalidPrice, EventAssert.Rejected(events));
    }

    private static EngineDriver Ready(decimal balance = 100_000m)
    {
        var driver = new EngineDriver();
        driver.CreateAccount(balance);
        driver.Quote(1.08000m, 1.08010m);
        return driver;
    }
}
