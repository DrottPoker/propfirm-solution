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
            var hoursProblem = instrument.TradingHours?.FindProblem();
            Require(hoursProblem is null, $"{instrument.Symbol}: trading hours: {hoursProblem}");
        }

        var groupIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in configuration.Groups)
        {
            Require(group is not null, "Groups must not contain null.");
            Require(!string.IsNullOrEmpty(group.Id) && groupIds.Add(group.Id), $"Group id '{group.Id}' is empty or duplicated.");
            var problem = GroupProblem(group, symbols);
            Require(problem is null, problem!);
        }

        foreach (var (currency, decimals) in configuration.CurrencyDecimals)
        {
            Require(decimals is >= 0 and <= MaxCurrencyDecimals, $"Currency {currency}: decimals must be between 0 and {MaxCurrencyDecimals}.");
        }
    }

    /// <summary>What is wrong with the group's conditions, or null. Shared by configured groups and groups created by input.</summary>
    public static string? GroupProblem(TradingGroup group, IReadOnlySet<string> symbols)
    {
        if (string.IsNullOrEmpty(group.Currency))
        {
            return $"Group {group.Id}: currency is required.";
        }

        if (group.StopOutLevelPercent < 0m)
        {
            return $"Group {group.Id}: stop out level must not be negative.";
        }

        if (group.Symbols is null)
        {
            return $"Group {group.Id}: symbols are required.";
        }

        var groupSymbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var conditions in group.Symbols)
        {
            if (conditions is null)
            {
                return $"Group {group.Id}: symbols must not contain null.";
            }

            if (conditions.Symbol is null || !symbols.Contains(conditions.Symbol))
            {
                return $"Group {group.Id}: unknown symbol '{conditions.Symbol}'.";
            }

            if (!groupSymbols.Add(conditions.Symbol))
            {
                return $"Group {group.Id}: symbol {conditions.Symbol} is listed twice.";
            }

            if (conditions.Leverage <= 0)
            {
                return $"Group {group.Id}, {conditions.Symbol}: leverage must be positive.";
            }

            if (conditions.SpreadMarkupPoints < 0)
            {
                return $"Group {group.Id}, {conditions.Symbol}: spread markup must not be negative.";
            }

            if (conditions.CommissionPerLotPerSide < 0m)
            {
                return $"Group {group.Id}, {conditions.Symbol}: commission must not be negative.";
            }
        }

        return null;
    }

    private static void Require([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
        {
            throw new ArgumentException($"Invalid engine configuration: {message}");
        }
    }
}
