using System.Diagnostics.CodeAnalysis;

namespace Trading.Engine.Internal;

internal static class ConfigurationValidator
{
    private const int MaxDigits = 10;
    private const int MaxCurrencyDecimals = 8;

    public static void Validate(EngineConfiguration configuration)
    {
        Require(configuration.Instruments is { Count: > 0 }, "At least one instrument is required.");
        Require(configuration.Groups is not null, "Groups are required.");
        Require(configuration.CurrencyDecimals is not null, "Currency decimals are required.");
        Require(configuration.MaxQuoteAge > TimeSpan.Zero, "MaxQuoteAge must be positive.");

        var symbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var instrument in configuration.Instruments)
        {
            Require(instrument is not null, "Instruments must not contain null.");
            Require(!string.IsNullOrEmpty(instrument.Symbol) && symbols.Add(instrument.Symbol), $"Instrument symbol '{instrument.Symbol}' is empty or duplicated.");
            Require(!string.IsNullOrEmpty(instrument.BaseCurrency) && !string.IsNullOrEmpty(instrument.QuoteCurrency), $"{instrument.Symbol}: currencies are required.");
            Require(instrument.ContractSize > 0m, $"{instrument.Symbol}: contract size must be positive.");
            Require(instrument.Digits is >= 0 and <= MaxDigits, $"{instrument.Symbol}: digits must be between 0 and {MaxDigits}.");
            Require(instrument.VolumeMin > 0m && instrument.VolumeStep > 0m && instrument.VolumeMax >= instrument.VolumeMin, $"{instrument.Symbol}: invalid volume limits.");
        }

        var groupIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in configuration.Groups)
        {
            Require(group is not null, "Groups must not contain null.");
            Require(!string.IsNullOrEmpty(group.Id) && groupIds.Add(group.Id), $"Group id '{group.Id}' is empty or duplicated.");
            Require(!string.IsNullOrEmpty(group.Currency), $"Group {group.Id}: currency is required.");
            Require(group.StopOutLevelPercent >= 0m, $"Group {group.Id}: stop out level must not be negative.");
            Require(group.Symbols is not null, $"Group {group.Id}: symbols are required.");

            var groupSymbols = new HashSet<string>(StringComparer.Ordinal);
            foreach (var conditions in group.Symbols)
            {
                Require(conditions is not null, $"Group {group.Id}: symbols must not contain null.");
                Require(conditions.Symbol is not null && symbols.Contains(conditions.Symbol), $"Group {group.Id}: unknown symbol '{conditions.Symbol}'.");
                Require(groupSymbols.Add(conditions.Symbol), $"Group {group.Id}: symbol {conditions.Symbol} is listed twice.");
                Require(conditions.Leverage > 0, $"Group {group.Id}, {conditions.Symbol}: leverage must be positive.");
                Require(conditions.SpreadMarkupPoints >= 0, $"Group {group.Id}, {conditions.Symbol}: spread markup must not be negative.");
                Require(conditions.CommissionPerLotPerSide >= 0m, $"Group {group.Id}, {conditions.Symbol}: commission must not be negative.");
            }
        }

        foreach (var (currency, decimals) in configuration.CurrencyDecimals)
        {
            Require(decimals is >= 0 and <= MaxCurrencyDecimals, $"Currency {currency}: decimals must be between 0 and {MaxCurrencyDecimals}.");
        }
    }

    private static void Require([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
        {
            throw new ArgumentException($"Invalid engine configuration: {message}");
        }
    }
}
