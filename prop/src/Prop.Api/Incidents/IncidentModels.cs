namespace Prop.Api.Incidents;

// What our admin view, a firm's admin panel and its status page show of incidents (ADR 0053).

/// <summary>Something said about an incident as it went on. <paramref name="By"/> is the staff member, shown in our admin view only.</summary>
public sealed record IncidentUpdateResponse(DateTimeOffset At, IncidentStatus Status, string Text, string? By);

/// <summary>An incident as our staff see it, with the note only they read.</summary>
public sealed record OpsIncidentResponse(
    Guid Id,
    IncidentKind Kind,
    string Title,
    string PublicText,
    string InternalNote,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    IncidentStatus Status,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<string>? Firms,
    bool Detected,
    string? CreatedBy,
    IReadOnlyList<IncidentUpdateResponse> Updates);

/// <summary>
/// Our incidents, and how the platform is doing now: the price feed, and the firms with traders in open positions,
/// which an outage would reach.
/// </summary>
public sealed record OpsIncidentsResponse(IReadOnlyList<OpsIncidentResponse> Incidents, PriceFeedNow? Feed, IReadOnlyList<FirmExposure> Firms);

/// <summary>
/// The price feed now: when the last price came, how many symbols have an open market and how many of them got no price
/// for a minute. Null in the response when the trading platform could not be asked.
/// </summary>
public sealed record PriceFeedNow(string Feed, DateTimeOffset? LastPriceAt, int OpenMarkets, int WithoutPrices);

/// <summary>A firm and how many of its accounts have open positions.</summary>
public sealed record FirmExposure(string FirmId, string Name, int AccountsWithPositions);

/// <summary>
/// An incident as our staff write it. <paramref name="Firms"/> are the firms it concerns, or null for every firm.
/// <paramref name="EndedAt"/> is empty while it goes on.
/// </summary>
public sealed record IncidentRequest(
    IncidentKind Kind,
    string? Title,
    string? PublicText,
    string? InternalNote,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    IReadOnlyList<string>? Firms);

/// <summary>What our staff say as an incident goes on. Resolved ends it.</summary>
public sealed record IncidentUpdateRequest(IncidentStatus Status, string? Text);

/// <summary>A published incident in a firm's list, with how many accounts the firm has decided on.</summary>
public sealed record FirmIncidentSummary(
    Guid Id,
    IncidentKind Kind,
    string Title,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    IncidentStatus Status,
    int Decisions);

/// <summary>The published incidents of the last 90 days that concern the firm, newest first, and how many still go on.</summary>
public sealed record FirmIncidentsResponse(IReadOnlyList<FirmIncidentSummary> Incidents, int Open);

/// <summary>
/// An incident as a firm sees it: what we said, the firm's own note, its accounts that the incident reached and what the
/// firm did for them. <paramref name="ImpactKnown"/> is false when the trading platform could not tell, or the incident is
/// too old or too long to look back on.
/// </summary>
public sealed record FirmIncidentResponse(
    Guid Id,
    IncidentKind Kind,
    string Title,
    string PublicText,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    IncidentStatus Status,
    IReadOnlyList<IncidentUpdateResponse> Updates,
    string? Note,
    bool ImpactKnown,
    IReadOnlyList<IncidentAccountResponse> Accounts,
    IReadOnlyList<IncidentDecisionResponse> Decisions);

/// <summary>
/// One of the firm's accounts that the incident reached: open positions when it began, requests refused during it for lack
/// of a fresh price, or a loss limit broken during it or in the half hour after. <paramref name="EquityAtStart"/> is the
/// equity when it began, when known. <paramref name="TradingAccountId"/> is the stage's account on the trading platform,
/// so an account that passed a stage during the incident shows once for each. <paramref name="CanReinstate"/> when a
/// broken limit ended the stage, and <paramref name="CanCredit"/> when the account still trades on it.
/// </summary>
public sealed record IncidentAccountResponse(
    Guid AccountId,
    long Number,
    string TradingAccountId,
    string ChallengeName,
    string StageName,
    string TraderEmail,
    string Currency,
    string Status,
    int OpenPositions,
    decimal BalanceAtStart,
    decimal? EquityAtStart,
    int OrdersRefused,
    int ClosesRefused,
    int ChangesRefused,
    IncidentBreachResponse? Breach,
    decimal Balance,
    decimal Equity,
    int TradingDays,
    bool CanReinstate,
    bool CanCredit);

/// <summary>A loss limit broken during the incident or soon after it.</summary>
public sealed record IncidentBreachResponse(DateTimeOffset At, string FloorId, decimal Level, decimal Equity);

/// <summary>What the firm did for an account, by whom and why.</summary>
public sealed record IncidentDecisionResponse(Guid Id, Guid AccountId, long Number, IncidentDecisionKind Kind, decimal Amount, string Reason, string DecidedBy, DateTimeOffset DecidedAt);

/// <summary>The firm's own words to its traders about an incident. Empty removes them.</summary>
public sealed record IncidentNoteRequest(string? Text);

/// <summary>Reinstates the account's ended stage with the balance, keeping the trading days counted so far or not. The reason is kept with the decision.</summary>
public sealed record ReinstateRequest(Guid AccountId, decimal Balance, bool KeepTradingDays, string? Reason);

/// <summary>Puts an amount on the account, for example for a close the outage refused.</summary>
public sealed record CreditRequest(Guid AccountId, decimal Amount, string? Reason);

/// <summary>A firm's status page: whether everything runs now, each part of the service, and the incidents of the last 90 days.</summary>
public sealed record StatusPageResponse(string FirmName, DateTimeOffset Now, bool AllRunning, IReadOnlyList<StatusPartResponse> Parts, IReadOnlyList<StatusIncidentResponse> Incidents);

/// <summary>A part of the service and whether it runs now.</summary>
public sealed record StatusPartResponse(ServicePart Part, bool Running);

/// <summary>An incident on the status page, with what we said and the firm's own note.</summary>
public sealed record StatusIncidentResponse(
    Guid Id,
    string Title,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    IncidentStatus Status,
    IReadOnlyList<ServicePart> Parts,
    IReadOnlyList<IncidentUpdateResponse> Updates,
    string? FirmNote);
