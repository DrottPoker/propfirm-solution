using Prop.Api.Firms;

namespace Prop.Api.Trading;

/// <summary>
/// What <see cref="TradingPlatformClient"/> does when the trading platform refuses a firm's admin key, for example after
/// our staff stopped it in the trading platform's staff panel.
/// </summary>
internal interface ITradingKeyRenewal
{
    /// <summary>
    /// The key to try the refused request again with: a new one, or the firm's current one when another request already
    /// renewed the refused key. Null when the firm's key cannot be renewed, so the refusal stands.
    /// </summary>
    Task<string?> RenewAsync(FirmTrading refused, CancellationToken cancellationToken);
}
