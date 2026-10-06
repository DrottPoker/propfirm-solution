using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Engine.Tests.Support;

namespace Trading.Engine.Tests;

/// <summary>Partial close, closing every position, modifying pending orders and trailing stops.</summary>
public sealed class OrderToolsTests
{
    private const string Account = EngineDriver.AccountId;

    [Fact]
    public void PartOfAPositionIsClosedAndTheRestStaysOpen()
    {
        var driver = Ready(commission: 3.50m);
        driver.Buy(1.00m, "P1");
        driver.Quote(1.08200m, 1.08210m);

        var events = driver.Apply(t => new ClosePosition(t, Account, "P1", 0.40m));

        var part = EventAssert.Single<PositionPartiallyClosed>(events);
        Assert.Equal((0.40m, 0.60m, 1.08010m, 1.08200m), (part.Volume, part.RemainingVolume, part.OpenPrice, part.ClosePrice));
        Assert.Equal((76.00m, 1.40m, CloseReason.Manual), (part.Profit, part.Commission, part.Reason));
        var account = driver.Account();
        Assert.Equal(100_000m - 3.50m + 76.00m - 1.40m, account.Balance);
        Assert.Equal(0.60m, Assert.Single(account.Positions).Volume);
    }

    [Fact]
    public void ClosingTheWholeVolumeIsAnOrdinaryClose()
    {
        var driver = Ready();
        driver.Buy(1.00m, "P1");

        Assert.IsType<PositionClosed>(Assert.Single(driver.Apply(t => new ClosePosition(t, Account, "P1", 1.00m))));
        Assert.Empty(driver.Account().Positions);
    }

    [Theory]
    [InlineData("0.005")] // not on the volume step
    [InlineData("1.50")] // more than the position
    [InlineData("0")]
    public void APartMustBeAVolumeThePositionHas(string volume)
    {
        var driver = Ready();
        driver.Buy(1.00m, "P1");

        Assert.Equal(RejectReason.InvalidVolume, EventAssert.Rejected(driver.Apply(t => new ClosePosition(t, Account, "P1", Decimals.Parse(volume)))));
    }

    [Fact]
    public void APartMustLeaveTheSmallestVolume()
    {
        var driver = new EngineDriver(TestMarket.Configuration(instruments: [TestMarket.EurUsd with { VolumeMin = 0.10m }]));
        driver.CreateAccount();
        driver.Quote(1.08000m, 1.08010m);
        driver.Buy(0.15m, "P1");

        Assert.Equal(RejectReason.InvalidVolume, EventAssert.Rejected(driver.Apply(t => new ClosePosition(t, Account, "P1", 0.10m))));
    }

    [Fact]
    public void ThePartsAddUpToTheWholePosition()
    {
        var driver = Ready();
        driver.Buy(1.00m, "P1");
        driver.Quote(1.08200m, 1.08210m);
        var whole = Ready();
        whole.Buy(1.00m, "P1");
        whole.Quote(1.08200m, 1.08210m);

        driver.Apply(t => new ClosePosition(t, Account, "P1", 0.30m));
        driver.Apply(t => new ClosePosition(t, Account, "P1", 0.30m));
        driver.Close("P1");
        whole.Close("P1");

        Assert.Equal(whole.Account().Balance, driver.Account().Balance);
    }

    [Fact]
    public void EveryPositionIsClosedAtOnce()
    {
        var driver = Ready();
        driver.Quote(2650.00m, 2650.30m, "XAUUSD");
        driver.Buy(1.00m, "P1");
        driver.Buy(0.10m, "P2", "XAUUSD");
        driver.Pending(OrderType.Limit, Side.Buy, 1.00m, 1.07000m, "O3");

        var events = driver.Apply(t => new CloseAllPositions(t, Account));

        Assert.Equal(["P1", "P2"], events.OfType<PositionClosed>().Select(e => e.PositionId));
        Assert.Empty(driver.Account().Positions);
        Assert.Single(driver.Account().Orders);
    }

    [Fact]
    public void OnlyTheSymbolsPositionsAreClosed()
    {
        var driver = Ready();
        driver.Quote(2650.00m, 2650.30m, "XAUUSD");
        driver.Buy(1.00m, "P1");
        driver.Buy(0.10m, "P2", "XAUUSD");

        var closed = Assert.Single(driver.Apply(t => new CloseAllPositions(t, Account, "XAUUSD")).OfType<PositionClosed>());

        Assert.Equal("P2", closed.PositionId);
        Assert.Equal("P1", Assert.Single(driver.Account().Positions).PositionId);
    }

    [Fact]
    public void ClosingAllWithoutPositionsIsRefused()
    {
        var driver = Ready();

        Assert.Equal(RejectReason.UnknownPosition, EventAssert.Rejected(driver.Apply(t => new CloseAllPositions(t, Account))));
    }

    [Fact]
    public void PositionsInAClosedMarketStayOpen()
    {
        // EURUSD follows the forex week and gold has no hours. On Saturday only gold closes.
        var driver = new EngineDriver(TestMarket.Configuration(instruments: [TestMarket.EurUsd with { TradingHours = TradingHoursTests.Forex }, TestMarket.XauUsd]));
        driver.CreateAccount();
        driver.Quote(1.08000m, 1.08010m);
        driver.Quote(2650.00m, 2650.30m, "XAUUSD");
        driver.Buy(1.00m, "P1");
        driver.Buy(0.10m, "P2", "XAUUSD");
        driver.Quote(2651.00m, 2651.30m, "XAUUSD", TimeSpan.FromHours(124));

        var closed = Assert.Single(driver.Apply(t => new CloseAllPositions(t, Account)).OfType<PositionClosed>());
        var refused = driver.Apply(t => new CloseAllPositions(t, Account));

        Assert.Equal("P2", closed.PositionId);
        Assert.Equal(RejectReason.MarketClosed, EventAssert.Rejected(refused));
        Assert.Equal("P1", Assert.Single(driver.Account().Positions).PositionId);
    }

    [Fact]
    public void APendingOrderMovesAndFillsAtItsNewPrice()
    {
        var driver = Ready();
        driver.Pending(OrderType.Limit, Side.Buy, 1.00m, 1.07000m, "O1");

        var modified = EventAssert.Single<OrderModified>(driver.Apply(t => new ModifyOrder(t, Account, "O1", 1.07900m, 1.07500m, 1.08500m)));
        var filled = driver.Quote(1.07890m, 1.07900m);

        Assert.Equal((1.07900m, 1.07500m, 1.08500m), (modified.Price, modified.StopLoss, modified.TakeProfit));
        var opened = Assert.IsType<PositionOpened>(Assert.Single(filled));
        Assert.Equal((1.07900m, 1.07500m, 1.08500m), (opened.OpenPrice, opened.StopLoss, opened.TakeProfit));
    }

    [Fact]
    public void AModifiedOrderIsCheckedLikeANewOne()
    {
        var driver = Ready();
        driver.Pending(OrderType.Limit, Side.Buy, 1.00m, 1.07000m, "O1");

        Assert.Equal(RejectReason.InvalidPrice, EventAssert.Rejected(driver.Apply(t => new ModifyOrder(t, Account, "O1", 1.08100m, null, null))));
        Assert.Equal(RejectReason.InvalidStopLoss, EventAssert.Rejected(driver.Apply(t => new ModifyOrder(t, Account, "O1", 1.07000m, 1.07100m, null))));
        Assert.Equal(RejectReason.UnknownOrder, EventAssert.Rejected(driver.Apply(t => new ModifyOrder(t, Account, "O9", 1.07000m, null, null))));
        Assert.Equal(1.07000m, Assert.Single(driver.Account().Orders).Price);
    }

    [Fact]
    public void ATrailingStopFollowsThePriceAndNeverGoesBack()
    {
        var driver = Ready();

        // The bid is 1.08000, so the stop loss trails 10 pips behind it.
        var opened = EventAssert.Single<PositionOpened>(driver.Apply(t => new PlaceOrder(t, Account, "P1", "EURUSD", Side.Buy, OrderType.Market, 1.00m, null, 1.07900m, null, true)));
        Assert.Equal(0.00100m, opened.TrailingDistance);

        Assert.Empty(driver.Quote(1.08250m, 1.08260m));
        Assert.Equal(1.08150m, Position(driver).StopLoss);
        Assert.Empty(driver.Quote(1.08200m, 1.08210m));
        Assert.Equal(1.08150m, Position(driver).StopLoss);

        var closed = Assert.IsType<PositionClosed>(Assert.Single(driver.Quote(1.08140m, 1.08150m)));
        Assert.Equal((CloseReason.StopLoss, 1.08140m), (closed.Reason, closed.ClosePrice));
    }

    [Fact]
    public void ASellsTrailingStopFollowsTheAskDown()
    {
        var driver = Ready();
        driver.Apply(t => new PlaceOrder(t, Account, "P1", "EURUSD", Side.Sell, OrderType.Market, 1.00m, null, 1.08110m, null, true));

        driver.Quote(1.07800m, 1.07810m);

        Assert.Equal(1.07910m, Position(driver).StopLoss);
        Assert.Equal(0.00100m, Position(driver).TrailingDistance);
    }

    [Fact]
    public void ATrailingStopNeedsAStopLoss()
    {
        var driver = Ready();
        driver.Buy(1.00m, "P1");

        Assert.Equal(RejectReason.NoStopLoss, EventAssert.Rejected(driver.Apply(t => new PlaceOrder(t, Account, "P2", "EURUSD", Side.Buy, OrderType.Market, 1.00m, TrailingStop: true))));
        Assert.Equal(RejectReason.NoStopLoss, EventAssert.Rejected(driver.Apply(t => new ModifyPosition(t, Account, "P1", null, null, true))));
    }

    [Fact]
    public void ATrailingStopIsTurnedOnAndOffWithTheStops()
    {
        var driver = Ready();
        driver.Buy(1.00m, "P1");

        var on = EventAssert.Single<PositionModified>(driver.Apply(t => new ModifyPosition(t, Account, "P1", 1.07800m, null, true)));
        driver.Quote(1.08100m, 1.08110m);
        var trailed = Position(driver).StopLoss;
        var off = EventAssert.Single<PositionModified>(driver.Apply(t => new ModifyPosition(t, Account, "P1", trailed, null)));
        driver.Quote(1.08300m, 1.08310m);

        Assert.Equal(0.00200m, on.TrailingDistance);
        Assert.Equal(1.07900m, trailed);
        Assert.Null(off.TrailingDistance);
        Assert.Equal(1.07900m, Position(driver).StopLoss);
    }

    [Fact]
    public void APendingOrderTrailsFromItsOwnPrice()
    {
        var driver = Ready();
        var placed = EventAssert.Single<OrderPlaced>(driver.Apply(t => new PlaceOrder(t, Account, "O1", "EURUSD", Side.Buy, OrderType.Limit, 1.00m, 1.07500m, 1.07300m, null, true)));

        var opened = Assert.IsType<PositionOpened>(Assert.Single(driver.Quote(1.07490m, 1.07500m)));
        driver.Quote(1.07700m, 1.07710m);

        Assert.Equal(0.00200m, placed.TrailingDistance);
        Assert.Equal(0.00200m, opened.TrailingDistance);
        Assert.Equal(1.07500m, Position(driver).StopLoss);
    }

    [Fact]
    public void TrailingStopsAndPartsSurviveARestore()
    {
        var driver = Ready();
        driver.Apply(t => new PlaceOrder(t, Account, "P1", "EURUSD", Side.Buy, OrderType.Market, 1.00m, null, 1.07900m, null, true));
        driver.Apply(t => new ClosePosition(t, Account, "P1", 0.25m));
        driver.Apply(t => new PlaceOrder(t, Account, "O2", "EURUSD", Side.Buy, OrderType.Limit, 1.00m, 1.07500m, 1.07300m, null, true));

        var restored = TradingEngine.FromState(TestMarket.Configuration(), driver.ExportState());

        var account = restored.GetAccount(Account)!;
        Assert.Equal((0.75m, 0.00100m), (account.Positions[0].Volume, account.Positions[0].TrailingDistance));
        Assert.Equal(0.00200m, account.Orders[0].TrailingDistance);
    }

    private static PositionSnapshot Position(EngineDriver driver) => Assert.Single(driver.Account().Positions);

    private static EngineDriver Ready(decimal commission = 0m)
    {
        var driver = new EngineDriver(TestMarket.Configuration(commission: commission));
        driver.CreateAccount();
        driver.Quote(1.08000m, 1.08010m);
        return driver;
    }
}
