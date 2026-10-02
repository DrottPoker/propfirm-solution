using Trading.Engine.Events;
using Trading.Engine.Tests.Support;

namespace Trading.Engine.Tests;

public sealed class StopLossTakeProfitTests
{
    [Fact]
    public void BuyStopLossClosesAtBidEvenAfterAGap()
    {
        var driver = Ready();
        driver.Buy(1.00m, stopLoss: 1.07900m);

        Assert.Empty(driver.Quote(1.07950m, 1.07960m));
        var closed = EventAssert.Single<PositionClosed>(driver.Quote(1.07850m, 1.07860m));

        Assert.Equal(CloseReason.StopLoss, closed.Reason);
        Assert.Equal(1.07850m, closed.ClosePrice);
        Assert.Equal(-160.00m, closed.Profit);
    }

    [Fact]
    public void SellTakeProfitClosesWhenAskReachesIt()
    {
        var driver = Ready();
        driver.Sell(1.00m, takeProfit: 1.07900m);

        var closed = EventAssert.Single<PositionClosed>(driver.Quote(1.07890m, 1.07900m));

        Assert.Equal(CloseReason.TakeProfit, closed.Reason);
        Assert.Equal(1.07900m, closed.ClosePrice);
        Assert.Equal(100.00m, closed.Profit);
    }

    [Fact]
    public void ModifySetsAndClearsStops()
    {
        var driver = Ready();
        driver.Buy(1.00m);

        var modified = EventAssert.Single<PositionModified>(driver.Modify("O1", 1.07900m, 1.08200m));
        Assert.Equal(1.07900m, modified.StopLoss);
        Assert.Equal(1.08200m, modified.TakeProfit);

        EventAssert.Single<PositionModified>(driver.Modify("O1", null, null));
        var position = Assert.Single(driver.Account().Positions);
        Assert.Null(position.StopLoss);
        Assert.Null(position.TakeProfit);
    }

    [Theory]
    [InlineData(Side.Buy, "1.08000", null, RejectReason.InvalidStopLoss)]
    [InlineData(Side.Buy, null, "1.08000", RejectReason.InvalidTakeProfit)]
    [InlineData(Side.Sell, "1.08010", null, RejectReason.InvalidStopLoss)]
    [InlineData(Side.Sell, null, "1.08010", RejectReason.InvalidTakeProfit)]
    [InlineData(Side.Buy, "1.079995", null, RejectReason.InvalidStopLoss)]
    [InlineData(Side.Buy, "0", null, RejectReason.InvalidStopLoss)]
    public void StopsMustBeOnTheRightSideOfTheClosePrice(Side side, string? stopLoss, string? takeProfit, RejectReason expected)
    {
        var driver = Ready();

        var events = side == Side.Buy
            ? driver.Buy(1.00m, stopLoss: Decimals.ParseOptional(stopLoss), takeProfit: Decimals.ParseOptional(takeProfit))
            : driver.Sell(1.00m, stopLoss: Decimals.ParseOptional(stopLoss), takeProfit: Decimals.ParseOptional(takeProfit));

        Assert.Equal(expected, EventAssert.Rejected(events));
    }

    [Fact]
    public void ModifyValidatesAgainstTheCurrentPrice()
    {
        var driver = Ready();
        driver.Buy(1.00m);

        Assert.Equal(RejectReason.InvalidStopLoss, EventAssert.Rejected(driver.Modify("O1", 1.08005m, null)));
        Assert.Equal(RejectReason.UnknownPosition, EventAssert.Rejected(driver.Modify("missing", null, null)));
    }

    private static EngineDriver Ready()
    {
        var driver = new EngineDriver();
        driver.CreateAccount();
        driver.Quote(1.08000m, 1.08010m);
        return driver;
    }
}
