namespace Trading.Engine;

/// <summary>
/// When an account's trading day starts: at <paramref name="Start"/> on the clock of <paramref name="TimeZone"/>, an IANA
/// id such as Europe/Stockholm. The firm's day, which its daily loss limit follows too, and which the trader's own
/// limits count from (ADR 0054). A day is named by the local date it starts on, as in the firm's system.
/// </summary>
public sealed record TradingDay(string TimeZone, TimeOnly Start)
{
    /// <summary>Midnight UTC, for an account the firm has not told its day.</summary>
    public static readonly TradingDay Utc = new("UTC", TimeOnly.MinValue);

    /// <summary>When the trading day after the one <paramref name="at"/> is in starts, always after <paramref name="at"/>.</summary>
    public DateTimeOffset NextStart(DateTimeOffset at)
    {
        var zone = Zone();
        var local = TimeZoneInfo.ConvertTime(at, zone).DateTime;
        var day = DateOnly.FromDateTime((local - Start.ToTimeSpan()).Date);
        return StartOf(day.AddDays(1), zone);
    }

    /// <summary>What is wrong with the day, or null. An unknown time zone is refused.</summary>
    public string? FindProblem() =>
        string.IsNullOrEmpty(TimeZone) || !TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone, out _) ? $"time zone '{TimeZone}' is unknown." : null;

    private TimeZoneInfo Zone() => TimeZone == "UTC" ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(TimeZone);

    private DateTimeOffset StartOf(DateOnly day, TimeZoneInfo zone)
    {
        var start = day.ToDateTime(Start);

        // A start in the hour the clocks skip moves to the first time that exists.
        while (zone.IsInvalidTime(start))
        {
            start = start.AddMinutes(1);
        }

        // A start in the hour the clocks repeat is the first of the two.
        var offset = zone.IsAmbiguousTime(start) ? zone.GetAmbiguousTimeOffsets(start).Max() : zone.GetUtcOffset(start);
        return new DateTimeOffset(start, offset);
    }
}

/// <summary>
/// Limits the trader sets for themselves, stricter than the firm's (ADR 0054). <paramref name="DailyLoss"/> and
/// <paramref name="DailyTarget"/> are amounts below and above the balance the trading day started with: reaching either
/// closes every position and locks new orders until the next trading day. <paramref name="MaxTrades"/> is how many
/// positions may open in a trading day. An empty limit is off.
/// </summary>
public sealed record OwnLimits(decimal? DailyLoss, decimal? DailyTarget, int? MaxTrades)
{
    public const int MostTrades = 1_000;

    public static readonly OwnLimits None = new(null, null, null);

    internal bool IsEmpty => DailyLoss is null && DailyTarget is null && MaxTrades is null;

    /// <summary>
    /// The limits that hold now when <paramref name="wanted"/> is asked for: each limit that is stricter takes effect at
    /// once, and each that is looser or turned off waits for the next trading day, so a bad moment cannot undo a limit.
    /// A lower amount or count is stricter, and so is any limit where there was none.
    /// </summary>
    public OwnLimits StricterOf(OwnLimits wanted) =>
        new(Stricter(DailyLoss, wanted.DailyLoss), Stricter(DailyTarget, wanted.DailyTarget), Stricter(MaxTrades, wanted.MaxTrades));

    private static T? Stricter<T>(T? now, T? wanted)
        where T : struct, IComparable<T> =>
        wanted is { } value && (now is not { } current || value.CompareTo(current) < 0) ? value : now;
}

/// <summary>
/// The trader's own limits on an account now. <paramref name="Pending"/> are the limits from the next trading day, when
/// the trader loosened one. <paramref name="LossLevel"/> and <paramref name="TargetLevel"/> are the equity at which the
/// daily loss limit and profit target are reached today. <paramref name="Lock"/> is set while new orders are locked.
/// </summary>
public sealed record OwnLimitsSnapshot(
    OwnLimits Limits,
    OwnLimits? Pending,
    TradingDay TradingDay,
    decimal DayStartBalance,
    decimal? LossLevel,
    decimal? TargetLevel,
    int TradesToday,
    DateTimeOffset NextDayStart,
    OwnLock? Lock);

/// <summary>New orders are locked until <paramref name="Until"/>, the start of the next trading day, for the reason.</summary>
public sealed record OwnLock(DateTimeOffset Until, LockReason Reason);

/// <summary>Why new orders are locked until the next trading day.</summary>
public enum LockReason
{
    /// <summary>The trader locked the rest of the day.</summary>
    Trader,

    /// <summary>The trader's own daily loss limit was reached.</summary>
    DailyLoss,

    /// <summary>The trader's own daily profit target was reached.</summary>
    DailyTarget,
}
