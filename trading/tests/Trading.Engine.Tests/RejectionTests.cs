using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Engine.Tests.Support;

namespace Trading.Engine.Tests;

public sealed class RejectionTests
{
    [Fact]
    public void InputEarlierThanThePreviousIsRejected()
    {
        var driver = Ready();

        var events = driver.Buy(1.00m, after: TimeSpan.FromMilliseconds(-1));

        Assert.Equal(RejectReason.OutOfOrder, EventAssert.Rejected(events));
        Assert.Empty(driver.Account().Positions);
    }

    [Fact]
    public void RejectedInputIsIncludedInTheEvent()
    {
        var driver = Ready();

        var rejected = EventAssert.Single<InputRejected>(driver.Buy(0.001m, "O9"));

        var input = Assert.IsType<PlaceOrder>(rejected.Input);
        Assert.Equal("O9", input.OrderId);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.001")]
    [InlineData("0.015")]
    [InlineData("100.01")]
    public void VolumeMustBeWithinLimitsAndOnTheStep(string volume)
    {
        var driver = Ready();

        Assert.Equal(RejectReason.InvalidVolume, EventAssert.Rejected(driver.Buy(Decimals.Parse(volume))));
    }

    [Fact]
    public void OrderIdsAreNeverReused()
    {
        var driver = Ready();
        driver.Buy(1.00m, "O1");
        driver.Close("O1");

        Assert.Equal(RejectReason.DuplicateId, EventAssert.Rejected(driver.Buy(1.00m, "O1")));
    }

    [Fact]
    public void MarketOrderMustNotHaveAPrice()
    {
        var driver = Ready();

        Assert.Equal(RejectReason.InvalidPrice, EventAssert.Rejected(driver.Pending(OrderType.Market, Side.Buy, 1.00m, 1.08010m)));
    }

    [Fact]
    public void OrderNeedsAPrice()
    {
        var driver = new EngineDriver();
        driver.CreateAccount();

        Assert.Equal(RejectReason.NoPrice, EventAssert.Rejected(driver.Buy(1.00m)));
    }

    [Fact]
    public void UnknownAndUntradableSymbolsAreRejected()
    {
        var driver = new EngineDriver(TestMarket.Configuration(tradableSymbols: ["EURUSD"]));
        driver.CreateAccount();
        driver.Quote(2650.00m, 2650.30m, "XAUUSD");

        Assert.Equal(RejectReason.UnknownSymbol, EventAssert.Rejected(driver.Buy(1.00m, symbol: "BTCUSD")));
        Assert.Equal(RejectReason.SymbolNotTradable, EventAssert.Rejected(driver.Buy(1.00m, symbol: "XAUUSD")));
    }

    [Fact]
    public void CommandsForUnknownAccountsOrItemsAreRejected()
    {
        var driver = Ready();

        Assert.Equal(RejectReason.UnknownAccount, EventAssert.Rejected(driver.Apply(t => new PlaceOrder(t, "missing", "O1", "EURUSD", Side.Buy, OrderType.Market, 1.00m))));
        Assert.Equal(RejectReason.UnknownOrder, EventAssert.Rejected(driver.CancelOrder("missing")));
        Assert.Equal(RejectReason.UnknownPosition, EventAssert.Rejected(driver.Close("missing")));
        Assert.Equal(RejectReason.UnknownFloor, EventAssert.Rejected(driver.RemoveFloor("missing")));
    }

    [Theory]
    [InlineData("A1", "standard", "100", RejectReason.DuplicateId)]
    [InlineData("", "standard", "100", RejectReason.InvalidId)]
    [InlineData("A2", "missing", "100", RejectReason.UnknownGroup)]
    [InlineData("A2", "standard", "-1", RejectReason.InvalidAmount)]
    [InlineData("A2", "standard", "100.005", RejectReason.InvalidAmount)]
    public void InvalidAccountsAreRejected(string accountId, string groupId, string balance, RejectReason expected)
    {
        var driver = Ready();

        Assert.Equal(expected, EventAssert.Rejected(driver.CreateAccount(Decimals.Parse(balance), accountId, groupId)));
    }

    [Theory]
    [InlineData("BTCUSD", "1.00000", "1.00010", RejectReason.UnknownSymbol)]
    [InlineData("EURUSD", "1.08010", "1.08000", RejectReason.InvalidQuote)]
    [InlineData("EURUSD", "0", "1.08000", RejectReason.InvalidQuote)]
    [InlineData("EURUSD", "1.080005", "1.08010", RejectReason.InvalidQuote)]
    public void InvalidQuotesAreRejected(string symbol, string bid, string ask, RejectReason expected)
    {
        var driver = Ready();

        Assert.Equal(expected, EventAssert.Rejected(driver.Quote(Decimals.Parse(bid), Decimals.Parse(ask), symbol)));
    }

    private static EngineDriver Ready()
    {
        var driver = new EngineDriver();
        driver.CreateAccount();
        driver.Quote(1.08000m, 1.08010m);
        return driver;
    }
}
