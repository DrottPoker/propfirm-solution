using Trading.Engine.Tests.Support;

namespace Trading.Engine.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void ValidConfigurationIsAccepted()
    {
        _ = new TradingEngine(TestMarket.Configuration());
    }

    [Theory]
    [MemberData(nameof(InvalidConfigurations))]
    public void InvalidConfigurationIsRejected(EngineConfiguration configuration, string expectedMessage)
    {
        var exception = Assert.Throws<ArgumentException>(() => new TradingEngine(configuration));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    public static TheoryData<EngineConfiguration, string> InvalidConfigurations()
    {
        var valid = TestMarket.Configuration();
        var eurUsd = TestMarket.EurUsd;
        return new()
        {
            { TestMarket.Configuration(instruments: []), "At least one instrument" },
            { TestMarket.Configuration(instruments: [eurUsd, eurUsd]), "empty or duplicated" },
            { TestMarket.Configuration(instruments: [eurUsd with { Digits = 11 }]), "digits" },
            { TestMarket.Configuration(instruments: [eurUsd with { ContractSize = 0m }]), "contract size" },
            { TestMarket.Configuration(instruments: [eurUsd with { VolumeStep = 0m }]), "volume limits" },
            { TestMarket.Configuration(instruments: [eurUsd with { TradingHours = TradingHoursTests.Forex with { TimeZone = "Mars/Olympus" } }]), "EURUSD: trading hours: time zone" },
            { valid with { Groups = [Group(new SymbolConditions("BTCUSD", 100, 0, 0m))] }, "unknown symbol" },
            { valid with { Groups = [Group(new SymbolConditions("EURUSD", 0, 0, 0m))] }, "leverage" },
            { valid with { Groups = [Group(new SymbolConditions("EURUSD", 100, -1, 0m))] }, "spread markup" },
            { valid with { Groups = [Group(new SymbolConditions("EURUSD", 100, 0, -1m))] }, "commission" },
            { valid with { MaxQuoteAge = TimeSpan.Zero }, "MaxQuoteAge" },
            { valid with { CurrencyDecimals = new Dictionary<string, int> { ["USD"] = 9 } }, "decimals" },
        };
    }

    private static TradingGroup Group(SymbolConditions conditions) => new("g", "USD", 50m, [conditions]);
}
