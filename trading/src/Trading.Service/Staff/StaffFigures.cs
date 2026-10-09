using System.Reflection;

using Microsoft.Extensions.Options;

using Trading.Engine;
using Trading.Service.Api;
using Trading.Service.Candles;
using Trading.Service.Configuration;
using Trading.Service.Engine;
using Trading.Service.Feeds;
using Trading.Service.Identity;
using Trading.Service.Persistence;
using Trading.Service.Realtime;
using Trading.Service.Tenancy;

namespace Trading.Service.Staff;

/// <summary>
/// Puts together what the staff panel shows (ADR 0057). Everything is read when asked: the engine's accounts and positions,
/// the stores, and the figures kept in memory. Nothing is counted ahead.
/// </summary>
internal sealed class StaffFigures(
    EngineHost engine,
    EngineConfiguration configuration,
    EngineMetrics metrics,
    IEngineJournal journal,
    IUserStore users,
    IChartStore charts,
    ChartHistory history,
    TenantCatalog tenants,
    TenantProvisioner provisioner,
    PartnerCatalog partners,
    PartnerActivity partnerActivity,
    TenantActivity tenantActivity,
    SubscriptionRegistry subscriptions,
    PlatformWatcher watcher,
    PlatformEvents events,
    MarketCatalog market,
    IPriceFeed feed,
    IOptions<TradingOptions> trading,
    IOptions<ChartOptions> chartOptions,
    IOptions<JournalOptions> journalOptions,
    TimeProvider time)
{
    /// <summary>A firm's system that has not asked for its events for this long, while events wait, needs us.</summary>
    public static readonly TimeSpan EventsStaleAfter = TimeSpan.FromMinutes(5);

    /// <summary>Traders who came within this time are new.</summary>
    public static readonly TimeSpan NewTraders = TimeSpan.FromDays(7);

    /// <summary>A gap found within this time that could not be filled needs us.</summary>
    public static readonly TimeSpan RecentGaps = TimeSpan.FromDays(7);

    /// <summary>This many inputs waiting for the engine needs us.</summary>
    public const int QueueBehind = 1_000;

    /// <summary>A save of the journal this slow in the last 5 minutes needs us.</summary>
    public static readonly TimeSpan SlowSave = TimeSpan.FromSeconds(1);

    private const int MaxWaitingCounted = 1_001;
    private const string ValueCurrency = "USD";

    public async Task<StaffOverviewResponse> OverviewAsync(CancellationToken cancellationToken)
    {
        var state = await LoadAsync(cancellationToken);
        var all = tenants.All;
        var minutes = metrics.LastHour();
        return new StaffOverviewResponse(
            state.Now,
            all.Count,
            all.Count(t => t.Listed),
            await NeedsUsAsync(state, minutes, cancellationToken),
            Figures(state, null),
            FeedSummary(state, minutes),
            await EngineSummaryAsync(minutes, cancellationToken),
            [.. Exposure(state.Positions).OrderByDescending(s => Math.Abs(s.NetValue)).Take(5)],
            await events.LatestAsync(null, 8, cancellationToken));
    }

    public async Task<StaffServersResponse> ServersAsync(string? group, string? search, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(cancellationToken);
        var currencies = await engine.QueryAsync(
            e => tenants.All.ToDictionary(t => t.Id, t => (IReadOnlyList<string>)[.. t.Groups.Select(e.GetGroup).OfType<TradingGroup>().Select(g => g.Currency).Distinct()]),
            cancellationToken);
        var rows = new List<StaffServerRow>();
        foreach (var tenant in tenants.All)
        {
            var figures = Figures(state, tenant);
            var read = tenantActivity.LastEventRead(tenant.Id);
            rows.Add(new StaffServerRow(
                tenant.Id,
                tenant.Name,
                MakerOf(tenant),
                PartnerNameOf(tenant),
                tenant.CreatedBy,
                tenant.CreatedAt,
                tenant.Listed,
                tenant.Profile.Kind,
                currencies.GetValueOrDefault(tenant.Id) ?? [],
                figures.Traders,
                figures.AccountsTrading,
                figures.OpenPositions,
                read?.At,
                await WaitingEventsAsync(tenant, state.Now, cancellationToken) > 0));
        }

        var counts = new ServerCounts(
            rows.Count,
            rows.Count(r => r.EventsStale),
            rows.Count(r => r.Listed),
            rows.Count(r => !r.Listed),
            rows.Count(r => r.MadeBy == ServerMaker.Configuration));
        IEnumerable<StaffServerRow> shown = group switch
        {
            "needsUs" => rows.Where(r => r.EventsStale),
            "listed" => rows.Where(r => r.Listed),
            "notListed" => rows.Where(r => !r.Listed),
            "configuration" => rows.Where(r => r.MadeBy == ServerMaker.Configuration),
            _ => rows,
        };
        if (search?.Trim() is { Length: > 0 } text)
        {
            shown = shown.Where(r => r.Id.Contains(text, StringComparison.OrdinalIgnoreCase) || r.Name.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        return new StaffServersResponse([.. shown.OrderByDescending(r => r.AccountsTrading).ThenBy(r => r.Id, StringComparer.Ordinal)], counts);
    }

    public async Task<StaffServerResponse?> ServerAsync(string id, CancellationToken cancellationToken)
    {
        if (tenants.ById(id) is not { } tenant)
        {
            return null;
        }

        var state = await LoadAsync(cancellationToken);
        var groups = await engine.QueryAsync(
            e => tenant.Groups.Select(g => (Group: e.GetGroup(g), Changeable: e.IsCreatedGroup(g))).Where(g => g.Group is not null).ToList(),
            cancellationToken);
        var log = await events.LatestAsync(tenant.Id, 10, cancellationToken);
        var read = tenantActivity.LastEventRead(tenant.Id);
        var waiting = await WaitingEventsAsync(tenant, state.Now, cancellationToken, staleOnly: false);
        return new StaffServerResponse(
            tenant.Id,
            tenant.Name,
            MakerOf(tenant),
            PartnerNameOf(tenant),
            tenant.CreatedBy,
            tenant.CreatedAt,
            tenant.Listed,
            tenant.Listed ? log.FirstOrDefault(e => e.Kind == PlatformEventKind.ServerListed)?.At : null,
            tenant.LoginUrl,
            tenant.LogoUrl,
            new StaffTerminal(tenant.Profile.Kind, tenant.Profile.Modules, tenant.Profile.ConfirmOrders, tenant.Profile.PasswordLogin, provisioner.TerminalSetBy(tenant)),
            Figures(state, tenant),
            [.. groups.Select(g => new StaffGroupResponse(
                g.Group!.Id,
                g.Group.Currency,
                g.Changeable,
                state.Accounts.TryGetValue(g.Group.Id, out var counts) ? counts.Active + counts.Suspended + counts.Disabled : 0,
                [.. g.Group.Symbols.Select(s => new StaffGroupSymbol(
                    s.Symbol,
                    s.Leverage,
                    s.SpreadMarkupPoints,
                    s.CommissionPerLotPerSide,
                    state.Positions.Count(p => p.GroupId == g.Group.Id && p.Symbol == s.Symbol)))]))],
            new AdminKeyInfo(
                PartnerNameOf(tenant),
                log.FirstOrDefault(e => e.Kind == PlatformEventKind.AdminKeyReplaced)?.At ?? tenant.CreatedAt,
                tenantActivity.KeyLastUsed(tenant.Id)),
            new EventReadInfo(read?.At, read?.After, waiting, read is not null && waiting > 0 && state.Now - read.At > EventsStaleAfter),
            await users.TenantNoticeOfAsync(tenant.Id, cancellationToken),
            log);
    }

    public async Task<StaffServerEventsResponse?> ServerEventsAsync(string id, string? accountId, int limit, CancellationToken cancellationToken) =>
        tenants.ById(id) is { } tenant
            ? new StaffServerEventsResponse(await journal.ReadLatestGroupEventsAsync([.. tenant.Groups], accountId, limit, cancellationToken))
            : null;

    public async Task<StaffAccountResponse?> AccountAsync(string accountId, CancellationToken cancellationToken)
    {
        var account = await engine.QueryAsync(e => e.GetAccount(accountId), cancellationToken);
        if (account is null || tenants.ByGroup(account.GroupId) is not { } tenant)
        {
            return null;
        }

        return new StaffAccountResponse(
            account.AccountId,
            tenant.Id,
            tenant.Name,
            account.GroupId,
            account.Currency,
            account.Status,
            account.Balance,
            account.Equity,
            account.UsedMargin,
            account.Positions.Count,
            account.Orders.Count);
    }

    public async Task<StaffSearchResponse> SearchAsync(string? query, CancellationToken cancellationToken)
    {
        var text = query?.Trim() ?? "";
        if (text.Length == 0)
        {
            return new StaffSearchResponse([], null);
        }

        var servers = tenants.All
            .Where(t => t.Id.Contains(text, StringComparison.OrdinalIgnoreCase) || t.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => !t.Id.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select(t => new StaffSearchServer(t.Id, t.Name))
            .ToList();
        var account = await AccountAsync(text.TrimStart('#'), cancellationToken);
        return new StaffSearchResponse(servers, account);
    }

    public async Task<StaffPriceFeedResponse> PriceFeedAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var latest = (await engine.QueryAsync(e => e.GetLatestQuotes(), cancellationToken)).ToDictionary(q => q.Symbol, StringComparer.Ordinal);
        var silent = watcher.SilentSymbols;
        var perMinute = metrics.PricesLastMinute();
        var minutes = metrics.LastHour();
        var symbols = new List<StaffSymbolFeed>();
        foreach (var instrument in configuration.Instruments)
        {
            var quote = latest.GetValueOrDefault(instrument.Symbol);
            var open = instrument.TradingHours?.IsOpen(now) ?? true;
            var state = !open ? SymbolFeedState.Closed
                : silent.ContainsKey(instrument.Symbol) ? SymbolFeedState.Silent
                : quote is null ? SymbolFeedState.Waiting
                : SymbolFeedState.Live;
            symbols.Add(new StaffSymbolFeed(
                instrument.Symbol,
                market.CategoryOf(instrument.Symbol),
                instrument.Digits,
                quote?.Bid,
                quote?.Ask,
                quote?.Timestamp,
                perMinute.GetValueOrDefault(instrument.Symbol),
                open,
                instrument.TradingHours is null,
                instrument.TradingHours?.NextChange(now),
                state));
        }

        var info = await charts.GetHistoryInfoAsync(feed.Name, cancellationToken);
        var options = chartOptions.Value;
        return new StaffPriceFeedResponse(
            feed.Name,
            history.HasHistory,
            feed.FollowsTradingHours,
            now,
            latest.Count == 0 ? null : latest.Values.Max(q => q.Timestamp),
            Math.Round(metrics.PerSecond(InputKind.Prices), 1),
            watcher.LiveSince,
            watcher.FeedSilentSince,
            (int)PlatformWatcher.SilentAfter.TotalSeconds,
            configuration.MaxQuoteAge.TotalSeconds,
            [.. minutes.Select(m => m.Inputs[InputKind.Prices])],
            symbols,
            [.. (await charts.ListGapsAsync(feed.Name, 20, cancellationToken)).Select(ChartGapResponse.Of)],
            new ChartHistoryResponse(info?.LoadedAt, info?.Reach, (int)options.DayHistory.TotalDays, (int)options.History.TotalDays, (int)options.QuarterHourHistory.TotalDays, (int)options.MinuteHistory.TotalDays, history.IsReloading));
    }

    public async Task<StaffInstrumentsResponse> InstrumentsAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var weekStart = new DateTimeOffset(now.UtcDateTime.Date.AddDays(-(int)now.UtcDateTime.DayOfWeek), TimeSpan.Zero);
        var weekEnd = weekStart.AddDays(7);
        var hoursFeed = feed.FollowsTradingHours ? feed.Name : null;
        var options = trading.Value;
        var groupSymbols = await engine.QueryAsync(
            e => tenants.All.ToDictionary(
                t => t.Id,
                t => t.Groups.Select(e.GetGroup).OfType<TradingGroup>().SelectMany(g => g.Symbols.Select(s => s.Symbol)).ToHashSet(StringComparer.Ordinal)),
            cancellationToken);

        var instruments = new List<StaffInstrument>();
        foreach (var instrument in configuration.Instruments)
        {
            var periods = instrument.TradingHours is { } hours
                ? hours.PeriodsBetween(weekStart, weekEnd)
                    .Select(p => new MarketPeriod(p.Opens < weekStart ? weekStart : p.Opens, p.Closes > weekEnd ? weekEnd : p.Closes))
                    .ToList()
                : [new MarketPeriod(weekStart, weekEnd)];
            instruments.Add(new StaffInstrument(
                instrument.Symbol,
                market.CategoryOf(instrument.Symbol),
                instrument.BaseCurrency,
                instrument.QuoteCurrency,
                instrument.ContractSize,
                instrument.Digits,
                instrument.VolumeMin,
                instrument.VolumeStep,
                instrument.VolumeMax,
                hoursFeed is null ? null : options.HoursNameFor(instrument.Symbol, hoursFeed),
                groupSymbols.Values.Count(s => s.Contains(instrument.Symbol)),
                periods));
        }

        var used = instruments.Where(i => i.Hours is not null).GroupBy(i => i.Hours!, StringComparer.Ordinal).ToList();
        var hoursList = used
            .Select(g => (Name: g.Key, Options: options.TradingHours[g.Key], Symbols: g.Select(i => i.Symbol).ToList()))
            .OrderBy(h => h.Name, StringComparer.Ordinal)
            .ToList();
        var closures = new List<StaffClosure>();
        foreach (var (name, hoursOptions, hourSymbols) in hoursList)
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(hoursOptions.TimeZone);
            var localNow = TimeZoneInfo.ConvertTime(now, zone).DateTime;
            foreach (var closure in hoursOptions.ToTradingHours().Closures ?? [])
            {
                if (closure.To > localNow)
                {
                    closures.Add(new StaffClosure(name, hoursOptions.TimeZone, closure.From, closure.To, hourSymbols));
                }
            }
        }

        return new StaffInstrumentsResponse(
            feed.Name,
            feed.FollowsTradingHours,
            weekStart,
            instruments,
            [.. hoursList.Select(h => new StaffTradingHours(h.Name, h.Options.TimeZone, h.Options.Sessions, h.Symbols))],
            [.. closures.OrderBy(c => c.From).ThenBy(c => c.Hours, StringComparer.Ordinal)]);
    }

    public async Task<StaffExposureResponse> ExposureAsync(string? serverId, CancellationToken cancellationToken)
    {
        var positions = await engine.QueryAsync(e => e.GetOpenPositions(ValueCurrency), cancellationToken);
        if (serverId is not null)
        {
            var groups = tenants.ById(serverId)?.Groups ?? [];
            positions = [.. positions.Where(p => groups.Contains(p.GroupId, StringComparer.Ordinal))];
        }

        var valued = positions.Where(p => p.Value is not null).ToList();
        var servers = positions
            .GroupBy(p => tenants.ByGroup(p.GroupId)?.Id ?? p.GroupId, StringComparer.Ordinal)
            .Select(g => new ServerExposureResponse(
                g.Key,
                tenants.ById(g.Key)?.Name ?? g.Key,
                g.Count(),
                g.Sum(p => Signed(p) ?? 0m),
                g.Sum(p => p.Profit ?? 0m)))
            .OrderByDescending(s => Math.Abs(s.NetValue))
            .ToList();
        return new StaffExposureResponse(
            ValueCurrency,
            valued.Where(p => p.Side == Side.Buy).Sum(p => p.Value!.Value),
            valued.Where(p => p.Side == Side.Sell).Sum(p => p.Value!.Value),
            valued.Sum(p => Signed(p)!.Value),
            positions.Sum(p => p.Profit ?? 0m),
            positions.Sum(p => p.Margin ?? 0m),
            positions.Count(p => p.Side == Side.Buy),
            positions.Count(p => p.Side == Side.Sell),
            positions.Select(p => p.AccountId).Distinct(StringComparer.Ordinal).Count(),
            positions.Count - valued.Count,
            [.. Exposure(positions).OrderByDescending(s => Math.Abs(s.NetValue)).ThenBy(s => s.Symbol, StringComparer.Ordinal)],
            [.. valued
                .OrderByDescending(p => p.Value)
                .Take(10)
                .Select(p => new PositionExposureResponse(p.AccountId, tenants.ByGroup(p.GroupId)?.Id ?? p.GroupId, p.PositionId, p.Symbol, p.Side, p.Volume, p.Value!.Value, p.Profit ?? 0m))],
            servers);
    }

    public async Task<StaffEngineResponse> EngineAsync(CancellationToken cancellationToken)
    {
        var minutes = metrics.LastHour();
        var stats = await journal.GetStatsAsync(cancellationToken);
        var lastInput = await journal.GetLastInputSequenceAsync(cancellationToken);
        var lastEvent = await journal.GetLastEventSequenceAsync(cancellationToken);
        var inputsPerSecond = metrics.PerSecond(null);
        long? perDay = stats.SizeBytes is { } bytes && lastInput > 0 ? (long)(bytes / (decimal)lastInput * inputsPerSecond * 86_400m) : null;
        var terminals = subscriptions.ByGroup();
        return new StaffEngineResponse(
            Healthy(),
            engine.JournalFailed,
            engine.QueueLength,
            Math.Round(inputsPerSecond, 1),
            Math.Round(metrics.PerSecond(InputKind.Prices), 1),
            [.. minutes.Select(m => new EngineMinuteResponse(
                m.Start,
                m.Inputs,
                m.Refused,
                m.MaxQueue,
                m.Saves,
                Milliseconds(m.AverageSave),
                Milliseconds(m.SlowestSave)))],
            lastInput,
            lastEvent,
            stats.SizeBytes,
            perDay,
            stats.Snapshots,
            journalOptions.Value.SnapshotInterval,
            journalOptions.Value.SnapshotsToKeep,
            metrics.Start is { } start ? new EngineStartResponse(start.At, (long)start.Took.TotalMilliseconds, start.Replayed, start.ConfigurationChanged) : null,
            Version(),
            subscriptions.Connections(),
            terminals.Values.Sum(t => t.Accounts),
            [.. partners.All.Select(p => new StaffPartner(p.Id, p.Name, tenants.All.Count(t => t.PartnerId == p.Id), partnerActivity.LastCall(p.Id)))]);
    }

    private static ServerMaker MakerOf(Tenant tenant) =>
        tenant.PartnerId is not null ? ServerMaker.Partner : tenant.CreatedBy is not null ? ServerMaker.Staff : ServerMaker.Configuration;

    private static decimal? Signed(PositionValue position) => position.Side == Side.Buy ? position.Value : -position.Value;

    private static decimal Milliseconds(TimeSpan span) => Math.Round((decimal)span.TotalMilliseconds, 1);

    private static IEnumerable<SymbolExposureResponse> Exposure(IReadOnlyList<PositionValue> positions) =>
        positions
            .GroupBy(p => p.Symbol, StringComparer.Ordinal)
            .Select(g =>
            {
                var longLots = g.Where(p => p.Side == Side.Buy).Sum(p => p.Volume);
                var shortLots = g.Where(p => p.Side == Side.Sell).Sum(p => p.Volume);
                return new SymbolExposureResponse(
                    g.Key,
                    longLots,
                    shortLots,
                    longLots - shortLots,
                    g.Sum(p => Signed(p) ?? 0m),
                    g.Sum(p => p.Profit ?? 0m),
                    g.Select(p => p.AccountId).Distinct(StringComparer.Ordinal).Count(),
                    g.Count());
            });

    // The version with the commit it was built from, shortened like git does: 1.0.0+ef78719.
    private static string Version()
    {
        var version = typeof(StaffFigures).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 && version.Length > plus + 8 ? version[..(plus + 8)] : version;
    }

    private bool Healthy() => engine.Ready.IsCompletedSuccessfully && !engine.JournalFailed;

    private string? PartnerNameOf(Tenant tenant) =>
        tenant.PartnerId is { } id ? partners.All.FirstOrDefault(p => p.Id == id)?.Name ?? id : null;

    private async Task<PlatformState> LoadAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var (accounts, positions, quotes) = await engine.QueryAsync(
            e => (e.GetAccountCounts(), e.GetOpenPositions(ValueCurrency), e.GetLatestQuotes()),
            cancellationToken);
        var traders = await users.CountTradersAsync(now - NewTraders, cancellationToken);
        var today = await journal.CountGroupActivityAsync(new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero), cancellationToken);
        return new PlatformState(
            now,
            accounts,
            positions,
            quotes.Count == 0 ? null : quotes.Max(q => q.Timestamp),
            traders,
            today,
            subscriptions.ByGroup());
    }

    // The whole platform when the tenant is null.
    private static PlatformFigures Figures(PlatformState state, Tenant? tenant)
    {
        bool Mine(string groupId) => tenant is null || tenant.Groups.Contains(groupId, StringComparer.Ordinal);
        var accounts = state.Accounts.Where(a => Mine(a.Key)).Select(a => a.Value).ToList();
        var positions = state.Positions.Where(p => Mine(p.GroupId)).ToList();
        var today = state.Today.Where(a => Mine(a.Key)).Select(a => a.Value).ToList();
        var terminals = state.Terminals.Where(t => Mine(t.Key)).Select(t => t.Value).ToList();
        var traders = tenant is null
            ? new TraderCount(state.Traders.Values.Sum(t => t.Total), state.Traders.Values.Sum(t => t.New))
            : state.Traders.GetValueOrDefault(tenant.Id) ?? new TraderCount(0, 0);
        return new PlatformFigures(
            traders.Total,
            traders.New,
            accounts.Sum(a => a.Active),
            accounts.Sum(a => a.Suspended),
            accounts.Sum(a => a.Disabled),
            positions.Count,
            positions.Select(p => p.AccountId).Distinct(StringComparer.Ordinal).Count(),
            today.Sum(a => a.PositionsOpened),
            today.Sum(a => a.Refused),
            today.Sum(a => a.RefusedForOldPrices),
            terminals.Sum(t => t.Connections),
            terminals.Sum(t => t.Accounts));
    }

    private FeedSummary FeedSummary(PlatformState state, IReadOnlyList<MinuteFigures> minutes)
    {
        var silent = watcher.SilentSymbols;
        var closed = configuration.Instruments.Count(i => !(i.TradingHours?.IsOpen(state.Now) ?? true));
        return new FeedSummary(
            feed.Name,
            state.LastPriceAt,
            configuration.Instruments.Count,
            configuration.Instruments.Count - closed - silent.Count,
            closed,
            silent.Count,
            Math.Round(metrics.PerSecond(InputKind.Prices), 1),
            watcher.LiveSince,
            watcher.FeedSilentSince,
            [.. minutes.Select(m => m.Inputs[InputKind.Prices])]);
    }

    private async Task<EngineSummary> EngineSummaryAsync(IReadOnlyList<MinuteFigures> minutes, CancellationToken cancellationToken)
    {
        var recent = minutes.TakeLast(5).ToList();
        var saves = recent.Sum(m => m.Saves);
        var average = saves == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(recent.Sum(m => m.AverageSave.Ticks * m.Saves) / saves);
        var stats = await journal.GetStatsAsync(cancellationToken);
        return new EngineSummary(
            Healthy(),
            engine.QueueLength,
            minutes.Max(m => m.MaxQueue),
            Milliseconds(average),
            Milliseconds(minutes.Max(m => m.SlowestSave)),
            await journal.GetLastInputSequenceAsync(cancellationToken),
            stats.Snapshots.Count > 0 ? stats.Snapshots[0] : null,
            metrics.Start?.At);
    }

    private async Task<IReadOnlyList<NeedsUsItem>> NeedsUsAsync(PlatformState state, IReadOnlyList<MinuteFigures> minutes, CancellationToken cancellationToken)
    {
        var items = new List<NeedsUsItem>();
        if (watcher.FeedSilentSince is { } silentSince)
        {
            items.Add(new NeedsUsItem(
                NeedsUsKind.FeedSilent,
                silentSince,
                null,
                null,
                state.Positions.Select(p => p.AccountId).Distinct(StringComparer.Ordinal).Count(),
                null));
        }
        else
        {
            foreach (var (symbol, last) in watcher.SilentSymbols.OrderBy(s => s.Key, StringComparer.Ordinal))
            {
                items.Add(new NeedsUsItem(
                    NeedsUsKind.SymbolSilent,
                    last,
                    symbol,
                    null,
                    state.Positions.Where(p => p.Symbol == symbol).Select(p => p.AccountId).Distinct(StringComparer.Ordinal).Count(),
                    null));
            }
        }

        foreach (var tenant in tenants.All.OrderBy(t => t.Id, StringComparer.Ordinal))
        {
            if (await WaitingEventsAsync(tenant, state.Now, cancellationToken) is var waiting and > 0)
            {
                items.Add(new NeedsUsItem(NeedsUsKind.EventsNotRead, tenantActivity.LastEventRead(tenant.Id)!.At, null, tenant.Id, waiting, null));
            }
        }

        foreach (var gap in await charts.ListGapsAsync(feed.Name, 50, cancellationToken))
        {
            if (gap.State == ChartGapState.NotFilled && state.Now - gap.FoundAt <= RecentGaps)
            {
                items.Add(new NeedsUsItem(NeedsUsKind.ChartGapNotFilled, gap.FoundAt, null, null, null, ChartGapResponse.Of(gap)));
            }
        }

        if (engine.QueueLength >= QueueBehind)
        {
            items.Add(new NeedsUsItem(NeedsUsKind.QueueBehind, null, null, null, engine.QueueLength, null));
        }

        var slowest = minutes.TakeLast(5).Max(m => m.SlowestSave);
        if (slowest >= SlowSave)
        {
            items.Add(new NeedsUsItem(NeedsUsKind.SlowSaves, null, null, null, (int)slowest.TotalMilliseconds, null));
        }

        return items;
    }

    // The events waiting for the firm's system, at most a little over a thousand. With staleOnly, only when it is late:
    // it asked before, but not for a while.
    private async Task<int> WaitingEventsAsync(Tenant tenant, DateTimeOffset now, CancellationToken cancellationToken, bool staleOnly = true)
    {
        if (tenantActivity.LastEventRead(tenant.Id) is not { } read || (staleOnly && now - read.At <= EventsStaleAfter) || tenant.Groups.Count == 0)
        {
            return 0;
        }

        return (await journal.ReadGroupEventsAsync([.. tenant.Groups], read.After, MaxWaitingCounted, cancellationToken)).Count;
    }

    private sealed record PlatformState(
        DateTimeOffset Now,
        IReadOnlyDictionary<string, AccountCounts> Accounts,
        IReadOnlyList<PositionValue> Positions,
        DateTimeOffset? LastPriceAt,
        IReadOnlyDictionary<string, TraderCount> Traders,
        IReadOnlyDictionary<string, GroupActivity> Today,
        IReadOnlyDictionary<string, (int Connections, int Accounts)> Terminals);
}
