using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Engine.Tests.Support;

namespace Trading.Engine.Tests;

public sealed class BalanceTests
{
    [Fact]
    public void DepositsAndWithdrawalsChangeTheBalance()
    {
        var driver = Ready();

        var deposit = EventAssert.Single<BalanceAdjusted>(driver.AdjustBalance(5_000m, "D1"));
        var withdrawal = EventAssert.Single<BalanceAdjusted>(driver.AdjustBalance(-2_000.50m, "W1"));

        Assert.Equal(("D1", 5_000m, 105_000m), (deposit.OperationId, deposit.Amount, deposit.BalanceAfter));
        Assert.Equal(("W1", -2_000.50m, 102_999.50m), (withdrawal.OperationId, withdrawal.Amount, withdrawal.BalanceAfter));
        Assert.Equal((102_999.50m, 102_999.50m), (driver.Account().Balance, driver.Account().Equity));
    }

    [Fact]
    public void AnOperationIsNeverAppliedTwice()
    {
        var driver = Ready();
        driver.AdjustBalance(-1_000m, "W1");

        Assert.Equal(RejectReason.DuplicateId, EventAssert.Rejected(driver.AdjustBalance(-1_000m, "W1")));
        Assert.Equal(99_000m, driver.Account().Balance);
    }

    [Fact]
    public void ARefusedOperationCanBeTriedAgain()
    {
        var driver = Ready();
        driver.AdjustBalance(-5_000m, "W1", minBalance: 96_000m);

        EventAssert.Single<BalanceAdjusted>(driver.AdjustBalance(-5_000m, "W1", minBalance: 95_000m));
    }

    [Theory]
    [InlineData("-5000", "96000", false)]
    [InlineData("-5000", "95000", true)]
    [InlineData("-100000", null, true)]
    [InlineData("-100000.01", null, false)]
    public void AWithdrawalKeepsTheMinimumBalance(string amount, string? minBalance, bool allowed)
    {
        var driver = Ready();

        var events = driver.AdjustBalance(Decimals.Parse(amount), minBalance: Decimals.ParseOptional(minBalance));

        if (allowed)
        {
            EventAssert.Single<BalanceAdjusted>(events);
        }
        else
        {
            Assert.Equal(RejectReason.InsufficientFunds, EventAssert.Rejected(events));
            Assert.Equal(100_000m, driver.Account().Balance);
        }
    }

    [Fact]
    public void AWithdrawalMustFitInTheFreeMargin()
    {
        var driver = Ready();
        driver.Buy(10.00m);
        var freeMargin = driver.Account().FreeMargin;

        Assert.Equal(RejectReason.InsufficientFunds, EventAssert.Rejected(driver.AdjustBalance(-(freeMargin + 0.01m), "W1")));
        EventAssert.Single<BalanceAdjusted>(driver.AdjustBalance(-freeMargin, "W2"));
    }

    [Fact]
    public void AWithdrawalMayNotTakeEquityBelowAFixedFloor()
    {
        var driver = Ready();
        driver.SetFloor("max-loss", new FixedFloor(90_000m));

        Assert.Equal(RejectReason.InsufficientFunds, EventAssert.Rejected(driver.AdjustBalance(-10_000.01m, "W1")));

        // Equity exactly at the floor is allowed, as on a price.
        EventAssert.Single<BalanceAdjusted>(driver.AdjustBalance(-10_000m, "W2"));
        Assert.Equal(AccountStatus.Active, driver.Account().Status);
    }

    // A withdrawal is not a loss: floors measured from the account move with it, fixed levels and lock levels stay.
    [Fact]
    public void FloorsMeasuredFromTheAccountMoveWithTheBalance()
    {
        var driver = Ready();
        var position = EventAssert.Single<PositionOpened>(driver.Buy(10.00m));
        driver.Quote(1.08510m, 1.08520m);
        driver.Close(position.PositionId);
        driver.SetFloor("daily", new AnchoredFloor(5_000m, FloorAnchor.Balance));
        driver.SetFloor("max-loss", new TrailingFloor(10_000m, LockLevel: 100_000m));
        driver.SetFloor("fixed", new FixedFloor(90_000m));
        Assert.Equal((100_000m, 95_000m, 90_000m), Levels(driver));

        EventAssert.Single<BalanceAdjusted>(driver.AdjustBalance(-5_000m, "W1", minBalance: 100_000m));

        Assert.Equal((95_000m, 90_000m, 90_000m), Levels(driver));
        Assert.Equal(100_000m, driver.Account().Floors.Single(f => f.FloorId == "max-loss").HighWaterMark);
    }

    [Fact]
    public void ATrailingFloorAtItsLockLevelMovesDownWithAWithdrawal()
    {
        var driver = Ready();
        driver.SetFloor("max-loss", new TrailingFloor(10_000m, LockLevel: 100_000m));
        driver.AdjustBalance(12_000m, "D1");
        Assert.Equal(100_000m, Assert.Single(driver.Account().Floors).Level);

        EventAssert.Single<BalanceAdjusted>(driver.AdjustBalance(-12_000m, "W1"));

        Assert.Equal(90_000m, Assert.Single(driver.Account().Floors).Level);
    }

    [Theory]
    [InlineData("", "100", null, RejectReason.InvalidId)]
    [InlineData("B1", "0", null, RejectReason.InvalidAmount)]
    [InlineData("B1", "0.001", null, RejectReason.InvalidAmount)]
    [InlineData("B1", "-100", "-1", RejectReason.InvalidAmount)]
    [InlineData("B1", "-100", "1000.001", RejectReason.InvalidAmount)]
    public void InvalidOperationsAreRejected(string operationId, string amount, string? minBalance, RejectReason expected)
    {
        var driver = Ready();

        Assert.Equal(expected, EventAssert.Rejected(driver.AdjustBalance(Decimals.Parse(amount), operationId, Decimals.ParseOptional(minBalance))));
    }

    [Fact]
    public void OnlyActiveAccountsTakeOperations()
    {
        var driver = Ready();
        driver.CloseAccount();

        Assert.Equal(RejectReason.AccountDisabled, EventAssert.Rejected(driver.AdjustBalance(100m)));
        Assert.Equal(RejectReason.UnknownAccount, EventAssert.Rejected(driver.AdjustBalance(100m, accountId: "missing")));
    }

    [Fact]
    public void UsedOperationIdsSurviveARestore()
    {
        var configuration = TestMarket.Configuration();
        var engine = new TradingEngine(configuration);
        var t = TestMarket.Start;
        engine.Apply(new CreateAccount(t, "A1", "standard", 100_000m));
        engine.Apply(new AdjustBalance(t, "A1", "W1", -1_000m));

        var restored = TradingEngine.FromState(configuration, engine.ExportState());

        Assert.Equal(RejectReason.DuplicateId, EventAssert.Rejected(restored.Apply(new AdjustBalance(t, "A1", "W1", -1_000m))));
    }

    [Fact]
    public void StateFromBeforeBalanceOperationsRestores()
    {
        var configuration = TestMarket.Configuration();
        var engine = new TradingEngine(configuration);
        engine.Apply(new CreateAccount(TestMarket.Start, "A1", "standard", 100_000m));
        var state = engine.ExportState();
        var older = state with { Accounts = [.. state.Accounts.Select(a => a with { UsedOperationIds = null })] };

        var restored = TradingEngine.FromState(configuration, older);

        EventAssert.Single<BalanceAdjusted>(restored.Apply(new AdjustBalance(TestMarket.Start, "A1", "W1", -1_000m)));
    }

    private static (decimal Daily, decimal MaxLoss, decimal Fixed) Levels(EngineDriver driver)
    {
        var floors = driver.Account().Floors.ToDictionary(f => f.FloorId, f => f.Level);
        return (floors["daily"], floors["max-loss"], floors["fixed"]);
    }

    private static EngineDriver Ready()
    {
        var driver = new EngineDriver();
        driver.CreateAccount();
        driver.Quote(1.08000m, 1.08010m);
        return driver;
    }
}
