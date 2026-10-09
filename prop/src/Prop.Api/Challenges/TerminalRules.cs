using Prop.Api.Trading;
using Prop.Rules;

namespace Prop.Api.Challenges;

/// <summary>The challenge's rules as they stand now, as the terminal shows them and warns about them (ADR 0052).</summary>
internal static class TerminalRules
{
    /// <summary>
    /// The rules of the current stage's trading account, or null when there is none or the challenge has ended.
    /// Deadlines are the starts of their trading days, and only while the stage is traded.
    /// </summary>
    public static TradingAccountRules? Of(ChallengeState state)
    {
        if (state.AccountId is null || state.HasEnded)
        {
            return null;
        }

        var rules = state.Rules;
        var tradingDay = state.Definition.TradingDay;
        var traded = state.Status == ChallengeStatus.Active;
        DateTimeOffset? StartOf(DateOnly? day) => traded && day is { } d ? TradingDays.StartOf(d, tradingDay).ToUniversalTime() : null;

        decimal? consistency = null;
        decimal? bestDay = null;
        bool? payoutAvailable = null;
        if (state.IsFunded)
        {
            var quote = ChallengeRules.QuotePayout(state);
            if (rules.ConsistencyPercent is { } percent)
            {
                consistency = percent;
                bestDay = quote.Profit > 0m && quote.BestDayProfit is { } best ? Math.Round(best / quote.Profit * 100m, 1) : null;
            }

            // Whether the trader can ask for a payout now, in the portal; why not is in the other rules (ADR 0058).
            payoutAvailable = rules.ProfitSplitPercent is null ? null : quote.Refusal is null;
        }

        return new TradingAccountRules(
            state.IsFunded,
            rules.MinTradingDays > 0 ? rules.MinTradingDays : null,
            state.TradingDays.Count,
            StartOf(state.StageDeadline),
            StartOf(state.InactivityDeadline),
            consistency,
            bestDay,
            state.IsFunded ? rules.ProfitSplitPercent : null,
            payoutAvailable);
    }
}
