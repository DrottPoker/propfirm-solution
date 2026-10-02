using Trading.Engine.Events;
using Trading.Engine.Tests.Support;

namespace Trading.Engine.Tests;

public sealed class EquityFloorTests
{
    [Fact]
    public void FixedFloorBreachLiquidatesAndDisablesWithEvidence()
    {
        var driver = Ready();
        var set = EventAssert.Single<EquityFloorSet>(driver.SetFloor("max-loss", new FixedFloor(90_000m)));
        Assert.Equal(90_000m, set.Level);
        driver.Buy(10.00m, "O1");
        driver.Pending(OrderType.Limit, Side.Buy, 1.00m, 1.05000m, "O2");

        // Equity exactly at the floor is allowed
        Assert.Empty(driver.Quote(1.07010m, 1.07020m));
        var events = driver.Quote(1.07000m, 1.07010m);

        Assert.Equal(4, events.Count);
        var breach = Assert.IsType<EquityFloorBreached>(events[0]);
        Assert.Equal("max-loss", breach.FloorId);
        Assert.Equal(90_000m, breach.Level);
        Assert.Equal(89_900.00m, breach.Equity);
        var price = Assert.Single(breach.Prices);
        Assert.Equal(("EURUSD", 1.07000m, 1.07010m), (price.Symbol, price.Bid, price.Ask));
        Assert.Equal(-10_100.00m, Assert.Single(breach.Positions).Profit);

        var closed = Assert.IsType<PositionClosed>(events[1]);
        Assert.Equal(CloseReason.EquityFloor, closed.Reason);
        Assert.Equal(89_900.00m, closed.BalanceAfter);
        Assert.Equal(CancelReason.EquityFloor, Assert.IsType<OrderCancelled>(events[2]).Reason);
        Assert.Equal(DisableReason.EquityFloor, Assert.IsType<AccountDisabled>(events[3]).Reason);

        Assert.Equal(AccountStatus.Disabled, driver.Account().Status);
        Assert.Equal(RejectReason.AccountDisabled, EventAssert.Rejected(driver.Buy(1.00m, "O3")));
    }

    [Fact]
    public void TrailingFloorFollowsHighestEquityAndStopsAtLockLevel()
    {
        var driver = Ready();
        driver.SetFloor("max-loss", new TrailingFloor(5_000m, LockLevel: 100_000m));
        driver.Buy(10.00m);

        driver.Quote(1.08300m, 1.08310m);
        Assert.Equal(97_900m, FloorLevel(driver));

        // The floor never moves down
        driver.Quote(1.08200m, 1.08210m);
        Assert.Equal(97_900m, FloorLevel(driver));

        driver.Quote(1.08700m, 1.08710m);
        Assert.Equal(100_000m, FloorLevel(driver));
        driver.Quote(1.09000m, 1.09010m);
        Assert.Equal(100_000m, FloorLevel(driver));

        Assert.Empty(driver.Quote(1.08100m, 1.08110m));
        var breach = Assert.IsType<EquityFloorBreached>(driver.Quote(1.07990m, 1.08000m)[0]);
        Assert.Equal(99_800.00m, breach.Equity);
    }

    [Fact]
    public void HeadroomIsHowFarEquityCanFallBeforeTheFloor()
    {
        var driver = Ready();
        driver.SetFloor("max-loss", new FixedFloor(90_000m));
        driver.Buy(10.00m);

        // Equity 99 900.00 after the spread
        Assert.Equal(9_900.00m, Assert.Single(driver.Account().Floors).Headroom);
    }

    [Fact]
    public void BreachNamesTheFloorThatWasBreached()
    {
        var driver = Ready();
        driver.SetFloor("daily", new FixedFloor(96_000m));
        driver.SetFloor("max-loss", new FixedFloor(90_000m));
        driver.Buy(10.00m);

        var breach = Assert.IsType<EquityFloorBreached>(driver.Quote(1.07590m, 1.07600m)[0]);

        Assert.Equal("daily", breach.FloorId);
        Assert.Equal(95_800.00m, breach.Equity);
    }

    [Fact]
    public void FloorAboveCurrentEquityBreachesImmediately()
    {
        var driver = Ready();

        var events = driver.SetFloor("daily", new FixedFloor(100_000.01m));

        Assert.Collection(
            events,
            e => Assert.IsType<EquityFloorSet>(e),
            e => Assert.Empty(Assert.IsType<EquityFloorBreached>(e).Positions),
            e => Assert.IsType<AccountDisabled>(e));
    }

    [Fact]
    public void RemovedFloorNoLongerApplies()
    {
        var driver = Ready();
        driver.SetFloor("max-loss", new FixedFloor(99_000m));
        driver.Buy(10.00m);

        EventAssert.Single<EquityFloorRemoved>(driver.RemoveFloor("max-loss"));

        Assert.Empty(driver.Quote(1.07000m, 1.07010m));
        Assert.Equal(AccountStatus.Active, driver.Account().Status);
    }

    [Theory]
    [MemberData(nameof(InvalidRules))]
    public void InvalidFloorsAreRejected(string floorId, EquityFloorRule rule)
    {
        var driver = Ready();

        Assert.Equal(RejectReason.InvalidFloor, EventAssert.Rejected(driver.SetFloor(floorId, rule)));
    }

    public static TheoryData<string, EquityFloorRule> InvalidRules() => new()
    {
        { "", new FixedFloor(90_000m) },
        { "max-loss", new FixedFloor(-1m) },
        { "max-loss", new TrailingFloor(0m) },
        { "max-loss", new TrailingFloor(5_000m, LockLevel: -1m) },
    };

    private static decimal FloorLevel(EngineDriver driver) => Assert.Single(driver.Account().Floors).Level;

    private static EngineDriver Ready()
    {
        var driver = new EngineDriver();
        driver.CreateAccount();
        driver.Quote(1.08000m, 1.08010m);
        return driver;
    }
}
