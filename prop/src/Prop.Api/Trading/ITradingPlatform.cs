using Prop.Api.Firms;
using Prop.Rules;

namespace Prop.Api.Trading;

/// <summary>
/// The trading platform as the prop platform uses it, through its public admin API (ADR 0012). One
/// implementation per platform: ours now, others such as Match-Trader later. Every call acts for one firm.
/// </summary>
internal interface ITradingPlatform
{
    /// <summary>The trader's user on the firm's server. Created with the password if there is none yet.</summary>
    Task<Guid> EnsureUserAsync(FirmTrading firm, string email, string password, CancellationToken cancellationToken);

    /// <summary>Opens the account in the firm's group. Done if the firm already has it, so a retry is safe.</summary>
    Task OpenAccountAsync(FirmTrading firm, string accountId, decimal initialBalance, Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>Sets or replaces the floor. Done if the account is already disabled, since there is nothing left to protect.</summary>
    Task SetFloorAsync(FirmTrading firm, string accountId, string floorId, FloorSpec floor, CancellationToken cancellationToken);

    /// <summary>Closes the account. Done if it is already disabled.</summary>
    Task CloseAccountAsync(FirmTrading firm, string accountId, CancellationToken cancellationToken);

    /// <summary>The firm's events after the cursor, oldest first, waiting up to <paramref name="waitSeconds"/> for new ones.</summary>
    Task<TradingEventPage> ReadEventsAsync(FirmTrading firm, long after, int limit, int waitSeconds, CancellationToken cancellationToken);

    /// <summary>A one-time link that logs the user in to the terminal, optionally on one account.</summary>
    Task<TradingLoginLink> CreateLoginLinkAsync(FirmTrading firm, Guid userId, string? accountId, CancellationToken cancellationToken);
}

internal sealed record TradingEventPage(IReadOnlyList<TradingEvent> Events, long Cursor);

internal sealed record TradingLoginLink(Uri Url, DateTimeOffset ExpiresAt);

/// <summary>An event from the trading platform. <paramref name="Raw"/> is the event as the platform sent it, kept as evidence.</summary>
internal abstract record TradingEvent(long Sequence, DateTimeOffset Time, string AccountId, string Raw);

internal sealed record TradingAccountCreated(long Sequence, DateTimeOffset Time, string AccountId, string Raw, decimal Balance)
    : TradingEvent(Sequence, Time, AccountId, Raw);

internal sealed record TradingPositionOpened(long Sequence, DateTimeOffset Time, string AccountId, string Raw, decimal BalanceAfter)
    : TradingEvent(Sequence, Time, AccountId, Raw);

internal sealed record TradingPositionClosed(long Sequence, DateTimeOffset Time, string AccountId, string Raw, decimal BalanceAfter)
    : TradingEvent(Sequence, Time, AccountId, Raw);

internal sealed record TradingFloorSet(long Sequence, DateTimeOffset Time, string AccountId, string Raw, string FloorId, decimal Level)
    : TradingEvent(Sequence, Time, AccountId, Raw);

internal sealed record TradingFloorBreached(long Sequence, DateTimeOffset Time, string AccountId, string Raw, string FloorId, decimal Level, decimal Equity)
    : TradingEvent(Sequence, Time, AccountId, Raw);

internal sealed record TradingAccountDisabled(long Sequence, DateTimeOffset Time, string AccountId, string Raw)
    : TradingEvent(Sequence, Time, AccountId, Raw);

/// <summary>An event the prop platform does not act on. It only moves the cursor.</summary>
internal sealed record TradingOtherEvent(long Sequence, DateTimeOffset Time, string AccountId, string Raw)
    : TradingEvent(Sequence, Time, AccountId, Raw);

/// <summary>The trading platform could not be reached or failed. Worth trying again later.</summary>
internal sealed class TradingPlatformUnavailableException : Exception
{
    public TradingPlatformUnavailableException()
    {
    }

    public TradingPlatformUnavailableException(string message)
        : base(message)
    {
    }

    public TradingPlatformUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The trading platform refused the request. Trying again gives the same answer.</summary>
internal sealed class TradingPlatformRejectedException : Exception
{
    public TradingPlatformRejectedException()
    {
    }

    public TradingPlatformRejectedException(string message)
        : base(message)
    {
    }

    public TradingPlatformRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
