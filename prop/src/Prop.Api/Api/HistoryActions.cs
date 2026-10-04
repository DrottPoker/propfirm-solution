using System.Globalization;
using System.Text;

using Microsoft.AspNetCore.Http.HttpResults;

using Prop.Api.Challenges;
using Prop.Api.Firms;
using Prop.Api.History;
using Prop.Rules;

namespace Prop.Api.Api;

/// <summary>
/// An account's trading history by stage: how the stage has gone, its closed positions and the same as a file. A
/// trader id limits it to that trader's own accounts. Without a stage, the latest stage that has started is shown.
/// </summary>
internal static class HistoryActions
{
    public const int MaxTradesPerRequest = 200;

    public static async Task<Results<Ok<PerformanceResponse>, ProblemHttpResult>> PerformanceAsync(
        Firm firm,
        Guid accountId,
        Guid? traderId,
        int? stage,
        ChallengeQueries queries,
        TradingHistoryQueries history,
        CancellationToken cancellationToken)
    {
        if (await AccountActions.FindAsync(firm, accountId, traderId, queries, cancellationToken) is not { } view)
        {
            return AccountActions.UnknownAccount();
        }

        var state = view.Account.State;
        if (await StageAsync(view, stage, history, cancellationToken) is not { } chosen)
        {
            return NoSuchStage();
        }

        var definition = state.Definition;
        var rules = definition.Stage(chosen.Stage);
        var target = rules.ProfitTargetPercent is { } percent ? definition.InitialBalance + definition.PercentOfInitialBalance(percent) : (decimal?)null;
        if (chosen.Record is not { } record)
        {
            return TypedResults.Ok(new PerformanceResponse(
                chosen.Stage, rules.Name, null, definition.InitialBalance, target, [], [], [], [], AccountPerformance.Statistics([])));
        }

        var stageHistory = await history.StageHistoryAsync(accountId, chosen.Stage, record.TradingAccountId, cancellationToken);
        return TypedResults.Ok(new PerformanceResponse(
            chosen.Stage,
            rules.Name,
            record.TradingAccountId,
            definition.InitialBalance,
            target,
            [.. stageHistory.Balance.Select(c => new BalancePoint(c.Time, c.Kind, c.Change, c.BalanceAfter))],
            [.. stageHistory.Floors.Where(f => f.FloorId == FloorIds.Daily).Select(f => new FloorPoint(f.Time, f.Level))],
            [.. stageHistory.Floors.Where(f => f.FloorId == FloorIds.MaxLoss).Select(f => new FloorPoint(f.Time, f.Level))],
            AccountPerformance.Days(stageHistory.Trades, stageHistory.CountedDays, definition.TradingDay),
            AccountPerformance.Statistics(stageHistory.Trades)));
    }

    /// <summary>A page of the stage's closed positions, newest first.</summary>
    public static async Task<Results<Ok<TradesResponse>, ProblemHttpResult>> TradesAsync(
        Firm firm,
        Guid accountId,
        Guid? traderId,
        int? stage,
        long? before,
        int limit,
        ChallengeQueries queries,
        TradingHistoryQueries history,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > MaxTradesPerRequest)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, $"limit must be 1 to {MaxTradesPerRequest}.");
        }

        if (await AccountActions.FindAsync(firm, accountId, traderId, queries, cancellationToken) is not { } view)
        {
            return AccountActions.UnknownAccount();
        }

        if (await StageAsync(view, stage, history, cancellationToken) is not { } chosen)
        {
            return NoSuchStage();
        }

        if (chosen.Record is not { } record)
        {
            return TypedResults.Ok(new TradesResponse([], null));
        }

        var trades = await history.ClosedTradesAsync(record.TradingAccountId, before, limit, cancellationToken);
        return TypedResults.Ok(new TradesResponse([.. trades.Select(TradeResponse.From)], trades.Count == limit ? trades[^1].CloseSequence : null));
    }

    /// <summary>Every closed position of the stage as a CSV file, oldest first, with times in UTC.</summary>
    public static async Task<Results<FileContentHttpResult, ProblemHttpResult>> TradesCsvAsync(
        Firm firm,
        Guid accountId,
        Guid? traderId,
        int? stage,
        ChallengeQueries queries,
        TradingHistoryQueries history,
        CancellationToken cancellationToken)
    {
        if (await AccountActions.FindAsync(firm, accountId, traderId, queries, cancellationToken) is not { } view)
        {
            return AccountActions.UnknownAccount();
        }

        if (await StageAsync(view, stage, history, cancellationToken) is not { } chosen)
        {
            return NoSuchStage();
        }

        var trades = chosen.Record is { } record ? await history.ClosedTradesAsync(record.TradingAccountId, null, null, cancellationToken) : [];
        var csv = new StringBuilder("Position,Symbol,Side,Volume,Opened (UTC),Open price,Closed (UTC),Close price,Profit,Commission,Result,Close reason\r\n");
        foreach (var trade in Enumerable.Reverse(trades))
        {
            csv.AppendJoin(
                ',',
                Field(trade.PositionId),
                Field(trade.Symbol),
                trade.Side.ToString(),
                Number(trade.Volume),
                trade.OpenedAt is { } opened ? Time(opened) : "",
                Number(trade.OpenPrice),
                Time(trade.ClosedAt),
                Number(trade.ClosePrice),
                Number(trade.Profit),
                Number(trade.Commission),
                Number(trade.Result),
                Field(trade.CloseReason));
            csv.Append("\r\n");
        }

        var stageName = view.Account.State.Definition.Stage(chosen.Stage).Name;
        var fileName = $"account-{view.Account.Number}-{Slug(stageName)}-trades.csv";
        return TypedResults.File(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(csv.ToString()), "text/csv", fileName);
    }

    /// <summary>
    /// The stage asked for, which must have started, or the latest one that has. A challenge whose first account is
    /// still opening has its current stage without a record. Null when the stage asked for has not started.
    /// </summary>
    private static async Task<ChosenStage?> StageAsync(AccountView view, int? stage, TradingHistoryQueries history, CancellationToken cancellationToken)
    {
        var accountId = view.Account.Id;
        var started = (await history.StagesAsync([accountId], cancellationToken))[accountId].Where(r => r.StartedAt is not null).ToList();
        if (stage is { } asked)
        {
            return started.FirstOrDefault(r => r.Stage == asked) is { } record ? new ChosenStage(asked, record) : null;
        }

        return started.Count > 0 ? new ChosenStage(started[^1].Stage, started[^1]) : new ChosenStage(view.Account.State.Stage, null);
    }

    private static ProblemHttpResult NoSuchStage() => AccountActions.Problem(StatusCodes.Status404NotFound, "The account has not started that stage.");

    private static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Time(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    // Text from the trading platform is quoted when it holds what would break the row.
    private static string Field(string value) =>
        value.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;

    // A stage name such as "Phase 1" in a file name: lower case letters and digits joined by dashes.
    private static string Slug(string name)
    {
        var slug = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                slug.Append(c);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        return slug.ToString().TrimEnd('-') is { Length: > 0 } result ? result : "stage";
    }

    private sealed record ChosenStage(int Stage, StageRecord? Record);
}
