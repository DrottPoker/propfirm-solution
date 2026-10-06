using Microsoft.Extensions.Options;

using Trading.Service.Feeds;

namespace Trading.Service.Configuration;

/// <summary>
/// Stops the start when the trading hours cannot be read, or an instrument or a feed refers to hours, symbols or feeds
/// that do not exist.
/// </summary>
internal sealed class TradingHoursValidation : IValidateOptions<TradingOptions>
{
    public ValidateOptionsResult Validate(string? name, TradingOptions options) =>
        options.TradingHoursProblem(PriceFeedOptions.Providers) is { } problem ? ValidateOptionsResult.Fail(problem) : ValidateOptionsResult.Success;
}
