using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Engine.Tests.Support;

namespace Trading.Engine.Tests;

/// <summary>The trader's own limits, the lock until the next trading day and the day they count in (ADR 0054).</summary>
public sealed class OwnLimitsTests
{
    private const string Account = EngineDriver.AccountId;

    // The test clock starts on Monday 5 October 2026 at 08:00 UTC, so the next UTC day starts in 16 hours.
    private static readonly DateTimeOffset Midnight = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<EngineEvent> SetLimits(EngineDriver driver, decimal? dailyLoss = null, decimal? dailyTarget = null, int? maxTrades = null) =>
        driver.Apply(t => new SetOwnLimits(t, Account, new OwnLimits(dailyLoss, dailyTarget, maxTrades)));

    private static IReadOnlyList<EngineEvent> Lock(EngineDriver driver, bool closePositions) => driver.Apply(t => new LockTrading(t, Account, closePositions));

    private static EngineDriver Trading()
    {
        var driver = new EngineDriver();
        driver.CreateAccount();
        driver.Quote(1.08000m, 1.08000m);
        return driver;
    }

    [Fact]
    public void TheDailyLossLimitClosesEveryPositionAndLocksNewOrdersUntilTheNextDay()
    {
        var driver = Trading();
        SetLimits(driver, dailyLoss: 1_000m);
        driver.Buy(1.00m, "P1");
        driver.Buy(0.50m, "P2");

        Assert.Empty(driver.Quote(1.07400m, 1.07400m));
        var events = driver.Quote(1.07330m, 1.07330m);

        Assert.Collection(
            events,
            e => Assert.Equal((LockReason.DailyLoss, 99_000m, 98_995m), (Assert.IsType<OwnLimitReached>(e).Limit, ((OwnLimitReached)e).Level, ((OwnLimitReached)e).Equity)),
            e => Assert.Equal((CloseReason.OwnLimit, "P1"), (Assert.IsType<PositionClosed>(e).Reason, ((PositionClosed)e).PositionId)),
            e => Assert.Equal(CloseReason.OwnLimit, Assert.IsType<PositionClosed>(e).Reason),
            e => Assert.Equal(new TradingLocked(e.Timestamp, Account, LockReason.DailyLoss, Midnight, 1_000m, -1_005m, 2), e));
        var account = driver.Account();
        Assert.Equal(AccountStatus.Active, account.Status);
        Assert.Equal(new OwnLock(Midnight, LockReason.DailyLoss), account.OwnLimits.Lock);
        Assert.Equal(RejectReason.AccountLocked, EventAssert.Rejected(driver.Buy(0.10m, "P3")));

        // The first price of the next day ends the lock, and the day counts from the balance it started with.
        var nextDay = driver.Quote(1.07330m, 1.07330m, after: Midnight - driver.Now);
        Assert.Collection(
            nextDay,
            e => Assert.Equal(98_995m, Assert.IsType<TradingDayStarted>(e).DayStartBalance),
            e => Assert.IsType<TradingUnlocked>(e));
        Assert.Equal((97_995m, Midnight.AddDays(1)), (driver.Account().OwnLimits.LossLevel, driver.Account().OwnLimits.NextDayStart));
        Assert.IsType<PositionOpened>(Assert.Single(driver.Buy(0.10m, "P3")));
    }

    [Fact]
    public void TheDailyProfitTargetClosesAndLocksWhileTheTraderIsAhead()
    {
        var driver = Trading();
        SetLimits(driver, dailyTarget: 500m);
        driver.Buy(1.00m, "P1");

        var events = driver.Quote(1.08500m, 1.08500m);

        Assert.Equal(LockReason.DailyTarget, Assert.IsType<OwnLimitReached>(events[0]).Limit);
        Assert.Equal(500m, Assert.IsType<PositionClosed>(events[1]).Profit);
        Assert.Equal(LockReason.DailyTarget, Assert.IsType<TradingLocked>(events[^1]).Reason);
        Assert.Equal(100_500m, driver.Account().Balance);
    }

    [Fact]
    public void TheFirmsLimitComesFirstWhenOnePriceBreaksBoth()
    {
        var driver = Trading();
        driver.SetFloor("daily", new FixedFloor(99_500m));
        SetLimits(driver, dailyLoss: 400m);
        driver.Buy(1.00m, "P1");

        var events = driver.Quote(1.07000m, 1.07000m);

        Assert.IsType<EquityFloorBreached>(events[0]);
        Assert.DoesNotContain(events, e => e is OwnLimitReached or TradingLocked);
        Assert.Equal(AccountStatus.Disabled, driver.Account().Status);
    }

    [Fact]
    public void ALimitTheDayHasAlreadyPassedIsReachedAtOnce()
    {
        var driver = Trading();
        driver.Buy(1.00m, "P1");
        driver.Quote(1.07800m, 1.07800m);

        var events = SetLimits(driver, dailyLoss: 150m);

        Assert.IsType<OwnLimitsSet>(events[0]);
        Assert.Equal(LockReason.DailyLoss, Assert.IsType<OwnLimitReached>(events[1]).Limit);
        Assert.IsType<TradingLocked>(events[^1]);
    }

    [Fact]
    public void AStricterLimitAppliesNowAndALooserOneFromTheNextDay()
    {
        var driver = Trading();
        SetLimits(driver, dailyLoss: 1_500m, dailyTarget: 1_000m, maxTrades: 6);

        var looser = EventAssert.Single<OwnLimitsSet>(SetLimits(driver, dailyLoss: 2_000m, dailyTarget: 800m, maxTrades: null));

        Assert.Equal(new OwnLimits(1_500m, 800m, 6), looser.Limits);
        Assert.Equal(new OwnLimits(2_000m, 800m, null), looser.Pending);

        // Asking for the limits as they are now takes back the loosening.
        var back = EventAssert.Single<OwnLimitsSet>(SetLimits(driver, dailyLoss: 1_500m, dailyTarget: 800m, maxTrades: 6));
        Assert.Null(back.Pending);

        SetLimits(driver, dailyLoss: 1_800m, dailyTarget: 800m, maxTrades: 6);
        var nextDay = driver.Quote(1.08000m, 1.08000m, after: Midnight - driver.Now);
        Assert.Equal(new OwnLimits(1_800m, 800m, 6), EventAssert.Single<TradingDayStarted>(nextDay).Limits);
        Assert.Null(driver.Account().OwnLimits.Pending);
    }

    [Fact]
    public void LimitsMustBeAmountsAboveZeroAndACountTheEngineAllows()
    {
        var driver = Trading();

        Assert.Equal(RejectReason.InvalidLimits, EventAssert.Rejected(SetLimits(driver, dailyLoss: 0m)));
        Assert.Equal(RejectReason.InvalidLimits, EventAssert.Rejected(SetLimits(driver, dailyTarget: 10.001m)));
        Assert.Equal(RejectReason.InvalidLimits, EventAssert.Rejected(SetLimits(driver, maxTrades: 0)));
        Assert.Equal(RejectReason.InvalidLimits, EventAssert.Rejected(SetLimits(driver, maxTrades: OwnLimits.MostTrades + 1)));
    }

    [Fact]
    public void TheTradeCountRefusesNewOrdersAndCancelsAPendingOrderThatWouldOpenOneMore()
    {
        var driver = Trading();
        SetLimits(driver, maxTrades: 2);
        driver.Pending(OrderType.Limit, Side.Buy, 0.10m, 1.07900m, "L1");
        driver.Buy(0.10m, "P1");
        driver.Buy(0.10m, "P2");

        Assert.Equal(RejectReason.TradeLimitReached, EventAssert.Rejected(driver.Buy(0.10m, "P3")));
        var triggered = driver.Quote(1.07900m, 1.07900m);
        Assert.Equal(CancelReason.TradeLimit, EventAssert.Single<OrderCancelled>(triggered).Reason);
        Assert.Equal(2, driver.Account().OwnLimits.TradesToday);
        Assert.DoesNotContain(driver.Close("P1"), e => e is InputRejected);

        driver.Quote(1.07900m, 1.07900m, after: Midnight - driver.Now);
        Assert.Equal(0, driver.Account().OwnLimits.TradesToday);
        Assert.IsType<PositionOpened>(Assert.Single(driver.Buy(0.10m, "P3")));
    }

    [Fact]
    public void TheTraderLocksTheRestOfTheDayAndChoosesWhetherPositionsClose()
    {
        var closing = Trading();
        closing.Buy(1.00m, "P1");
        closing.Pending(OrderType.Limit, Side.Buy, 0.10m, 1.07000m, "L1");

        var closed = Lock(closing, closePositions: true);

        Assert.Collection(
            closed,
            e => Assert.Equal(CloseReason.OwnLimit, Assert.IsType<PositionClosed>(e).Reason),
            e => Assert.Equal(CancelReason.TradingLocked, Assert.IsType<OrderCancelled>(e).Reason),
            e => Assert.Equal(new TradingLocked(e.Timestamp, Account, LockReason.Trader, Midnight, null, 0m, 1), e));
        Assert.Equal(RejectReason.AccountLocked, EventAssert.Rejected(Lock(closing, closePositions: false)));

        var keeping = Trading();
        keeping.Buy(1.00m, "P1");
        Assert.IsType<TradingLocked>(Assert.Single(Lock(keeping, closePositions: false)));
        Assert.Equal(RejectReason.AccountLocked, EventAssert.Rejected(keeping.Buy(0.10m, "P2")));
        Assert.IsType<PositionModified>(Assert.Single(keeping.Modify("P1", 1.07000m, null)));
        Assert.IsType<PositionClosed>(Assert.Single(keeping.Close("P1")));
    }

    [Fact]
    public void ALockKeepsAPositionWhoseMarketHasNoFreshPrice()
    {
        var driver = Trading();
        driver.Buy(1.00m, "P1");
        driver.Quote(1.08000m, 1.08000m, "GBPUSD", after: TimeSpan.FromSeconds(10));

        var events = Lock(driver, closePositions: true);

        Assert.IsType<TradingLocked>(Assert.Single(events));
        Assert.Single(driver.Account().Positions);
    }

    [Fact]
    public void TheTradingDayFollowsTheFirmsClockAndDepositsMoveTheStart()
    {
        var driver = Trading();
        var day = new TradingDay("Europe/Stockholm", new TimeOnly(0, 0));

        var set = EventAssert.Single<TradingDaySet>(driver.Apply(t => new SetTradingDay(t, Account, day)));

        // 00:00 in Stockholm is 22:00 UTC in October, summer time.
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 22, 0, 0, TimeSpan.Zero), set.NextDayStart);
        Assert.Equal(RejectReason.InvalidTradingDay, EventAssert.Rejected(driver.Apply(t => new SetTradingDay(t, Account, new TradingDay("Mars/Olympus", TimeOnly.MinValue)))));

        SetLimits(driver, dailyLoss: 1_000m);
        driver.AdjustBalance(500m);
        Assert.Equal((100_500m, (decimal?)99_500m), (driver.Account().OwnLimits.DayStartBalance, driver.Account().OwnLimits.LossLevel));
    }

    [Fact]
    public void ReopeningTheAccountEndsALock()
    {
        var driver = Trading();
        Lock(driver, closePositions: false);
        driver.CloseAccount();

        var events = driver.Apply(t => new ReopenAccount(t, Account, 90_000m));

        Assert.IsType<TradingUnlocked>(events[^1]);
        Assert.Equal((90_000m, (OwnLock?)null), (driver.Account().OwnLimits.DayStartBalance, driver.Account().OwnLimits.Lock));
    }

    [Fact]
    public void AnAccountWithoutOwnLimitsStartsItsDayWithoutAnEvent()
    {
        var driver = Trading();
        driver.Buy(1.00m, "P1");

        Assert.Empty(driver.Quote(1.08000m, 1.08000m, after: Midnight - driver.Now));
        Assert.Equal(Midnight.AddDays(1), driver.Account().OwnLimits.NextDayStart);
    }

    [Fact]
    public void LimitsAndALockSurviveARestart()
    {
        var configuration = TestMarket.Configuration();
        var engine = new TradingEngine(configuration);
        var t = TestMarket.Start;
        engine.Apply(new CreateAccount(t, Account, "standard", 100_000m));
        engine.Apply(new Quote(t, "EURUSD", 1.08000m, 1.08000m));
        engine.Apply(new SetTradingDay(t, Account, new TradingDay("America/New_York", new TimeOnly(17, 0))));
        engine.Apply(new SetOwnLimits(t, Account, new OwnLimits(1_500m, null, 4)));
        engine.Apply(new SetOwnLimits(t, Account, new OwnLimits(2_000m, null, 4)));
        engine.Apply(new LockTrading(t, Account, false));

        var exported = engine.ExportState();
        var restored = TradingEngine.FromState(configuration, exported);

        Assert.Equal(StateJson.Serialize(exported), StateJson.Serialize(restored.ExportState()));
        Assert.Equal(EventJson.Serialize(engine.GetAccount(Account)!), EventJson.Serialize(restored.GetAccount(Account)!));
    }
}
