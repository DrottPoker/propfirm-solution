using Trading.Engine.Inputs;
using Trading.Engine.Tests.Support;

namespace Trading.Engine.Tests;

public sealed class StateTests
{
    // Restarts while positions, pending orders, floors and a disabled account exist.
    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(200)]
    [InlineData(1_201)]
    [InlineData(2_999)]
    public void RestoredEngineContinuesExactlyLikeTheOriginal(int restartAtTick)
    {
        Assert.Equal(ReplayScenario.Run(), ReplayScenario.Run(restartAtTick));
    }

    [Fact]
    public void RestartingManyTimesChangesNothing()
    {
        Assert.Equal(ReplayScenario.Run(), ReplayScenario.Run(100, 101, 500, 1_000, 2_000, 2_600));
    }

    [Fact]
    public void ExportOfARestoredEngineIsIdentical()
    {
        var configuration = TestMarket.Configuration();
        var engine = new TradingEngine(configuration);
        var t = TestMarket.Start;
        engine.Apply(new CreateAccount(t, "A1", "standard", 100_000m));
        engine.Apply(new Quote(t, "EURUSD", 1.08000m, 1.08010m));
        engine.Apply(new SetEquityFloor(t, "A1", "max-loss", new TrailingFloor(5_000m, LockLevel: 100_000m)));
        engine.Apply(new SetEquityFloor(t, "A1", "daily", new AnchoredFloor(5_000m, FloorAnchor.HigherOfBalanceAndEquity)));
        engine.Apply(new PlaceOrder(t, "A1", "O1", "EURUSD", Side.Buy, OrderType.Market, 1.00m, null, 1.07000m, null));
        engine.Apply(new PlaceOrder(t, "A1", "O2", "EURUSD", Side.Sell, OrderType.Limit, 0.50m, 1.09000m));

        var exported = engine.ExportState();
        var restored = TradingEngine.FromState(configuration, exported);

        Assert.Equal(StateJson.Serialize(exported), StateJson.Serialize(restored.ExportState()));
        Assert.Equal(EventJson.Serialize(engine.GetAccount("A1")!), EventJson.Serialize(restored.GetAccount("A1")!));
    }

    [Fact]
    public void RestoredEngineKeepsItsClock()
    {
        var configuration = TestMarket.Configuration();
        var engine = new TradingEngine(configuration);
        engine.Apply(new Quote(TestMarket.Start.AddMinutes(5), "EURUSD", 1.08000m, 1.08010m));

        var restored = TradingEngine.FromState(configuration, engine.ExportState());
        var events = restored.Apply(new CreateAccount(TestMarket.Start, "A1", "standard", 1_000m));

        Assert.Equal(RejectReason.OutOfOrder, EventAssert.Rejected(events));
    }

    [Fact]
    public void StateThatDoesNotFitTheConfigurationIsRejected()
    {
        var engine = new TradingEngine(TestMarket.Configuration());
        var t = TestMarket.Start;
        engine.Apply(new CreateAccount(t, "A1", "standard", 100_000m));
        engine.Apply(new Quote(t, "XAUUSD", 2650.00m, 2650.30m));
        engine.Apply(new PlaceOrder(t, "A1", "O1", "XAUUSD", Side.Buy, OrderType.Market, 0.10m));
        var state = engine.ExportState();

        var withoutGold = TestMarket.Configuration(tradableSymbols: ["EURUSD"]);
        var exception = Assert.Throws<InvalidOperationException>(() => TradingEngine.FromState(withoutGold, state));

        Assert.Contains("XAUUSD", exception.Message, StringComparison.Ordinal);
    }
}
