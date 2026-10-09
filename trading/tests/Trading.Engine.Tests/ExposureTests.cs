using Trading.Engine.Inputs;
using Trading.Engine.Tests.Support;

namespace Trading.Engine.Tests;

public sealed class ExposureTests
{
    [Fact]
    public void APositionIsValuedAtTheBaseCurrencysRate()
    {
        var driver = new EngineDriver();
        driver.CreateAccount();
        driver.Quote(2650.00m, 2650.30m, "XAUUSD");
        driver.Buy(0.10m, symbol: "XAUUSD");
        driver.Quote(2660.00m, 2660.30m, "XAUUSD");

        var position = Assert.Single(driver.OpenPositions());
        Assert.Equal(EngineDriver.AccountId, position.AccountId);
        Assert.Equal("standard", position.GroupId);
        Assert.Equal(("XAUUSD", Side.Buy, 0.10m), (position.Symbol, position.Side, position.Volume));

        // 0.10 lot x 100 oz at the mid price 2660.15
        Assert.Equal(26_601.50m, position.Value);
        Assert.Equal(97.00m, position.Profit);
        Assert.Equal(886.72m, position.Margin);
    }

    [Fact]
    public void FiguresOfAnAccountInEurAreTurnedIntoUsd()
    {
        var usd = TestMarket.Configuration();
        var driver = new EngineDriver(usd with { Groups = [usd.Groups[0] with { Id = "eur", Currency = "EUR" }] });
        driver.CreateAccount(groupId: "eur");
        driver.Quote(1.08000m, 1.08010m, "EURUSD");
        driver.Quote(2650.00m, 2650.30m, "XAUUSD");
        driver.Buy(0.10m, symbol: "XAUUSD");
        driver.Quote(2660.00m, 2660.30m, "XAUUSD");

        var account = driver.Account();
        var position = Assert.Single(driver.OpenPositions());

        // 89.81 EUR and 821.00 EUR at the mid rate 1.08005
        Assert.Equal((89.81m, 821.00m), (account.Positions[0].Profit, account.Positions[0].Margin));
        Assert.Equal((97.00m, 886.72m), (position.Profit, position.Margin));
        Assert.Equal(26_601.50m, position.Value);

        // In the account's own currency nothing is converted.
        var inEur = Assert.Single(driver.OpenPositions("EUR"));
        Assert.Equal((89.81m, 821.00m), (inEur.Profit, inEur.Margin));
    }

    [Fact]
    public void PositionsComeInAccountOrderWithTheirSide()
    {
        var driver = new EngineDriver();
        driver.CreateAccount(accountId: "B2");
        driver.CreateAccount();
        driver.Quote(1.08000m, 1.08010m, "EURUSD");
        driver.Apply(t => new PlaceOrder(t, "B2", "O1", "EURUSD", Side.Sell, OrderType.Market, 2.00m, null, null, null));
        driver.Buy(1.00m);

        var positions = driver.OpenPositions();
        Assert.Equal(["A1", "B2"], positions.Select(p => p.AccountId));
        Assert.Equal([Side.Buy, Side.Sell], positions.Select(p => p.Side));

        // 1 and 2 lots of 100,000 EUR at the mid rate 1.08005
        Assert.Equal([108_005.00m, 216_010.00m], positions.Select(p => p.Value));
    }

    [Fact]
    public void FiguresAreMissingWithoutARateToTheCurrency()
    {
        // USDJPY has no price, so nothing can be turned into JPY yet.
        var driver = new EngineDriver();
        driver.CreateAccount();
        driver.Quote(1.08000m, 1.08010m, "EURUSD");
        driver.Buy(1.00m);

        var position = Assert.Single(driver.OpenPositions("JPY"));
        Assert.Null(position.Value);
        Assert.Null(position.Profit);
        Assert.Null(position.Margin);

        driver.Quote(150.000m, 150.020m, "USDJPY");
        Assert.NotNull(Assert.Single(driver.OpenPositions("JPY")).Value);
    }

    [Fact]
    public void AccountsAreCountedPerGroupAndStatus()
    {
        var driver = new EngineDriver();
        driver.CreateAccount();
        driver.CreateAccount(accountId: "B2");
        driver.CreateAccount(accountId: "C3");
        driver.Suspend();
        driver.Apply(t => new CloseAccount(t, "C3"));

        Assert.Equal(new AccountCounts(1, 1, 1), driver.AccountCounts()["standard"]);
    }

    [Fact]
    public void AccountsWithoutPositionsGiveNothing()
    {
        var driver = new EngineDriver();
        driver.CreateAccount();
        driver.Quote(1.08000m, 1.08010m, "EURUSD");

        Assert.Empty(driver.OpenPositions());
    }
}
