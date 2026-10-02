using Trading.Engine.Tests.Support;

namespace Trading.Engine.Tests;

public sealed class PriceQueryTests
{
    [Fact]
    public void PricesIncludeTheGroupMarkupAndOnlySymbolsWithAPrice()
    {
        var driver = new EngineDriver(TestMarket.Configuration(markupPoints: 3));
        driver.Quote(1.08000m, 1.08010m);

        var price = Assert.Single(driver.Prices()!);

        Assert.Equal("EURUSD", price.Symbol);
        Assert.Equal(1.07999m, price.Bid);
        Assert.Equal(1.08012m, price.Ask);
        Assert.Equal(driver.Now, price.Timestamp);
    }

    [Fact]
    public void UnknownGroupHasNoPrices()
    {
        var driver = new EngineDriver();

        Assert.Null(driver.Prices("missing"));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(2, 1, 1)]
    [InlineData(3, 1, 2)]
    public void MarkupIsSplitWithTheLargerPartOnTheAsk(int markup, int bid, int ask)
    {
        var conditions = new SymbolConditions("EURUSD", 100, markup, 0m);

        Assert.Equal((bid, ask), (conditions.BidMarkupPoints, conditions.AskMarkupPoints));
    }
}
