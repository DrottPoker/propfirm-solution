using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Engine.Tests.Support;

namespace Trading.Engine.Tests;

public sealed class GroupTests
{
    // Commission 5 per lot and side, 10 points of markup and leverage 50, unlike the configured group.
    private static readonly TradingGroup Firm = new(
        "firm-standard",
        "USD",
        50m,
        [new SymbolConditions("XAUUSD", 20, 0, 0m), new SymbolConditions("EURUSD", 50, 10, 5m)]);

    [Fact]
    public void CreatedGroupTradesWithItsOwnConditions()
    {
        var driver = new EngineDriver();

        var created = EventAssert.Single<GroupCreated>(driver.Apply(t => new CreateGroup(t, Firm)));
        driver.CreateAccount(groupId: "firm-standard");
        driver.Quote(1.08000m, 1.08010m);
        var opened = Assert.IsType<PositionOpened>(driver.Buy(1.00m)[0]);

        Assert.Equal(["EURUSD", "XAUUSD"], created.Group.Symbols.Select(s => s.Symbol));
        Assert.Equal(1.08015m, opened.OpenPrice);
        Assert.Equal(5m, opened.Commission);
        var price = Assert.Single(driver.Prices("firm-standard")!);
        Assert.Equal(("EURUSD", 1.07995m, 1.08015m), (price.Symbol, price.Bid, price.Ask));
    }

    [Fact]
    public void GroupIdsAreUnique()
    {
        var driver = new EngineDriver();
        driver.Apply(t => new CreateGroup(t, Firm));

        Assert.Equal(RejectReason.DuplicateId, EventAssert.Rejected(driver.Apply(t => new CreateGroup(t, Firm))));
        Assert.Equal(RejectReason.DuplicateId, EventAssert.Rejected(driver.Apply(t => new CreateGroup(t, Firm with { Id = "standard" }))));
    }

    [Theory]
    [MemberData(nameof(InvalidGroups))]
    public void InvalidGroupIsRejected(TradingGroup group, RejectReason expected)
    {
        var driver = new EngineDriver();

        Assert.Equal(expected, EventAssert.Rejected(driver.Apply(t => new CreateGroup(t, group))));
        Assert.Equal(RejectReason.UnknownGroup, EventAssert.Rejected(driver.CreateAccount(groupId: group.Id)));
    }

    public static TheoryData<TradingGroup, RejectReason> InvalidGroups() => new()
    {
        { Firm with { Id = "" }, RejectReason.InvalidId },
        { Firm with { Currency = "" }, RejectReason.InvalidGroup },
        { Firm with { StopOutLevelPercent = -1m }, RejectReason.InvalidGroup },
        { Firm with { Symbols = [new SymbolConditions("BTCUSD", 100, 0, 0m)] }, RejectReason.InvalidGroup },
        { Firm with { Symbols = [new SymbolConditions("EURUSD", 0, 0, 0m)] }, RejectReason.InvalidGroup },
        { Firm with { Symbols = [new SymbolConditions("EURUSD", 100, -1, 0m)] }, RejectReason.InvalidGroup },
        { Firm with { Symbols = [new SymbolConditions("EURUSD", 100, 0, -1m)] }, RejectReason.InvalidGroup },
        { Firm with { Symbols = [new SymbolConditions("EURUSD", 100, 0, 0m), new SymbolConditions("EURUSD", 50, 0, 0m)] }, RejectReason.InvalidGroup },
    };

    [Fact]
    public void RestoredEngineKeepsCreatedGroupsButNotConfiguredOnes()
    {
        var configuration = TestMarket.Configuration();
        var engine = new TradingEngine(configuration);
        var t = TestMarket.Start;
        engine.Apply(new CreateGroup(t, Firm));
        engine.Apply(new CreateGroup(t, Firm with { Id = "other-standard" }));
        engine.Apply(new CreateAccount(t, "A1", "firm-standard", 100_000m));
        engine.Apply(new Quote(t, "EURUSD", 1.08000m, 1.08010m));
        engine.Apply(new PlaceOrder(t, "A1", "O1", "EURUSD", Side.Buy, OrderType.Market, 1.00m));

        var exported = engine.ExportState();
        var restored = TradingEngine.FromState(configuration, exported);

        Assert.Equal(["firm-standard", "other-standard"], exported.Groups!.Select(g => g.Id));
        Assert.Equal(StateJson.Serialize(exported), StateJson.Serialize(restored.ExportState()));
        Assert.Equal(EventJson.Serialize(engine.GetAccount("A1")!), EventJson.Serialize(restored.GetAccount("A1")!));
        Assert.Equal(RejectReason.DuplicateId, EventAssert.Rejected(restored.Apply(new CreateGroup(t, Firm))));
    }

    [Fact]
    public void SnapshotFromBeforeCreatedGroupsStillRestores()
    {
        var configuration = TestMarket.Configuration();
        var engine = new TradingEngine(configuration);
        engine.Apply(new CreateAccount(TestMarket.Start, "A1", "standard", 100_000m));

        var restored = TradingEngine.FromState(configuration, engine.ExportState() with { Groups = null });

        Assert.Equal(100_000m, restored.GetAccount("A1")!.Balance);
    }

    [Fact]
    public void CreatedGroupThatIsNowConfiguredIsRejected()
    {
        var engine = new TradingEngine(TestMarket.Configuration());
        engine.Apply(new CreateGroup(TestMarket.Start, Firm));
        var state = engine.ExportState();

        var configured = TestMarket.Configuration() with
        {
            Groups = [.. TestMarket.Configuration().Groups, Firm],
        };
        var exception = Assert.Throws<InvalidOperationException>(() => TradingEngine.FromState(configured, state));

        Assert.Contains("firm-standard", exception.Message, StringComparison.Ordinal);
    }
}
