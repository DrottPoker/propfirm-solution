using Trading.Engine.Events;
using Trading.Engine.Tests.Support;

namespace Trading.Engine.Tests;

public sealed class AccountTests
{
    [Fact]
    public void NewAccountHasItsBalanceAsEquity()
    {
        var driver = new EngineDriver();

        var created = EventAssert.Single<AccountCreated>(driver.CreateAccount(50_000m));

        Assert.Equal("USD", created.Currency);
        var account = driver.Account();
        Assert.Equal(50_000m, account.Balance);
        Assert.Equal(50_000m, account.Equity);
        Assert.Null(account.MarginLevelPercent);
    }

    [Fact]
    public void CloseAccountLiquidatesAndDisables()
    {
        var driver = new EngineDriver();
        driver.CreateAccount();
        driver.Quote(1.08000m, 1.08010m);
        driver.Buy(1.00m, "O1");
        driver.Pending(OrderType.Limit, Side.Buy, 1.00m, 1.07000m, "O2");

        var events = driver.CloseAccount();

        Assert.Collection(
            events,
            e => Assert.Equal(CloseReason.AccountClosed, Assert.IsType<PositionClosed>(e).Reason),
            e => Assert.Equal(CancelReason.AccountClosed, Assert.IsType<OrderCancelled>(e).Reason),
            e => Assert.Equal(DisableReason.Closed, Assert.IsType<AccountDisabled>(e).Reason));
        Assert.Equal(99_990.00m, driver.Account().Balance);
        Assert.Equal(RejectReason.AccountDisabled, EventAssert.Rejected(driver.CloseAccount()));
    }
}
