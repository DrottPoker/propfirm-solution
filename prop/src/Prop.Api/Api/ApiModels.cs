using System.Text.Json;

using Prop.Api.Challenges;
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
/// for the current stage. The figures are what the trading platform last reported.
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
    decimal? ProfitTarget,
    decimal? Balance,
    int OpenPositions,
    decimal? DailyFloor,
    decimal? MaxLossFloor,
    DateTimeOffset CreatedAt)
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
            rules.ProfitTargetPercent is { } target ? definition.InitialBalance + definition.PercentOfInitialBalance(target) : null,
            trading?.Balance,
            trading?.OpenPositions ?? 0,
            trading?.DailyFloor,
            trading?.MaxLossFloor,
            account.CreatedAt);
    }
}

/// <summary>One input to the account and what the rule engine decided, with the trading platform's event behind it.</summary>
public sealed record StepResponse(int Step, DateTimeOffset RecordedAt, JsonElement Input, JsonElement Outputs, JsonElement? SourceEvent);

/// <summary>Open <paramref name="Url"/> once before <paramref name="ExpiresAt"/> to be logged in to the trading terminal.</summary>
public sealed record LoginLinkResponse(Uri Url, DateTimeOffset ExpiresAt);
