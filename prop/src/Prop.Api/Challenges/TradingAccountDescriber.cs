using Common.Postgres;

using Prop.Api.Firms;

namespace Prop.Api.Challenges;

/// <summary>
/// Once at startup, tells the trading platform how to show every open trading account it was not told about, so the
/// terminal names accounts opened before it could as the portal does (ADR 0035).
/// </summary>
internal sealed partial class TradingAccountDescriber(
    ChallengeService accounts,
    FirmCatalog firms,
    DatabaseSchema schema,
    ILogger<TradingAccountDescriber> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await schema.EnsureAsync(stoppingToken);
        await firms.Ready.WaitAsync(stoppingToken);
        foreach (var firm in firms.All.Where(f => f.Trading is not null))
        {
            try
            {
                if (await accounts.DescribeOpenAccountsAsync(firm, stoppingToken) is > 0 and var count)
                {
                    LogDescribed(logger, count, firm.Id);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception, firm.Id);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Described {Count} open trading accounts of firm {FirmId} to the trading platform")]
    private static partial void LogDescribed(ILogger logger, int count, string firmId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Describing the open trading accounts of firm {FirmId} failed")]
    private static partial void LogFailed(ILogger logger, Exception exception, string firmId);
}
