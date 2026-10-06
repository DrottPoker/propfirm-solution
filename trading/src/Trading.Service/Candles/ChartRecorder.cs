namespace Trading.Service.Candles;

/// <summary>Stores the charts' finished minute bars every few seconds, and the last ones when the service stops.</summary>
internal sealed partial class ChartRecorder(ChartHistory history, CandleStore candles, TimeProvider time, ILogger<ChartRecorder> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Started with the service, so the clock is the same for every start.
        using var timer = new PeriodicTimer(Interval, time);
        try
        {
            await candles.Ready.WaitAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RecordAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }

        // So a normal restart only replays the minute that had not finished.
        if (candles.Ready.IsCompletedSuccessfully)
        {
            await RecordAsync(CancellationToken.None);
        }
    }

    private async Task RecordAsync(CancellationToken cancellationToken)
    {
        try
        {
            await history.RecordAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The bars are tried again next time, and a restart replays their prices from the journal.
            LogRecordFailed(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not store the charts' finished bars")]
    private static partial void LogRecordFailed(ILogger logger, Exception exception);
}
