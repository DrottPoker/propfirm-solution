using Trading.Engine.Events;
using Trading.Engine.Inputs;

namespace Trading.Engine.Tests.Support;

/// <summary>Drives the engine with a test clock. Each input is 100 ms after the previous one unless told otherwise.</summary>
internal sealed class EngineDriver(EngineConfiguration configuration)
{
    public const string AccountId = "A1";

    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(100);

    private readonly TradingEngine _engine = new(configuration);

    public EngineDriver()
        : this(TestMarket.Configuration())
    {
    }

    public DateTimeOffset Now { get; private set; } = TestMarket.Start;

    public IReadOnlyList<EngineEvent> Apply(Func<DateTimeOffset, EngineInput> input, TimeSpan? after = null)
    {
        Now += after ?? Step;
        return _engine.Apply(input(Now));
    }

    public IReadOnlyList<EngineEvent> CreateAccount(decimal balance = 100_000m, string accountId = AccountId, string groupId = "standard") =>
        Apply(t => new CreateAccount(t, accountId, groupId, balance));

    public IReadOnlyList<EngineEvent> Quote(decimal bid, decimal ask, string symbol = "EURUSD", TimeSpan? after = null) =>
        Apply(t => new Quote(t, symbol, bid, ask), after);

    public IReadOnlyList<EngineEvent> Buy(decimal volume, string orderId = "O1", string symbol = "EURUSD", decimal? stopLoss = null, decimal? takeProfit = null, TimeSpan? after = null) =>
        Apply(t => new PlaceOrder(t, AccountId, orderId, symbol, Side.Buy, OrderType.Market, volume, null, stopLoss, takeProfit), after);

    public IReadOnlyList<EngineEvent> Sell(decimal volume, string orderId = "O1", string symbol = "EURUSD", decimal? stopLoss = null, decimal? takeProfit = null) =>
        Apply(t => new PlaceOrder(t, AccountId, orderId, symbol, Side.Sell, OrderType.Market, volume, null, stopLoss, takeProfit));

    public IReadOnlyList<EngineEvent> Pending(OrderType type, Side side, decimal volume, decimal? price, string orderId = "O1", decimal? stopLoss = null, decimal? takeProfit = null) =>
        Apply(t => new PlaceOrder(t, AccountId, orderId, "EURUSD", side, type, volume, price, stopLoss, takeProfit));

    public IReadOnlyList<EngineEvent> CancelOrder(string orderId) => Apply(t => new CancelOrder(t, AccountId, orderId));

    public IReadOnlyList<EngineEvent> Close(string positionId) => Apply(t => new ClosePosition(t, AccountId, positionId));

    public IReadOnlyList<EngineEvent> Modify(string positionId, decimal? stopLoss, decimal? takeProfit) =>
        Apply(t => new ModifyPosition(t, AccountId, positionId, stopLoss, takeProfit));

    public IReadOnlyList<EngineEvent> SetFloor(string floorId, EquityFloorRule rule) => Apply(t => new SetEquityFloor(t, AccountId, floorId, rule));

    public IReadOnlyList<EngineEvent> RemoveFloor(string floorId) => Apply(t => new RemoveEquityFloor(t, AccountId, floorId));

    public IReadOnlyList<EngineEvent> CloseAccount() => Apply(t => new CloseAccount(t, AccountId));

    public IReadOnlyList<EngineEvent> AdjustBalance(decimal amount, string operationId = "B1", decimal? minBalance = null, string accountId = AccountId) =>
        Apply(t => new AdjustBalance(t, accountId, operationId, amount, minBalance));

    public IReadOnlyList<SymbolPrice>? Prices(string groupId = "standard") => _engine.GetPrices(groupId);

    public PointValue? PointValue(string symbol = "EURUSD", string accountId = AccountId) => _engine.GetPointValue(accountId, symbol);

    public AccountSnapshot Account(string accountId = AccountId) =>
        _engine.GetAccount(accountId) ?? throw new InvalidOperationException($"Account {accountId} does not exist.");
}

internal static class EventAssert
{
    public static T Single<T>(IReadOnlyList<EngineEvent> events)
        where T : EngineEvent => Assert.IsType<T>(Assert.Single(events));

    public static RejectReason Rejected(IReadOnlyList<EngineEvent> events) => Single<InputRejected>(events).Reason;
}
