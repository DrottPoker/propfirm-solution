namespace Trading.Service.Candles;

/// <summary>
/// The charts' bars per feed (ADR 0048): minute bars made from the feed's live prices, and the feed's history. Derived
/// data, since the journal keeps every price, but it lets a start read days of charts without replaying the prices.
/// </summary>
public interface IChartStore
{
    /// <summary>True when the feed's history is loaded since the service last switched to the feed.</summary>
    Task<bool> HasHistoryAsync(string feed, CancellationToken cancellationToken);

    /// <summary>Marks the feed's history as out of date, so it is loaded again.</summary>
    Task ForgetHistoryAsync(string feed, CancellationToken cancellationToken);

    /// <summary>Replaces the feed's bars before <paramref name="until"/> with its history and marks it as loaded, all or nothing.</summary>
    Task ReplaceHistoryAsync(string feed, DateTimeOffset until, IReadOnlyList<ChartBar> bars, CancellationToken cancellationToken);

    /// <summary>Stores bars made from the feed's live prices. A bar stored before is replaced.</summary>
    Task SaveAsync(string feed, IReadOnlyList<ChartBar> bars, CancellationToken cancellationToken);

    /// <summary>The feed's bars from the given time, oldest first.</summary>
    IAsyncEnumerable<ChartBar> ReadAsync(string feed, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Deletes every feed's bars that start before the given time.</summary>
    Task PruneAsync(DateTimeOffset before, CancellationToken cancellationToken);
}

/// <summary>A period of history and the length of its bars.</summary>
public sealed record HistorySpan(Timeframe Resolution, DateTimeOffset From, DateTimeOffset Until);

public sealed class ChartOptions
{
    public const string SectionName = "Charts";

    /// <summary>How far back the charts reach, in whole days before today in UTC.</summary>
    public TimeSpan History { get; init; } = TimeSpan.FromDays(30);

    /// <summary>
    /// The latest part of the history, in whole days before today, that is loaded in minute bars. Older history comes
    /// in 15 minute bars, which is enough for the longer timeframes and keeps the calls to a data vendor few.
    /// </summary>
    public TimeSpan MinuteHistory { get; init; } = TimeSpan.FromDays(2);

    public bool IsValid() =>
        History.Ticks % TimeSpan.TicksPerDay == 0
        && MinuteHistory.Ticks % TimeSpan.TicksPerDay == 0
        && MinuteHistory >= TimeSpan.Zero
        && History >= MinuteHistory;
}
