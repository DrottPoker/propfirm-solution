using Microsoft.Extensions.Options;

using Prop.Api.Configuration;

namespace Prop.Api.Incidents;

/// <summary>Asks the trading platform how its price feed is doing, again and again (ADR 0053).</summary>
internal sealed partial class PriceFeedWatcher(IncidentService incidents, IOptions<IncidentOptions> options, TimeProvider time, ILogger<PriceFeedWatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await incidents.CheckPriceFeedAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception);
            }

            await Task.Delay(options.Value.CheckEvery, time, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The price feed could not be checked")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
