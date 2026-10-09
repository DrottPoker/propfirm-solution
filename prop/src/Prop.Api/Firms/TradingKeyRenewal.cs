using System.Collections.Concurrent;

using Prop.Api.Trading;

namespace Prop.Api.Firms;

/// <summary>
/// Gives a firm a new key to its server's admin API through the partner API when the trading platform refuses the one it
/// has, and saves it like <see cref="FirmProvisioner"/> does: encrypted in the database, then in the catalog. Only firms
/// that signed up have a server the prop platform created, so a configured firm's key is left as the configuration has it.
/// Requests for the same firm that are refused at the same time wait for one renewal and use its key. Other firms do not wait.
/// </summary>
internal sealed partial class TradingKeyRenewal(
    FirmCatalog firms,
    FirmStore store,
    ITradingPartner partner,
    TimeProvider time,
    ILogger<TradingKeyRenewal> logger) : ITradingKeyRenewal
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<string?> RenewAsync(FirmTrading refused, CancellationToken cancellationToken)
    {
        if (FirmOf(refused.Server) is not { } firm)
        {
            return null;
        }

        var gate = _locks.GetOrAdd(firm.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (FirmOf(refused.Server)?.Trading is not { } current)
            {
                return null;
            }

            // Another request renewed the refused key, for example while this one waited.
            if (current.ApiKey != refused.ApiKey)
            {
                return current.ApiKey;
            }

            string apiKey;
            try
            {
                apiKey = await partner.ReplaceAdminKeyAsync(current.Server, cancellationToken);
            }
            catch (TradingPlatformRejectedException exception)
            {
                // For example a server that is not the prop platform's. The refusal stands.
                LogNotRenewed(logger, firm.Id, exception);
                return null;
            }

            // The old key no longer works, so the new one is saved even if the caller has given up.
            await store.SetTradingApiKeyAsync(firm.Id, apiKey, time.GetUtcNow(), CancellationToken.None);
            firms.Put(await store.GetAsync(firm.Id, CancellationToken.None) ?? throw new InvalidOperationException($"Firm {firm.Id} disappeared."));
            LogRenewed(logger, firm.Id);
            return apiKey;
        }
        finally
        {
            gate.Release();
        }
    }

    // The firm that signed up with the server. Its server is its own, so no other firm has it.
    private Firm? FirmOf(string server) => firms.All.FirstOrDefault(f => !f.Configured && f.Trading?.Server == server);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The trading platform refused the admin key of firm {FirmId}, so the firm has a new one")]
    private static partial void LogRenewed(ILogger logger, string firmId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The trading platform refused the admin key of firm {FirmId}, and the partner API refused to make a new one")]
    private static partial void LogNotRenewed(ILogger logger, string firmId, Exception exception);
}
