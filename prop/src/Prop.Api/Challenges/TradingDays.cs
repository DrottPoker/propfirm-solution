using Prop.Rules;

namespace Prop.Api.Challenges;

/// <summary>
/// Turns a challenge's trading day definition into days. A trading day starts at the definition's local
/// time in its time zone and is named by the local date it starts on, so a day that starts at 17:00 on a
/// Sunday is Sunday's.
/// </summary>
internal static class TradingDays
{
    public static bool IsKnownTimeZone(string timeZone) => TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out _);

    public static DateOnly DayOf(DateTimeOffset time, TradingDayDefinition definition)
    {
        var local = TimeZoneInfo.ConvertTime(time, Zone(definition)).DateTime;
        return DateOnly.FromDateTime((local - definition.Start.ToTimeSpan()).Date);
    }

    /// <summary>When the trading day after the one that <paramref name="time"/> is in starts.</summary>
    public static DateTimeOffset NextStart(DateTimeOffset time, TradingDayDefinition definition)
    {
        var zone = Zone(definition);
        var start = DayOf(time, definition).AddDays(1).ToDateTime(definition.Start);

        // A start in the hour the clocks skip moves to the first time that exists.
        while (zone.IsInvalidTime(start))
        {
            start = start.AddMinutes(1);
        }

        // A start in the hour the clocks repeat is the first of the two.
        var offset = zone.IsAmbiguousTime(start) ? zone.GetAmbiguousTimeOffsets(start).Max() : zone.GetUtcOffset(start);
        return new DateTimeOffset(start, offset);
    }

    private static TimeZoneInfo Zone(TradingDayDefinition definition) => TimeZoneInfo.FindSystemTimeZoneById(definition.TimeZone);
}
