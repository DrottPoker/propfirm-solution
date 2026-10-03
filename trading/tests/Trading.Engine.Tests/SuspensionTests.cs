using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Engine.Tests.Support;

namespace Trading.Engine.Tests;

public sealed class SuspensionTests
{
    [Fact]
    public void SuspendingCancelsPendingOrdersAndKeepsPositions()
    {
        var driver = Ready();
        driver.Buy(1.00m, "O1");
        driver.Pending(OrderType.Limit, Side.Buy, 1.00m, 1.07000m, "O2");

        var events = driver.Suspend();

        Assert.Collection(
            events,
            e =>
            {
                var cancelled = Assert.IsType<OrderCancelled>(e);
                Assert.Equal(("O2", CancelReason.AccountSuspended), (cancelled.OrderId, cancelled.Reason));
            },
            e => Assert.IsType<AccountSuspended>(e));
        var account = driver.Account();
        Assert.Equal(AccountStatus.Suspended, account.Status);
        Assert.Equal("O1", Assert.Single(account.Positions).PositionId);
        Assert.Empty(account.Orders);
    }

    [Fact]
    public void ASuspendedAccountTakesNoNewOrders()
    {
        var driver = Ready();
        driver.Suspend();

        Assert.Equal(RejectReason.AccountSuspended, EventAssert.Rejected(driver.Buy(1.00m, "O1")));
        Assert.Equal(RejectReason.AccountSuspended, EventAssert.Rejected(driver.Pending(OrderType.Limit, Side.Buy, 1.00m, 1.07000m, "O2")));
    }

    [Fact]
    public void TheOwnerCanStillCloseAndProtectOpenPositions()
    {
        var driver = Ready();
        driver.Buy(1.00m, "O1");
        driver.Buy(1.00m, "O2");
        driver.Suspend();

        EventAssert.Single<PositionModified>(driver.Modify("O1", 1.07000m, null));
        EventAssert.Single<PositionClosed>(driver.Close("O2"));
    }

    [Fact]
    public void StopsAndFloorsStillHoldWhileSuspended()
    {
        var driver = Ready();
        driver.Buy(1.00m, "O1", stopLoss: 1.07900m);
        driver.Buy(1.00m, "O2");
        driver.SetFloor("max-loss", new FixedFloor(99_000m));
        driver.Suspend();

        var stopped = driver.Quote(1.07890m, 1.07900m);
        var breached = driver.Quote(1.07000m, 1.07010m);

        Assert.Equal(CloseReason.StopLoss, EventAssert.Single<PositionClosed>(stopped).Reason);
        Assert.Contains(breached, e => e is EquityFloorBreached);
        Assert.Equal(AccountStatus.Disabled, driver.Account().Status);
    }

    [Fact]
    public void MoneyCanBeMovedAndTheAccountClosedWhileSuspended()
    {
        var driver = Ready();
        driver.Suspend();

        EventAssert.Single<BalanceAdjusted>(driver.AdjustBalance(-1_000m));
        Assert.Contains(driver.CloseAccount(), e => e is AccountDisabled);
    }

    [Fact]
    public void ResumingLetsTheAccountTradeAgain()
    {
        var driver = Ready();
        driver.Suspend();

        EventAssert.Single<AccountResumed>(driver.Resume());

        Assert.Equal(AccountStatus.Active, driver.Account().Status);
        EventAssert.Single<PositionOpened>(driver.Buy(1.00m, "O1"));
    }

    [Fact]
    public void SuspendingTwiceOrResumingAnActiveAccountIsRejected()
    {
        var driver = Ready();

        Assert.Equal(RejectReason.AccountNotSuspended, EventAssert.Rejected(driver.Resume()));
        driver.Suspend();
        Assert.Equal(RejectReason.AccountSuspended, EventAssert.Rejected(driver.Suspend()));
    }

    [Fact]
    public void ADisabledAccountCanNeitherBeSuspendedNorResumed()
    {
        var driver = Ready();
        driver.CloseAccount();

        Assert.Equal(RejectReason.AccountDisabled, EventAssert.Rejected(driver.Suspend()));
        Assert.Equal(RejectReason.AccountDisabled, EventAssert.Rejected(driver.Resume()));
    }

    [Fact]
    public void TheSuspensionSurvivesASnapshot()
    {
        var engine = new TradingEngine(TestMarket.Configuration());
        engine.Apply(new CreateAccount(TestMarket.Start, EngineDriver.AccountId, "standard", 100_000m));
        engine.Apply(new SuspendAccount(TestMarket.Start, EngineDriver.AccountId));

        var restored = TradingEngine.FromState(TestMarket.Configuration(), engine.ExportState());

        Assert.Equal(AccountStatus.Suspended, restored.GetAccount(EngineDriver.AccountId)!.Status);
        EventAssert.Single<AccountResumed>(restored.Apply(new ResumeAccount(TestMarket.Start, EngineDriver.AccountId)));
    }

    private static EngineDriver Ready()
    {
        var driver = new EngineDriver();
        driver.CreateAccount();
        driver.Quote(1.08000m, 1.08010m);
        return driver;
    }
}
