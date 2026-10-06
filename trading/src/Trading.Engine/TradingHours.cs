namespace Trading.Engine;

/// <summary>
/// When an instrument's market is open: sessions each week in the market's own time zone, less closures such as
/// holidays. While the market is closed the engine takes no orders, closes or stop changes in the instrument (ADR 0050).
/// The sessions follow the time zone's clock changes, so a session that opens at 17:00 New York time does so all year.
/// </summary>
/// <param name="TimeZone">IANA id of the market's time zone, for example America/New_York.</param>
/// <param name="Sessions">Open periods each week. They may not overlap, and the market must close some time each week.</param>
/// <param name="Closures">Periods in the market's local time when it is closed although a session says open, for example a holiday.</param>
public sealed record TradingHours(string TimeZone, IReadOnlyList<TradingSession> Sessions, IReadOnlyList<TradingClosure>? Closures = null)
{
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);

    // A session is shorter than a week, so this many days on each side finds every session around a period.
    private const int DaysAround = 8;

    // How far ahead the next opening is looked for. A closure longer than this reads as closed until further notice.
    private static readonly TimeSpan LookAhead = TimeSpan.FromDays(31);

    public bool IsOpen(DateTimeOffset at) => PeriodsBetween(at, at.AddTicks(1)).Count > 0;

    /// <summary>When the market closes if it is open at <paramref name="at"/>, or when it opens next if not. Null if it does not open within a month.</summary>
    public DateTimeOffset? NextChange(DateTimeOffset at)
    {
        var periods = PeriodsBetween(at, at + LookAhead);
        if (periods.Count == 0)
        {
            return null;
        }

        return periods[0].Opens <= at ? periods[0].Closes : periods[0].Opens;
    }

    /// <summary>
    /// The open periods that overlap <paramref name="from"/> to <paramref name="to"/>, whole and in UTC, oldest first.
    /// Sessions that touch are one period, and closures are cut out.
    /// </summary>
    public IReadOnlyList<MarketPeriod> PeriodsBetween(DateTimeOffset from, DateTimeOffset to)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
        var firstDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(from, zone).DateTime).AddDays(-DaysAround);
        var lastDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(to, zone).DateTime).AddDays(DaysAround);

        var sessions = new List<MarketPeriod>();
        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            foreach (var session in Sessions)
            {
                if (session.OpenDay == day.DayOfWeek)
                {
                    var opens = day.ToDateTime(session.OpenTime);
                    sessions.Add(new MarketPeriod(MomentOf(opens, zone), MomentOf(opens + session.Length, zone)));
                }
            }
        }

        sessions.Sort((a, b) => a.Opens.CompareTo(b.Opens));
        var periods = Merge(sessions);
        foreach (var closure in Closures ?? [])
        {
            var closed = new MarketPeriod(MomentOf(closure.From, zone), MomentOf(closure.To, zone));
            periods = periods.SelectMany(p => Without(p, closed)).ToList();
        }

        return periods.Where(p => p.Closes > from && p.Opens < to).ToList();
    }

    /// <summary>What is wrong with the hours, or null. The engine refuses a configuration with a problem.</summary>
    public string? FindProblem()
    {
        if (string.IsNullOrEmpty(TimeZone) || !TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone, out _))
        {
            return $"time zone '{TimeZone}' is unknown.";
        }

        if (Sessions is not { Count: > 0 } || Sessions.Any(s => s is null))
        {
            return "at least one session is required.";
        }

        if (Sessions.Any(s => !Enum.IsDefined(s.OpenDay) || !Enum.IsDefined(s.CloseDay)))
        {
            return "a session has an unknown day.";
        }

        // Each session as a start within the week, in order, must end before the next one starts.
        var spans = Sessions
            .Select(s => (Start: TimeSpan.FromDays((int)s.OpenDay) + s.OpenTime.ToTimeSpan(), s.Length))
            .OrderBy(s => s.Start)
            .ToList();
        if (spans.Sum(s => s.Length.Ticks) >= Week.Ticks)
        {
            return "the sessions must leave the market closed some time each week. A market that never closes has no trading hours.";
        }

        for (var i = 0; i < spans.Count; i++)
        {
            var nextStart = i + 1 < spans.Count ? spans[i + 1].Start : spans[0].Start + Week;
            if (spans[i].Start + spans[i].Length > nextStart)
            {
                return "sessions overlap.";
            }
        }

        if ((Closures ?? []).Any(c => c is null || c.From >= c.To))
        {
            return "a closure must end after it starts.";
        }

        return null;
    }

    // A local time as a moment. A time in the hour the clocks skip moves to the first time that exists, and a time in
    // the hour they repeat is the first of the two.
    private static DateTimeOffset MomentOf(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    private static List<MarketPeriod> Merge(List<MarketPeriod> sorted)
    {
        var merged = new List<MarketPeriod>();
        foreach (var period in sorted)
        {
            if (merged.Count > 0 && period.Opens <= merged[^1].Closes)
            {
                var last = merged[^1];
                merged[^1] = last with { Closes = period.Closes > last.Closes ? period.Closes : last.Closes };
            }
            else
            {
                merged.Add(period);
            }
        }

        return merged;
    }

    private static IEnumerable<MarketPeriod> Without(MarketPeriod period, MarketPeriod closed)
    {
        if (closed.Closes <= period.Opens || closed.Opens >= period.Closes)
        {
            yield return period;
            yield break;
        }

        if (period.Opens < closed.Opens)
        {
            yield return period with { Closes = closed.Opens };
        }

        if (closed.Closes < period.Closes)
        {
            yield return period with { Opens = closed.Closes };
        }
    }
}

/// <summary>
/// An open period each week, from a local day and time to the next time the market closes. Sunday 17:00 to Friday
/// 17:00 is five days, and Thursday 18:00 to Friday 17:00 is 23 hours.
/// </summary>
public sealed record TradingSession(DayOfWeek OpenDay, TimeOnly OpenTime, DayOfWeek CloseDay, TimeOnly CloseTime)
{
    /// <summary>How long the session is on the local clock, more than nothing and at most a week.</summary>
    public TimeSpan Length
    {
        get
        {
            var length = TimeSpan.FromDays(((int)CloseDay - (int)OpenDay + 7) % 7) + (CloseTime.ToTimeSpan() - OpenTime.ToTimeSpan());
            return length <= TimeSpan.Zero ? length + TimeSpan.FromDays(7) : length;
        }
    }
}

/// <summary>A period in the market's local time when it is closed, from <paramref name="From"/> up to <paramref name="To"/>.</summary>
public sealed record TradingClosure(DateTime From, DateTime To);

/// <summary>A period when a market is open, in UTC.</summary>
public sealed record MarketPeriod(DateTimeOffset Opens, DateTimeOffset Closes);
