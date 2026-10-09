using Trading.Engine;
using Trading.Service.Candles;
using Trading.Service.Configuration;
using Trading.Service.Engine;
using Trading.Service.Identity;
using Trading.Service.Persistence;
using Trading.Service.Staff;
using Trading.Service.Tenancy;

namespace Trading.Service.Api;

/// <summary>Our staff's email and password.</summary>
public sealed record StaffLoginRequest(string? Email, string? Password);

/// <summary>The logged in staff member.</summary>
public sealed record StaffMeResponse(string Email);

/// <summary>What needs our staff, and how it was made (ADR 0057).</summary>
public enum NeedsUsKind
{
    /// <summary>The whole feed has given no price for a minute while markets are open. <c>Since</c> is the last price, <c>Count</c> the accounts with open positions.</summary>
    FeedSilent,

    /// <summary>A symbol has had no price for a minute while its market is open. <c>Since</c> is its last price, null when it has had none, and <c>Count</c> the accounts that hold it.</summary>
    SymbolSilent,

    /// <summary>A firm's system has not asked for its events for a while, and events wait. <c>Since</c> is when it last asked, <c>Count</c> the events waiting, at most 1,001.</summary>
    EventsNotRead,

    /// <summary>A gap in the charts could not be filled. <c>Gap</c> is the gap.</summary>
    ChartGapNotFilled,

    /// <summary>Many inputs wait for the engine. <c>Count</c> is how many.</summary>
    QueueBehind,

    /// <summary>Saving the journal has been slow in the last minutes. <c>Count</c> is the slowest save in milliseconds.</summary>
    SlowSaves,
}

/// <summary>Something that needs our staff. Which fields are set depends on <paramref name="Kind"/>.</summary>
public sealed record NeedsUsItem(NeedsUsKind Kind, DateTimeOffset? Since, string? Symbol, string? ServerId, int? Count, ChartGapResponse? Gap);

/// <summary>Figures across the whole platform, or one server. Today is the day in UTC, and new traders came in the last 7 days.</summary>
public sealed record PlatformFigures(
    int Traders,
    int NewTraders,
    int AccountsTrading,
    int AccountsPaused,
    int AccountsClosed,
    int OpenPositions,
    int AccountsWithPositions,
    int PositionsOpenedToday,
    int RefusedToday,
    int RefusedForOldPricesToday,
    int TerminalsOpen,
    int AccountsWatched);

/// <summary>The price feed at a glance. <paramref name="PricesPerMinute"/> covers the last hour, oldest first.</summary>
public sealed record FeedSummary(
    string Feed,
    DateTimeOffset? LastPriceAt,
    int Symbols,
    int Live,
    int Closed,
    int Silent,
    decimal PricesPerSecond,
    DateTimeOffset? LiveSince,
    DateTimeOffset? SilentSince,
    IReadOnlyList<int> PricesPerMinute);

/// <summary>The engine at a glance. Save times are averaged over the last 5 minutes, and the slowest is this hour's.</summary>
public sealed record EngineSummary(
    bool Healthy,
    int QueueLength,
    int MaxQueueThisHour,
    decimal AverageSaveMilliseconds,
    decimal SlowestSaveMilliseconds,
    long LastInput,
    SnapshotInfo? LastSnapshot,
    DateTimeOffset? StartedAt);

/// <summary>The staff panel's overview (ADR 0057).</summary>
public sealed record StaffOverviewResponse(
    DateTimeOffset Now,
    int Servers,
    int ListedServers,
    IReadOnlyList<NeedsUsItem> NeedsUs,
    PlatformFigures Figures,
    FeedSummary Feed,
    EngineSummary Engine,
    IReadOnlyList<SymbolExposureResponse> TopExposure,
    IReadOnlyList<PlatformLogEntry> Latest);

/// <summary>Who made a server.</summary>
public enum ServerMaker
{
    /// <summary>A partner, such as Kronant Prop.</summary>
    Partner,

    /// <summary>One of our staff.</summary>
    Staff,

    /// <summary>The service's configuration, saved again at every start.</summary>
    Configuration,
}

/// <summary>
/// A server in the list. <paramref name="EventsReadAt"/> is when the firm's system last asked for its events since the
/// service started, and <paramref name="EventsStale"/> whether it has not for a while although events wait.
/// </summary>
public sealed record StaffServerRow(
    string Id,
    string Name,
    ServerMaker MadeBy,
    string? PartnerName,
    string? CreatedBy,
    DateTimeOffset? CreatedAt,
    bool Listed,
    TerminalKind Kind,
    IReadOnlyList<string> Currencies,
    int Traders,
    int AccountsTrading,
    int OpenPositions,
    DateTimeOffset? EventsReadAt,
    bool EventsStale);

public sealed record ServerCounts(int All, int NeedsUs, int Listed, int NotListed, int Configuration);

public sealed record StaffServersResponse(IReadOnlyList<StaffServerRow> Servers, ServerCounts Counts);

/// <summary>A symbol a group trades, with its conditions and the group's open positions in it.</summary>
public sealed record StaffGroupSymbol(string Symbol, int Leverage, int SpreadMarkupPoints, decimal CommissionPerLotPerSide, int OpenPositions);

/// <summary>A server's group. Only groups made for the firm can have their conditions changed by its system.</summary>
public sealed record StaffGroupResponse(string Id, string Currency, bool Changeable, int Accounts, IReadOnlyList<StaffGroupSymbol> Symbols);

/// <summary>
/// The server's admin key: who holds it, a partner or nobody we know of, when it was made or last replaced, and when it
/// was last used since the service started.
/// </summary>
public sealed record AdminKeyInfo(string? HeldBy, DateTimeOffset? MadeAt, DateTimeOffset? LastUsedAt);

/// <summary>When the firm's system last asked for its events, after which event, how many wait now, at most 1,001, and whether it is late.</summary>
public sealed record EventReadInfo(DateTimeOffset? LastReadAt, long? After, int Waiting, bool Stale);

/// <summary>
/// How the server's terminal works (ADR 0058): the kind of business, the parts it shows, whether orders ask before they
/// are sent and whether traders log in with a password. Only a kind our staff set can be changed here.
/// </summary>
public sealed record StaffTerminal(TerminalKind Kind, TerminalModules Modules, bool ConfirmOrders, bool PasswordLogin, TerminalSetBy SetBy);

/// <summary>One server in the staff panel (ADR 0057).</summary>
public sealed record StaffServerResponse(
    string Id,
    string Name,
    ServerMaker MadeBy,
    string? PartnerName,
    string? CreatedBy,
    DateTimeOffset? CreatedAt,
    bool Listed,
    DateTimeOffset? ListedAt,
    Uri? LoginUrl,
    Uri? LogoUrl,
    StaffTerminal Terminal,
    PlatformFigures Figures,
    IReadOnlyList<StaffGroupResponse> Groups,
    AdminKeyInfo AdminKey,
    EventReadInfo Events,
    TerminalNotice? Notice,
    IReadOnlyList<PlatformLogEntry> Log);

/// <summary>The server's latest events, of one account when asked, newest first.</summary>
public sealed record StaffServerEventsResponse(IReadOnlyList<EventEnvelope> Events);

/// <summary>
/// A new server made by our staff, for a firm that uses the platform on its own (ADR 0057), with its kind of business
/// (ADR 0058). Its traders log in to the terminal with a password.
/// </summary>
public sealed record StaffCreateServerRequest(string? Id, string? Name, string? Currency, TerminalKind? Kind);

/// <summary>A new server and the key to its admin API, which is shown only now.</summary>
public sealed record StaffCreatedServerResponse(string Id, string Name, string AdminApiKey);

/// <summary>Whether the server is on the list traders choose from, and its kind of business when our staff set it.</summary>
public sealed record StaffUpdateServerRequest(bool? Listed = null, TerminalKind? Kind = null);

/// <summary>Why our staff replace the server's admin key, for the platform's log.</summary>
public sealed record StaffReplaceKeyRequest(string? Reason);

/// <summary>
/// The new admin key, shown only now, or null when a partner holds the server's key: the partner then gets a new one
/// from the partner API when the old one is turned away, and nobody sees it here.
/// </summary>
public sealed record StaffReplacedKeyResponse(string? AdminApiKey, string? HeldBy);

/// <summary>Servers whose id or name matches the search, and the account with that number, if any.</summary>
public sealed record StaffSearchResponse(IReadOnlyList<StaffSearchServer> Servers, StaffAccountResponse? Account);

public sealed record StaffSearchServer(string Id, string Name);

/// <summary>An account as our staff see it: its server and numbers, never who the trader is.</summary>
public sealed record StaffAccountResponse(
    string AccountId,
    string ServerId,
    string ServerName,
    string GroupId,
    string Currency,
    AccountStatus Status,
    decimal Balance,
    decimal Equity,
    decimal UsedMargin,
    int OpenPositions,
    int PendingOrders);

/// <summary>How a symbol's prices are now.</summary>
public enum SymbolFeedState
{
    Live,

    /// <summary>Its market is open, but no price has come for a minute.</summary>
    Silent,

    /// <summary>Its market is closed.</summary>
    Closed,

    /// <summary>No price has come yet since the service started, for less than a minute.</summary>
    Waiting,
}

/// <summary>
/// A symbol's raw price from the feed, before any firm's markup, with the symbol's decimals, when it came, how many came
/// in the last whole minute, and whether its market is open, with when that changes. A market that never closes has <paramref name="AlwaysOpen"/>.
/// </summary>
public sealed record StaffSymbolFeed(
    string Symbol,
    InstrumentCategory Category,
    int Digits,
    decimal? Bid,
    decimal? Ask,
    DateTimeOffset? LastPriceAt,
    int PricesLastMinute,
    bool MarketOpen,
    bool AlwaysOpen,
    DateTimeOffset? NextChange,
    SymbolFeedState State);

/// <summary>A gap in the charts and what came of it.</summary>
public sealed record ChartGapResponse(
    Guid Id,
    DateTimeOffset From,
    DateTimeOffset Until,
    DateTimeOffset FoundAt,
    ChartGapState State,
    int Tries,
    DateTimeOffset? FinishedAt,
    int Bars,
    string? Problem)
{
    public static ChartGapResponse Of(ChartGap gap) =>
        new(gap.Id, gap.From, gap.Until, gap.FoundAt, gap.State, gap.Tries, gap.FinishedAt, gap.Bars, gap.Problem);
}

/// <summary>
/// The charts' history: when it was loaded and where it starts, how many whole days it reaches back in day, hour, 15
/// minute and minute bars, and whether it is being loaded again now.
/// </summary>
public sealed record ChartHistoryResponse(
    DateTimeOffset? LoadedAt,
    DateTimeOffset? Reach,
    int DayDays,
    int HistoryDays,
    int QuarterHourDays,
    int MinuteDays,
    bool Reloading);

/// <summary>
/// The price feed in the staff panel (ADR 0057). <paramref name="HasHistory"/> is false for made-up prices, which have no
/// true history to fill gaps from. Orders are refused on a price older than <paramref name="MaxPriceAgeSeconds"/>.
/// </summary>
public sealed record StaffPriceFeedResponse(
    string Feed,
    bool HasHistory,
    bool FollowsTradingHours,
    DateTimeOffset Now,
    DateTimeOffset? LastPriceAt,
    decimal PricesPerSecond,
    DateTimeOffset? LiveSince,
    DateTimeOffset? SilentSince,
    int SilentAfterSeconds,
    double MaxPriceAgeSeconds,
    IReadOnlyList<int> PricesPerMinute,
    IReadOnlyList<StaffSymbolFeed> Symbols,
    IReadOnlyList<ChartGapResponse> Gaps,
    ChartHistoryResponse History);

/// <summary>
/// An instrument on the platform, the name of the trading hours it follows with the current feed, null when always open,
/// how many servers trade it, and when it is open this week, Sunday to Sunday in UTC.
/// </summary>
public sealed record StaffInstrument(
    string Symbol,
    InstrumentCategory Category,
    string BaseCurrency,
    string QuoteCurrency,
    decimal ContractSize,
    int Digits,
    decimal VolumeMin,
    decimal VolumeStep,
    decimal VolumeMax,
    string? Hours,
    int Servers,
    IReadOnlyList<MarketPeriod> ThisWeek);

/// <summary>Named trading hours as configured, in the market's time zone, and the symbols that follow them with the current feed.</summary>
public sealed record StaffTradingHours(string Name, string TimeZone, IReadOnlyList<string> Sessions, IReadOnlyList<string> Symbols);

/// <summary>A closure ahead, in the market's local time, with the hours and symbols it closes.</summary>
public sealed record StaffClosure(string Hours, string TimeZone, DateTime From, DateTime To, IReadOnlyList<string> Symbols);

/// <summary>The instruments and trading hours in the staff panel (ADR 0057). The week starts on <paramref name="WeekStart"/>.</summary>
public sealed record StaffInstrumentsResponse(
    string Feed,
    bool FollowsTradingHours,
    DateTimeOffset WeekStart,
    IReadOnlyList<StaffInstrument> Instruments,
    IReadOnlyList<StaffTradingHours> Hours,
    IReadOnlyList<StaffClosure> Closures);

/// <summary>
/// What traders hold of a symbol: lots long and short, net lots, the net value and open profit in USD, the accounts that
/// hold it and the positions.
/// </summary>
public sealed record SymbolExposureResponse(
    string Symbol,
    decimal LongLots,
    decimal ShortLots,
    decimal NetLots,
    decimal NetValue,
    decimal OpenProfit,
    int Accounts,
    int Positions);

/// <summary>An open position valued in USD.</summary>
public sealed record PositionExposureResponse(string AccountId, string ServerId, string PositionId, string Symbol, Side Side, decimal Lots, decimal Value, decimal Profit);

/// <summary>What a server's traders hold, net, in USD.</summary>
public sealed record ServerExposureResponse(string ServerId, string Name, int Positions, decimal NetValue, decimal OpenProfit);

/// <summary>
/// What traders hold now, in USD, across every server or one (ADR 0057). <paramref name="Unvalued"/> counts positions
/// without a rate to USD yet, which are left out of the values.
/// </summary>
public sealed record StaffExposureResponse(
    string Currency,
    decimal LongValue,
    decimal ShortValue,
    decimal NetValue,
    decimal OpenProfit,
    decimal Margin,
    int LongPositions,
    int ShortPositions,
    int Accounts,
    int Unvalued,
    IReadOnlyList<SymbolExposureResponse> Symbols,
    IReadOnlyList<PositionExposureResponse> Largest,
    IReadOnlyList<ServerExposureResponse> Servers);

/// <summary>One minute of the engine loop. Save times are in milliseconds.</summary>
public sealed record EngineMinuteResponse(
    DateTimeOffset Start,
    IReadOnlyDictionary<InputKind, int> Inputs,
    int Refused,
    int MaxQueue,
    int Saves,
    decimal AverageSaveMilliseconds,
    decimal SlowestSaveMilliseconds);

/// <summary>How the engine started: when, how long the replay took, the inputs replayed after the snapshot, and whether the configuration had changed.</summary>
public sealed record EngineStartResponse(DateTimeOffset At, long Milliseconds, long Replayed, bool ConfigurationChanged);

/// <summary>A partner that makes servers, how many it has made, and when it last called since the service started.</summary>
public sealed record StaffPartner(string Id, string Name, int Servers, DateTimeOffset? LastCallAt);

/// <summary>
/// The engine, its journal and the terminals in the staff panel (ADR 0057). <paramref name="JournalBytes"/> is null when the
/// storage cannot tell, and <paramref name="JournalBytesPerDay"/> estimates the growth at the last minute's pace.
/// </summary>
public sealed record StaffEngineResponse(
    bool Healthy,
    bool JournalFailed,
    int QueueLength,
    decimal InputsPerSecond,
    decimal PricesPerSecond,
    IReadOnlyList<EngineMinuteResponse> Minutes,
    long LastInput,
    long LastEvent,
    long? JournalBytes,
    long? JournalBytesPerDay,
    IReadOnlyList<SnapshotInfo> Snapshots,
    int SnapshotInterval,
    int SnapshotsKept,
    EngineStartResponse? Start,
    string Version,
    int TerminalsOpen,
    int AccountsWatched,
    IReadOnlyList<StaffPartner> Partners);
