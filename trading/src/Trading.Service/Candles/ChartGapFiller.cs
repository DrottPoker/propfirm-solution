using Microsoft.AspNetCore.SignalR;

using Trading.Service.Realtime;

namespace Trading.Service.Candles;

/// <summary>
/// Fills the charts' gaps from the feed's history when prices come again after the service or the feed was down
/// (ADR 0056), and tells the terminals to load their charts again.
/// </summary>
internal sealed partial class ChartGapFiller(
    ChartHistory history,
    CandleStore candles,
    IHubContext<TradingHub, ITradingClient> hub,
    ILogger<ChartGapFiller> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Gaps found while the stored prices are replayed wait for the charts to be loaded.
            await candles.Ready.WaitAsync(stoppingToken);
            while (true)
            {
                await history.WaitForGapAsync(stoppingToken);
                if (await history.FillGapsAsync(stoppingToken) > 0)
                {
                    await TellTerminalsAsync();
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown. Prices that still waited are replayed from the journal at the next start.
        }
    }

    private async Task TellTerminalsAsync()
    {
        try
        {
            await hub.Clients.All.Charts();
        }
        catch (Exception exception)
        {
            // The terminals load the charts again on their next reconnect anyway.
            LogNotTold(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not tell the terminals that the charts were filled")]
    private static partial void LogNotTold(ILogger logger, Exception exception);
}
