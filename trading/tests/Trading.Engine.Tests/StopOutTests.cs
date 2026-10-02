using Trading.Engine.Events;
using Trading.Engine.Tests.Support;

namespace Trading.Engine.Tests;

public sealed class StopOutTests
{
    [Fact]
    public void ClosesLargestLossFirstUntilMarginLevelRecovers()
    {
        var driver = TwoPositionsOnSmallAccount(stopOutLevel: 50m);

        // P1 loses 755.00 and P2 805.00: equity 440.00 against 1 065.06 used margin, 41.31 %
        var events = driver.Quote(1.06500m, 1.06510m);

        Assert.Equal(2, events.Count);
        var stopOut = Assert.IsType<StopOutTriggered>(events[0]);
        Assert.Equal(440.00m, stopOut.Equity);
        Assert.Equal(1_065.06m, stopOut.UsedMargin);
        Assert.Equal(41.31m, Math.Round(stopOut.MarginLevelPercent, 2));

        var closed = Assert.IsType<PositionClosed>(events[1]);
        Assert.Equal("P2", closed.PositionId);
        Assert.Equal(CloseReason.StopOut, closed.Reason);
        Assert.Equal(-805.00m, closed.Profit);
        Assert.Equal(1_195.00m, closed.BalanceAfter);

        Assert.Equal("P1", Assert.Single(driver.Account().Positions).PositionId);
    }

    [Fact]
    public void StopOutLevelZeroDisablesStopOut()
    {
        var driver = TwoPositionsOnSmallAccount(stopOutLevel: 0m);

        Assert.Empty(driver.Quote(1.06500m, 1.06510m));
        Assert.Equal(2, driver.Account().Positions.Count);
    }

    private static EngineDriver TwoPositionsOnSmallAccount(decimal stopOutLevel)
    {
        var driver = new EngineDriver(TestMarket.Configuration(stopOutLevel: stopOutLevel));
        driver.CreateAccount(balance: 2_000m);
        driver.Quote(1.08000m, 1.08010m);
        EventAssert.Single<PositionOpened>(driver.Buy(0.50m, "P1"));
        driver.Quote(1.08100m, 1.08110m);
        EventAssert.Single<PositionOpened>(driver.Buy(0.50m, "P2"));
        return driver;
    }
}
