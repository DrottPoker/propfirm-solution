using System.Globalization;
using System.Text.RegularExpressions;

using Trading.Engine;

namespace Trading.Service.Configuration;

/// <summary>Instruments, groups and development accounts from the "Trading" configuration section.</summary>
public sealed class TradingOptions
{
    public const string SectionName = "Trading";

    public IReadOnlyList<InstrumentOptions> Instruments { get; init; } = [];

    /// <summary>Trading hours by name, which instruments refer to with <see cref="InstrumentOptions.TradingHours"/> (ADR 0050).</summary>
    public IReadOnlyDictionary<string, TradingHoursOptions> TradingHours { get; init; } = new Dictionary<string, TradingHoursOptions>();

    /// <summary>
    /// The hours a price feed quotes in, where they differ from the instrument's own: by feed name, then by symbol, the
    /// name of the hours in <see cref="TradingHours"/>, or empty when the feed quotes the symbol around the clock. A CFD
    /// broker can quote an index long after its exchange has closed, and the trader trades on the feed's prices.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> TradingHoursByFeed { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>();

    public IReadOnlyList<GroupOptions> Groups { get; init; } = [];

    public TimeSpan MaxQuoteAge { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Accounts created at startup. For local development.</summary>
    public IReadOnlyList<SeedAccountOptions> SeedAccounts { get; init; } = [];

    /// <summary>
    /// The engine's configuration, with the trading hours <paramref name="feed"/> quotes in. Without a feed every market
    /// is always open, which is how made-up prices run.
    /// </summary>
    public EngineConfiguration ToEngineConfiguration(string? feed)
    {
        var hours = TradingHours.ToDictionary(h => h.Key, h => h.Value.ToTradingHours(), StringComparer.Ordinal);
        return new(
            Instruments
                .Select(i => new Instrument(
                    i.Symbol,
                    i.BaseCurrency,
                    i.QuoteCurrency,
                    i.ContractSize,
                    i.Digits,
                    i.VolumeMin,
                    i.VolumeStep,
                    i.VolumeMax,
                    HoursNameOf(i, feed) is { Length: > 0 } name ? hours[name] : null))
                .ToList(),
            Groups
                .Select(g => new TradingGroup(
                    g.Id,
                    g.Currency,
                    g.StopOutLevelPercent,
                    g.Symbols.Select(s => new SymbolConditions(s.Symbol, s.Leverage, s.SpreadMarkupPoints, s.CommissionPerLotPerSide)).ToList()))
                .ToList(),
            MaxQuoteAge);
    }

    /// <summary>
    /// What is wrong with the trading hours, or null: hours that cannot be read or make no sense, and instruments that
    /// refer to hours that do not exist. Checked whatever the price feed, so a mistake shows before a real feed is used.
    /// </summary>
    public string? TradingHoursProblem(IReadOnlyCollection<string> feeds)
    {
        foreach (var (name, options) in TradingHours)
        {
            try
            {
                if (options.ToTradingHours().FindProblem() is { } problem)
                {
                    return $"Trading:TradingHours:{name}: {problem}";
                }
            }
            catch (FormatException e)
            {
                return $"Trading:TradingHours:{name}: {e.Message}";
            }
        }

        var unknown = Instruments.FirstOrDefault(i => !string.IsNullOrEmpty(i.TradingHours) && !TradingHours.ContainsKey(i.TradingHours));
        if (unknown is not null)
        {
            return $"Trading:Instruments: {unknown.Symbol} refers to trading hours '{unknown.TradingHours}', which are not in Trading:TradingHours.";
        }

        foreach (var (feed, bySymbol) in TradingHoursByFeed)
        {
            if (!feeds.Contains(feed, StringComparer.Ordinal))
            {
                return $"Trading:TradingHoursByFeed: '{feed}' is not a price feed. The feeds are {string.Join(", ", feeds)}.";
            }

            foreach (var (symbol, name) in bySymbol)
            {
                if (!Instruments.Any(i => string.Equals(i.Symbol, symbol, StringComparison.Ordinal)))
                {
                    return $"Trading:TradingHoursByFeed:{feed}: '{symbol}' is not an instrument.";
                }

                if (!string.IsNullOrEmpty(name) && !TradingHours.ContainsKey(name))
                {
                    return $"Trading:TradingHoursByFeed:{feed}: {symbol} refers to trading hours '{name}', which are not in Trading:TradingHours.";
                }
            }
        }

        return null;
    }

    /// <summary>The name of the trading hours the symbol follows with the feed, or null when it is always open (ADR 0057).</summary>
    public string? HoursNameFor(string symbol, string? feed) =>
        Instruments.FirstOrDefault(i => i.Symbol == symbol) is { } instrument && HoursNameOf(instrument, feed) is { Length: > 0 } name ? name : null;

    // The feed's own hours for the instrument when it has them, otherwise the instrument's. Empty for always open.
    private string? HoursNameOf(InstrumentOptions instrument, string? feed)
    {
        if (feed is null)
        {
            return null;
        }

        return TradingHoursByFeed.TryGetValue(feed, out var bySymbol) && bySymbol.TryGetValue(instrument.Symbol, out var name)
            ? name
            : instrument.TradingHours;
    }
}

/// <summary>
/// When a market is open, in its own time zone. Sessions are written like "Sun 17:00 - Fri 17:00". A closure is a whole
/// day like "2026-12-25", or a part like "2026-12-24 13:15 - 2026-12-27 18:00", in the same time zone.
/// </summary>
public sealed partial class TradingHoursOptions
{
    /// <summary>IANA id, for example America/New_York.</summary>
    public string TimeZone { get; init; } = "";

    public IReadOnlyList<string> Sessions { get; init; } = [];

    public IReadOnlyList<string> Closures { get; init; } = [];

    private static readonly string[] Days = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    /// <summary>The hours for the engine. Throws <see cref="FormatException"/> for a session or closure that cannot be read.</summary>
    public TradingHours ToTradingHours() =>
        new(TimeZone, [.. Sessions.Select(ParseSession)], [.. Closures.Select(ParseClosure)]);

    private static TradingSession ParseSession(string text)
    {
        var match = SessionPattern().Match(text);
        if (!match.Success)
        {
            throw new FormatException($"the session '{text}' is not like 'Sun 17:00 - Fri 17:00'.");
        }

        return new TradingSession(Day(match.Groups["openDay"].Value), Time(match.Groups["open"].Value, text), Day(match.Groups["closeDay"].Value), Time(match.Groups["close"].Value, text));
    }

    private static TradingClosure ParseClosure(string text)
    {
        if (DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return new TradingClosure(day.ToDateTime(TimeOnly.MinValue), day.AddDays(1).ToDateTime(TimeOnly.MinValue));
        }

        var match = ClosurePattern().Match(text);
        if (!match.Success
            || !DateTime.TryParseExact(match.Groups["from"].Value, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
            || !DateTime.TryParseExact(match.Groups["to"].Value, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
        {
            throw new FormatException($"the closure '{text}' is not like '2026-12-25' or '2026-12-24 13:15 - 2026-12-27 18:00'.");
        }

        return new TradingClosure(from, to);
    }

    private static DayOfWeek Day(string name) => (DayOfWeek)Array.IndexOf(Days, name);

    private static TimeOnly Time(string value, string session) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : throw new FormatException($"the session '{session}' has a time that does not exist.");

    [GeneratedRegex(@"^\s*(?<openDay>Sun|Mon|Tue|Wed|Thu|Fri|Sat) (?<open>\d\d:\d\d)\s*-\s*(?<closeDay>Sun|Mon|Tue|Wed|Thu|Fri|Sat) (?<close>\d\d:\d\d)\s*$")]
    private static partial Regex SessionPattern();

    [GeneratedRegex(@"^\s*(?<from>\d{4}-\d\d-\d\d \d\d:\d\d)\s*-\s*(?<to>\d{4}-\d\d-\d\d \d\d:\d\d)\s*$")]
    private static partial Regex ClosurePattern();
}

/// <summary>What kind of market an instrument is. The terminal lists instruments by it.</summary>
public enum InstrumentCategory
{
    Forex,
    Metals,
    Indices,
    Commodities,
    Crypto,
}

public sealed class InstrumentOptions
{
    public string Symbol { get; init; } = "";

    /// <summary>The instrument's name for traders, such as "Gold" or "Germany 40", which the terminal shows and searches (ADR 0058).</summary>
    public string? Name { get; init; }

    /// <summary>Required. Null only when the configuration lacks it, which stops the start.</summary>
    public InstrumentCategory? Category { get; init; }

    public string BaseCurrency { get; init; } = "";

    public string QuoteCurrency { get; init; } = "";

    public decimal ContractSize { get; init; }

    public int Digits { get; init; }

    public decimal VolumeMin { get; init; }

    public decimal VolumeStep { get; init; }

    public decimal VolumeMax { get; init; }

    /// <summary>The name of its hours in <see cref="TradingOptions.TradingHours"/>. Empty for a market that never closes, like crypto.</summary>
    public string? TradingHours { get; init; }
}

public sealed class GroupOptions
{
    public string Id { get; init; } = "";

    public string Currency { get; init; } = "";

    public decimal StopOutLevelPercent { get; init; }

    public IReadOnlyList<SymbolConditionsOptions> Symbols { get; init; } = [];
}

public sealed class SymbolConditionsOptions
{
    public string Symbol { get; init; } = "";

    public int Leverage { get; init; }

    public int SpreadMarkupPoints { get; init; }

    public decimal CommissionPerLotPerSide { get; init; }
}

public sealed class SeedAccountOptions
{
    public string AccountId { get; init; } = "";

    public string GroupId { get; init; } = "";

    public decimal InitialBalance { get; init; }

    /// <summary>The trader who owns the account. Created with this password if missing. Development only.</summary>
    public string OwnerEmail { get; init; } = "";

    public string OwnerPassword { get; init; } = "";

    public IReadOnlyList<SeedFloorOptions> Floors { get; init; } = [];
}

/// <summary>A fixed floor when only Level is set, a trailing floor when TrailingDistance is set.</summary>
public sealed class SeedFloorOptions
{
    public string FloorId { get; init; } = "";

    public decimal? Level { get; init; }

    public decimal? TrailingDistance { get; init; }

    public decimal? LockLevel { get; init; }

    public EquityFloorRule ToRule() =>
        TrailingDistance is { } distance ? new TrailingFloor(distance, LockLevel)
        : Level is { } level ? new FixedFloor(level)
        : throw new InvalidOperationException($"Seed floor {FloorId} needs Level or TrailingDistance.");
}
