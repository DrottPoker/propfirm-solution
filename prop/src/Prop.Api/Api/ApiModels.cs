using System.Text.Json;

using Prop.Api.Challenges;
using Prop.Api.Firms;
using Prop.Rules;

namespace Prop.Api.Api;

/// <summary>
/// Starts a challenge for the trader with the email. <paramref name="Reference"/> is the firm's own id, for
/// example its order number: repeating it returns the account already started instead of a second one.
/// </summary>
public sealed record StartAccountRequest(string? Email, string? ChallengeId, string? Reference = null);

public sealed record CancelAccountRequest(string? Reason);

/// <summary>
/// A trader's challenge account. <paramref name="TradingAccountId"/> is the account on the trading platform
/// for the current stage, which starts at <paramref name="InitialBalance"/>. The figures are what the trading
/// platform last reported. <paramref name="Paused"/> is set while the firm's month is unpaid: no new positions
/// can be opened and the days do not count. <paramref name="StageDeadline"/> is the trading day the stage fails on
/// unless it is passed before, and <paramref name="InactivityDeadline"/> the trading day the challenge ends on
/// unless a position is opened before.
/// </summary>
public sealed record AccountResponse(
    Guid Id,
    long Number,
    string Email,
    string ChallengeId,
    string? Reference,
    ChallengeStatus Status,
    int Stage,
    string StageName,
    bool Funded,
    string? TradingAccountId,
    int TradingDays,
    int MinTradingDays,
    decimal InitialBalance,
    string Currency,
    decimal? ProfitTarget,
    decimal? Balance,
    int OpenPositions,
    decimal? DailyFloor,
    decimal? MaxLossFloor,
    DateTimeOffset CreatedAt,
    PayoutQuoteResponse? NextPayout,
    bool Paused,
    DateOnly? StageDeadline,
    DateOnly? InactivityDeadline)
{
    internal static AccountResponse From(AccountView view)
    {
        var (account, trading) = (view.Account, view.Trading);
        var state = account.State;
        var definition = state.Definition;
        var rules = state.Rules;
        return new AccountResponse(
            account.Id,
            account.Number,
            account.Email,
            account.DefinitionId,
            account.Reference,
            state.Status,
            state.Stage,
            rules.Name,
            state.Stage == definition.FundedStage,
            state.AccountId,
            state.TradingDays.Count,
            rules.MinTradingDays,
            definition.InitialBalance,
            definition.Currency,
            rules.ProfitTargetPercent is { } target ? definition.InitialBalance + definition.PercentOfInitialBalance(target) : null,
            trading?.Balance,
            trading?.OpenPositions ?? 0,
            trading?.DailyFloor,
            trading?.MaxLossFloor,
            account.CreatedAt,
            state.IsFunded ? PayoutQuoteResponse.From(ChallengeRules.QuotePayout(state)) : null,
            state.IsPaused,
            state.Status == ChallengeStatus.Active ? state.StageDeadline : null,
            state.Status == ChallengeStatus.Active ? state.InactivityDeadline : null);
    }
}

/// <summary>
/// What a payout asked for now would pay the trader: <paramref name="ProfitSplitPercent"/> of the profit, which
/// is all withdrawn from the trading account. When <paramref name="CanRequest"/> is false,
/// <paramref name="Refusal"/> says why. Only for funded accounts.
/// </summary>
public sealed record PayoutQuoteResponse(
    bool CanRequest,
    string? Refusal,
    decimal Profit,
    decimal ProfitSplitPercent,
    decimal Amount,
    int TradingDays,
    int MinTradingDays)
{
    internal static PayoutQuoteResponse From(PayoutQuote quote) =>
        new(quote.CanRequest, quote.Refusal, quote.Profit, quote.ProfitSplitPercent, quote.Amount, quote.TradingDays, quote.MinTradingDays);
}

/// <summary>
/// A funded trader's payout. <paramref name="Profit"/> was withdrawn from <paramref name="TradingAccountId"/>,
/// and the trader gets <paramref name="Amount"/>. The firm approves it, sends the money itself and marks it as paid.
/// </summary>
public sealed record PayoutResponse(
    Guid Id,
    Guid AccountId,
    long AccountNumber,
    string Email,
    string TradingAccountId,
    PayoutStatus Status,
    decimal Profit,
    decimal ProfitSplitPercent,
    decimal Amount,
    string Currency,
    DateTimeOffset RequestedAt,
    DateTimeOffset? WithdrawnAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? RejectedAt,
    DateTimeOffset? FailedAt,
    string? Reason,
    string? Reference)
{
    internal static PayoutResponse From(PayoutView view) =>
        new(
            view.Id,
            view.ChallengeAccountId,
            view.AccountNumber,
            view.Email,
            view.TradingAccountId,
            view.Status,
            view.Profit,
            view.ProfitSplitPercent,
            view.Amount,
            view.Currency,
            view.RequestedAt,
            view.WithdrawnAt,
            view.ApprovedAt,
            view.PaidAt,
            view.RejectedAt,
            view.FailedAt,
            view.Reason,
            view.Reference);
}

public sealed record RejectPayoutRequest(string? Reason);

/// <summary><paramref name="Reference"/> is the firm's own, for example a bank transfer id.</summary>
public sealed record MarkPayoutPaidRequest(string? Reference);

/// <summary>One input to the account and what the rule engine decided, with the trading platform's event behind it.</summary>
public sealed record StepResponse(int Step, DateTimeOffset RecordedAt, JsonElement Input, JsonElement Outputs, JsonElement? SourceEvent);

/// <summary>Open <paramref name="Url"/> once before <paramref name="ExpiresAt"/> to be logged in to the trading terminal.</summary>
public sealed record LoginLinkResponse(Uri Url, DateTimeOffset ExpiresAt);

/// <summary>
/// An account as the portal shows it: the account, its trading account valued right now, the challenge it was bought
/// with, its stages, its results, the evidence if a floor was breached, why it expired if it ran out of time, when it
/// ended, and its payouts, newest first. <paramref name="HistoryVersion"/> changes whenever the account's trading
/// history or the rule engine's steps do, so the portal asks for the history again only then.
/// </summary>
public sealed record AccountDetailsResponse(
    AccountResponse Account,
    ChallengeDefinition Challenge,
    LiveFigures? Live,
    IReadOnlyList<StageResponse> Stages,
    ResultsResponse Results,
    BreachEvidence? Breach,
    ExpiryEvidence? Expiry,
    DateTimeOffset? EndedAt,
    IReadOnlyList<PayoutResponse> Payouts,
    string HistoryVersion);

/// <summary>The trading account valued at the latest prices. Missing when the trading platform cannot be reached.</summary>
public sealed record LiveFigures(decimal Balance, decimal Equity, IReadOnlyList<FloorFigure> Floors);

/// <summary>
/// A loss limit and how far equity can fall before it is breached. <paramref name="Distance"/> is the whole loss the
/// limit allows, from the challenge's rules, so the room left can be judged against it. Null for floors the rule
/// engine did not set.
/// </summary>
public sealed record FloorFigure(string FloorId, decimal Level, decimal Headroom, decimal? Distance);

/// <summary>Where a stage of the challenge is.</summary>
public enum StageProgress
{
    Passed,

    /// <summary>The stage the challenge is on, also while its account opens, while the firm reviews the funded account, and after the challenge ended.</summary>
    Current,

    Upcoming,
}

/// <summary>
/// One stage of the challenge. A passed stage has when it started and was passed, its <paramref name="Result"/> above
/// the initial balance and its trading days. The current stage has its trading days so far, on the funded stage
/// those since the last payout. <paramref name="ProfitTarget"/>, <paramref name="DailyLoss"/> and
/// <paramref name="MaxLoss"/> are the stage's rules as amounts of the initial balance, rounded as the rule engine does.
/// </summary>
public sealed record StageResponse(
    int Stage,
    string Name,
    StageProgress Progress,
    string? TradingAccountId,
    DateTimeOffset? StartedAt,
    DateTimeOffset? PassedAt,
    decimal? Result,
    int? TradingDays,
    decimal? ProfitTarget,
    decimal DailyLoss,
    decimal MaxLoss);

/// <summary>
/// The account's results, worked out by the service so that the portal only shows them. <paramref name="Balance"/>
/// and <paramref name="Equity"/> are the trading account's right now, or the balance the platform last reported.
/// <paramref name="Floating"/> is the open positions' result. <paramref name="StageResult"/> is how far the stage has
/// come from the initial balance, valued at equity when there is one. <paramref name="Today"/> is equity now against
/// the balance when the trading day started at <paramref name="DayStartedAt"/>, without deposits or withdrawals.
/// The profit target needs <paramref name="TargetRequired"/> above the initial balance, of which the balance has
/// <paramref name="TargetGained"/>, which is <paramref name="TargetPercent"/> of the way from 0 to 100.
/// <paramref name="PaidOut"/> is what the firm has paid the trader for the account.
/// </summary>
public sealed record ResultsResponse(
    decimal? Balance,
    decimal? Equity,
    decimal? Floating,
    decimal? StageResult,
    decimal? StageResultPercent,
    decimal? Today,
    decimal? DayStartBalance,
    DateTimeOffset? DayStartedAt,
    DateTimeOffset? NextDayStartsAt,
    decimal? TargetGained,
    decimal? TargetRequired,
    decimal? TargetPercent,
    decimal PaidOut);

/// <summary>What the trading platform recorded when a floor was breached.</summary>
public sealed record BreachEvidence(DateTimeOffset Time, string FloorId, decimal Level, decimal Equity, FailureReason Reason);

/// <summary>The challenge ran out of time when trading day <paramref name="Day"/> started, after its time limit or the days allowed without a new position.</summary>
public sealed record ExpiryEvidence(DateTimeOffset Time, ExpiryReason Reason, DateOnly Day);

/// <summary>Open <paramref name="Url"/> once before <paramref name="ExpiresAt"/> to choose a password for the portal.</summary>
public sealed record InviteResponse(Uri Url, DateTimeOffset ExpiresAt);

public sealed record PortalLoginRequest(string? Email, string? Password);

public sealed record AcceptInviteRequest(string? Token, string? Password);

/// <summary>Who is logged in to the portal. <paramref name="Role"/> is trader or admin.</summary>
public sealed record PortalMeResponse(Guid UserId, string Email, string Role, string FirmName);

/// <summary>What the portal needs to look like the firm's own, and whether the firm is still in the sandbox or being set up.</summary>
public sealed record BrandingResponse(string Name, string? LogoUrl, IReadOnlyDictionary<string, string> Colors, FirmStatus Status)
{
    internal static BrandingResponse From(Firm firm) =>
        new(firm.Portal.Branding.Name, firm.Portal.Branding.LogoUrl, firm.Portal.Branding.Colors, firm.Status);
}
