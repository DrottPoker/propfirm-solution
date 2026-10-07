using Trading.Engine.Inputs;
using Trading.Engine.Tests.Support;

namespace Trading.Engine.Tests;

/// <summary>Valuing positions after the fact from recorded prices, as the engine did (ADR 0053).</summary>
public sealed class RevaluationTests
{
    // A JPY position converted at the USDJPY mid and a EURUSD position, both with markup.
    [Fact]
    public void EquityIsTheEnginesForTheSamePricesAndConditions()
    {
        var configuration = TestMarket.Configuration(commission: 3.50m, markupPoints: 3);
        var driver = new EngineDriver(configuration);
        driver.CreateAccount();
        driver.Quote(150.000m, 150.020m, "USDJPY");
        driver.Quote(1.08000m, 1.08010m);
        driver.Buy(1.00m, "P1", symbol: "USDJPY");
        driver.Sell(0.50m, "P2");
        driver.Quote(150.520m, 150.540m, "USDJPY");
        driver.Quote(1.07900m, 1.07910m);
        var account = driver.Account();
        var conditions = configuration.Groups[0].Symbols.ToDictionary(s => s.Symbol);
        var instruments = configuration.Instruments.ToDictionary(i => i.Symbol);

        var revaluation = new Revaluation(configuration);
        revaluation.Update(new Quote(driver.Now, "USDJPY", 150.520m, 150.540m));
        revaluation.Update(new Quote(driver.Now, "EURUSD", 1.07900m, 1.07910m));
        var equity = revaluation.Equity(
            account.Balance,
            account.Positions.Select(p => new RevaluedPosition(instruments[p.Symbol], conditions[p.Symbol], p.Side, p.Volume, p.OpenPrice)),
            account.Currency);

        Assert.Equal(account.Equity, equity);
    }

    [Fact]
    public void WithoutAPriceForAPositionThereIsNoEquity()
    {
        var configuration = TestMarket.Configuration();
        var revaluation = new Revaluation(configuration);
        revaluation.Update(new Quote(TestMarket.Start, "EURUSD", 1.08000m, 1.08010m));
        var gold = new RevaluedPosition(TestMarket.XauUsd, configuration.Groups[0].Symbols.Single(s => s.Symbol == "XAUUSD"), Side.Buy, 1.00m, 2650.00m);

        Assert.Null(revaluation.Equity(100_000m, [gold], "USD"));
        Assert.Equal(100_000m, revaluation.Equity(100_000m, [], "USD"));
    }
}
