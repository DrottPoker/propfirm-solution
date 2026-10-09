namespace Trading.Service.Candles;

/// <summary>
/// The charts' bars per feed (ADR 0048): minute bars made from the feed's live prices, and the feed's history. Derived
/// data, since the journal keeps every price, but it lets a start read days of charts without replaying the prices.
/// </summary>
public interface IChartStore
{
    /// <summary>
    /// Where the feed's history starts, when it is loaded since the service last switched to the feed. Null when it is
    /// not, or when it was loaded before its start was stored.
    /// </summary>
    Task<DateTimeOffset?> GetHistoryReachAsync(string feed, CancellationToken cancellationToken);

    /// <summary>Marks the feed's history as out of date, so it is loaded again.</summary>
    Task ForgetHistoryAsync(string feed, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the feed's bars before <paramref name="until"/> with its history, which starts at <paramref name="reach"/>,
    /// and marks it as loaded, all or nothing.
    /// </summary>
    Task ReplaceHistoryAsync(string feed, DateTimeOffset reach, DateTimeOffset until, IReadOnlyList<ChartBar> bars, CancellationToken cancellationToken);

    /// <summary>Stores bars made from the feed's live prices. A bar stored before is replaced.</summary>
    Task SaveAsync(string feed, IReadOnlyList<ChartBar> bars, CancellationToken cancellationToken);

    /// <summary>The feed's bars from the given time, oldest first.</summary>
    IAsyncEnumerable<ChartBar> ReadAsync(string feed, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Deletes every feed's bars that start before the given time.</summary>
    Task PruneAsync(DateTimeOffset before, CancellationToken cancellationToken);

    /// <summary>When the feed's history was loaded and how far back it reaches, or null when it is not loaded (ADR 0057).</summary>
    Task<ChartHistoryInfo?> GetHistoryInfoAsync(string feed, CancellationToken cancellationToken);

    /// <summary>Saves a gap in the charts and what came of it, replacing an earlier save of the same gap (ADR 0057).</summary>
    Task SaveGapAsync(ChartGap gap, CancellationToken cancellationToken);

    /// <summary>The feed's latest gaps, the latest found first.</summary>
    Task<IReadOnlyList<ChartGap>> ListGapsAsync(string feed, int limit, CancellationToken cancellationToken);
}

/// <summary>When a feed's history was loaded, and where it starts, null for a history loaded before that was kept.</summary>
public sealed record ChartHistoryInfo(DateTimeOffset LoadedAt, DateTimeOffset? Reach);

/// <summary>What came of a gap in the charts.</summary>
public enum ChartGapState
{
    /// <summary>The feed's history is being asked for it.</summary>
    Filling,

    /// <summary>Filled from the feed's history.</summary>
    Filled,

    /// <summary>The feed had no prices for it, for example since every market was closed.</summary>
    Empty,

    /// <summary>The feed could not be asked, after a few tries. The gap stays in the charts.</summary>
    NotFilled,
}

/// <summary>
/// A whole minute or more without any price in the charts (ADR 0056), from <paramref name="From"/> until
/// <paramref name="Until"/>, and what came of filling it: how often the feed was asked, when it finished, how many bars
/// it gave and what went wrong.
/// </summary>
public sealed record ChartGap(
    Guid Id,
    string Feed,
    DateTimeOffset From,
    DateTimeOffset Until,
    DateTimeOffset FoundAt,
    ChartGapState State,
    int Tries,
    DateTimeOffset? FinishedAt,
    int Bars,
    string? Problem);

/// <summary>A period of history and the length of its bars.</summary>
public sealed record HistorySpan(Timeframe Resolution, DateTimeOffset From, DateTimeOffset Until);

public sealed class ChartOptions
{
    public const string SectionName = "Charts";

    /// <summary>
    /// How far back the charts reach, in whole days before today in UTC. The part older than <see cref="History"/> is
    /// loaded in day bars, so it shows on D1, W1 and MN (ADR 0058).
    /// </summary>
    public TimeSpan DayHistory { get; init; } = TimeSpan.FromDays(1_095);

    /// <summary>
    /// The latest part of the history, in whole days before today in UTC, that is loaded in hour bars, so it shows on H1
    /// and longer timeframes. The part older than <see cref="QuarterHourHistory"/>.
    /// </summary>
    public TimeSpan History { get; init; } = TimeSpan.FromDays(180);

    /// <summary>The latest part of the history, in whole days before today, that is loaded in 15 minute bars.</summary>
    public TimeSpan QuarterHourHistory { get; init; } = TimeSpan.FromDays(30);

    /// <summary>
    /// The latest part of the history, in whole days before today, that is loaded in minute bars. Longer bars further back
    /// are enough for the longer timeframes and keep the calls to a data vendor few.
    /// </summary>
    public TimeSpan MinuteHistory { get; init; } = TimeSpan.FromDays(2);

    public bool IsValid() =>
        DayHistory.Ticks % TimeSpan.TicksPerDay == 0
        && History.Ticks % TimeSpan.TicksPerDay == 0
        && QuarterHourHistory.Ticks % TimeSpan.TicksPerDay == 0
        && MinuteHistory.Ticks % TimeSpan.TicksPerDay == 0
        && MinuteHistory >= TimeSpan.Zero
        && QuarterHourHistory >= MinuteHistory
        && History >= QuarterHourHistory
        && DayHistory >= History;
}
