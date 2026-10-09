using System.Globalization;
using System.Text.Json.Nodes;

using Prop.Api.Firms;
using Prop.Api.Trading;
using Prop.Rules;

namespace Prop.Api.Tests.Support;

/// <summary>
/// A trading platform in memory that behaves like ours for the prop platform: opening an account, setting a
/// floor, closing an account and withdrawing become events in the stream of the firm whose group the account
/// is in, in order. Its partner API creates servers for firms that sign up, and those servers accept only
/// their newest key. Tests trade on it, breach floors and make it unreachable.
/// </summary>
internal sealed class FakeTradingPlatform(TimeProvider time) : ITradingPlatform, ITradingPartner
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, (string Key, int Keys)> _servers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _currencies = new(StringComparer.Ordinal);
    private bool _loseNextCreateAnswer;
    private readonly Dictionary<string, Guid> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Account> _accounts = new(StringComparer.Ordinal);
    private readonly List<(string Group, TradingEvent Event)> _events = [];
    private TaskCompletionSource _newEvents = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _sequence;
    private int _failures;
    private int _positions;

    /// <summary>Every command that reached the platform, in order, such as "open demo-firm-1001-1".</summary>
    public List<string> Commands { get; } = [];

    /// <summary>Servers someone else has on the platform.</summary>
    public HashSet<string> TakenServers { get; } = new(StringComparer.Ordinal);

    /// <summary>The next server is created, but the answer never arrives, as when the connection drops.</summary>
    public void LoseNextCreateAnswer()
    {
        lock (_lock)
        {
            _loseNextCreateAnswer = true;
        }
    }

    /// <summary>The key a server created through the partner API accepts now.</summary>
    public string? KeyOf(string server)
    {
        lock (_lock)
        {
            return _servers.TryGetValue(server, out var created) ? created.Key : null;
        }
    }

    /// <summary>Our staff stop the server's key in the trading platform's staff panel. The platform makes a new one that nobody sees.</summary>
    public void StopKey(string server)
    {
        lock (_lock)
        {
            var keys = _servers[server].Keys + 1;
            _servers[server] = ($"key-{server}-{keys}", keys);
        }
    }

    public Task<bool> IsServerAvailableAsync(string server, CancellationToken cancellationToken) =>
        Call($"server-name {server}", () => !_servers.ContainsKey(server) && !TakenServers.Contains(server));

    public async Task<PartnerTenant?> CreateTenantAsync(string server, string name, string currency, CancellationToken cancellationToken)
    {
        var tenant = await Call<PartnerTenant?>($"create server {server} {currency}", () =>
        {
            if (_servers.ContainsKey(server) || TakenServers.Contains(server))
            {
                return null;
            }

            _servers[server] = ($"key-{server}-1", 1);
            _currencies[server] = currency;
            return new PartnerTenant(server, [new PartnerGroup(GroupOf(server), currency)], $"key-{server}-1");
        });

        lock (_lock)
        {
            if (tenant is not null && _loseNextCreateAnswer)
            {
                _loseNextCreateAnswer = false;
                throw new TradingPlatformUnavailableException("The answer from the fake trading platform was lost.");
            }
        }

        return tenant;
    }

    public Task<PartnerTenant?> GetTenantAsync(string server, CancellationToken cancellationToken) =>
        Call<PartnerTenant?>($"get server {server}", () => _servers.ContainsKey(server) ? new PartnerTenant(server, [new PartnerGroup(GroupOf(server), _currencies[server])], null) : null);

    public Task<string> ReplaceAdminKeyAsync(string server, CancellationToken cancellationToken) =>
        Call($"replace key {server}", () =>
        {
            var keys = _servers[server].Keys + 1;
            _servers[server] = ($"key-{server}-{keys}", keys);
            return _servers[server].Key;
        });

    /// <summary>What each server created through the partner API was last told: whether it is listed, and where its traders log in.</summary>
    private readonly Dictionary<string, (bool Listed, Uri LoginUrl, Uri? LogoUrl)> _listings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TradingAccountDetails> _details = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TradingDayDefinition> _tradingDays = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TradingAccountRules> _rules = new(StringComparer.Ordinal);

    /// <summary>The symbols and conditions each group trades. Groups start with the standard four.</summary>
    public Dictionary<string, List<TradingSymbolConditions>> GroupSymbols { get; } = new(StringComparer.Ordinal);

    /// <summary>The next change of a group's symbols is refused with the reason, for example SymbolInUse.</summary>
    public string? RefuseNextSymbols { get; set; }

    public Task SetListingAsync(string server, bool listed, Uri loginUrl, Uri? logoUrl, CancellationToken cancellationToken) =>
        Call($"listing {server} {listed} {loginUrl}", () => _listings[server] = (listed, loginUrl, logoUrl));

    /// <summary>Whether the terminal lists the server, where its traders log in and its logo, as last told. Null before that.</summary>
    public (bool Listed, Uri LoginUrl, Uri? LogoUrl)? ListingOf(string server)
    {
        lock (_lock)
        {
            return _listings.TryGetValue(server, out var listing) ? listing : null;
        }
    }

    public Task<IReadOnlyList<TradingInstrument>> GetInstrumentsAsync(FirmTrading firm, CancellationToken cancellationToken) =>
        Call<IReadOnlyList<TradingInstrument>>(
            "instruments",
            () =>
            [
                new TradingInstrument("EURUSD", "EUR", "USD", 100_000m, 5),
                new TradingInstrument("GBPUSD", "GBP", "USD", 100_000m, 5),
                new TradingInstrument("USDJPY", "USD", "JPY", 100_000m, 3),
                new TradingInstrument("XAGUSD", "XAG", "USD", 5_000m, 3),
                new TradingInstrument("XAUUSD", "XAU", "USD", 100m, 2),
            ]);

    public Task<TradingGroupConditions?> GetGroupAsync(FirmTrading firm, CancellationToken cancellationToken) =>
        Call<TradingGroupConditions?>($"group {firm.Group}", () => new TradingGroupConditions(firm.Group, firm.Currency, _servers.ContainsKey(firm.Server), [.. SymbolsOf(firm.Group)]));

    public Task SetGroupSymbolsAsync(FirmTrading firm, IReadOnlyList<TradingSymbolConditions> symbols, CancellationToken cancellationToken) =>
        Call($"symbols {firm.Group} {string.Join(",", symbols.Select(s => s.Symbol))}", () =>
        {
            if (RefuseNextSymbols is { } reason)
            {
                RefuseNextSymbols = null;
                throw new TradingPlatformRejectedException($"The fake trading platform refused the symbols: {reason}", reason);
            }

            GroupSymbols[firm.Group] = [.. symbols];
            return true;
        });

    private List<TradingSymbolConditions> SymbolsOf(string group) =>
        GroupSymbols.TryGetValue(group, out var symbols)
            ? symbols
            :
            [
                new("EURUSD", 100, 2, 3.5m), new("GBPUSD", 100, 2, 3.5m), new("USDJPY", 100, 2, 3.5m), new("XAUUSD", 30, 10, 3.5m),
            ];

    /// <summary>The group a server created through the partner API gets, as on ours.</summary>
    public static string GroupOf(string server) => $"{server}-standard";

    /// <summary>How many of the calls told to fail have not happened yet.</summary>
    public int RemainingFailures
    {
        get
        {
            lock (_lock)
            {
                return _failures;
            }
        }
    }

    /// <summary>The next calls fail as if the platform could not be reached.</summary>
    public void FailNext(int calls)
    {
        lock (_lock)
        {
            _failures = calls;
        }
    }

    public Account AccountOf(string accountId)
    {
        lock (_lock)
        {
            return _accounts[accountId];
        }
    }

    public bool HasAccount(string accountId)
    {
        lock (_lock)
        {
            return _accounts.ContainsKey(accountId);
        }
    }

    /// <summary>A trader opens a position, paying <paramref name="commission"/> for it like on ours. Returns the position's id.</summary>
    public string OpenPosition(
        string accountId,
        string symbol = "EURUSD",
        TradeSide side = TradeSide.Buy,
        decimal volume = 1m,
        decimal openPrice = 1.1m,
        decimal commission = 0m)
    {
        Position position;
        lock (_lock)
        {
            position = new Position($"position-{++_positions}", symbol, side, volume, openPrice);
            var account = _accounts[accountId];
            account.Balance -= commission;
            account.Positions.Add(position);
        }

        Publish(
            accountId,
            "PositionOpened",
            a => new JsonObject
            {
                ["positionId"] = position.Id,
                ["symbol"] = symbol,
                ["side"] = side.ToString(),
                ["volume"] = volume,
                ["openPrice"] = openPrice,
                ["stopLoss"] = null,
                ["takeProfit"] = null,
                ["commission"] = commission,
                ["balanceAfter"] = a.Balance,
            },
            (s, t, id, raw, a) => new TradingPositionOpened(s, t, id, raw, position.Id, symbol, side, volume, openPrice, commission, a.Balance));
        return position.Id;
    }

    /// <summary>
    /// The oldest open position, or the one with the id, closes with <paramref name="profit"/> before
    /// <paramref name="commission"/>, like on ours. A close without an open position closes one that opened unseen.
    /// </summary>
    public void ClosePosition(
        string accountId,
        decimal profit,
        string? positionId = null,
        decimal closePrice = 1.1m,
        decimal commission = 0m,
        string reason = "Manual")
    {
        Position position;
        lock (_lock)
        {
            var account = _accounts[accountId];
            position = (positionId is null ? account.Positions.FirstOrDefault() : account.Positions.Single(p => p.Id == positionId))
                ?? new Position($"position-{++_positions}", "EURUSD", TradeSide.Buy, 1m, 1.1m);
            account.Positions.Remove(position);
            account.Balance += profit - commission;
        }

        Publish(
            accountId,
            "PositionClosed",
            a => new JsonObject
            {
                ["positionId"] = position.Id,
                ["symbol"] = position.Symbol,
                ["side"] = position.Side.ToString(),
                ["volume"] = position.Volume,
                ["openPrice"] = position.OpenPrice,
                ["closePrice"] = closePrice,
                ["profit"] = profit,
                ["commission"] = commission,
                ["reason"] = reason,
                ["balanceAfter"] = a.Balance,
            },
            (s, t, id, raw, a) => new TradingPositionClosed(
                s, t, id, raw, position.Id, position.Symbol, position.Side, position.Volume, position.OpenPrice, closePrice, profit, commission, reason, a.Balance));
    }

    /// <summary>Closes <paramref name="volume"/> of the oldest open position, with <paramref name="profit"/> before <paramref name="commission"/>.</summary>
    public void ClosePartOfPosition(string accountId, decimal volume, decimal profit, decimal closePrice = 1.1m, decimal commission = 0m)
    {
        Position position;
        lock (_lock)
        {
            var account = _accounts[accountId];
            var open = account.Positions[0];
            position = open with { Volume = open.Volume - volume };
            account.Positions[0] = position;
            account.Balance += profit - commission;
        }

        Publish(
            accountId,
            "PositionPartiallyClosed",
            a => new JsonObject
            {
                ["positionId"] = position.Id,
                ["symbol"] = position.Symbol,
                ["side"] = position.Side.ToString(),
                ["volume"] = volume,
                ["remainingVolume"] = position.Volume,
                ["openPrice"] = position.OpenPrice,
                ["closePrice"] = closePrice,
                ["profit"] = profit,
                ["commission"] = commission,
                ["reason"] = "Manual",
                ["balanceAfter"] = a.Balance,
            },
            (s, t, id, raw, a) => new TradingPositionPartiallyClosed(s, t, id, raw, position.Id, volume, position.Volume, closePrice, profit, commission, a.Balance));
    }

    /// <summary>The floor is breached, so the platform disables the account, like ours does.</summary>
    public void Breach(string accountId, string floorId, decimal equity)
    {
        var level = AccountOf(accountId).Floors[floorId];
        Publish(
            accountId,
            "EquityFloorBreached",
            _ => new JsonObject { ["floorId"] = floorId, ["level"] = level, ["equity"] = equity, ["positions"] = new JsonArray() },
            (s, t, id, raw, _) => new TradingFloorBreached(s, t, id, raw, floorId, level, equity));
        Disable(accountId);
    }

    public Task<Guid> EnsureUserAsync(FirmTrading firm, string email, string password, CancellationToken cancellationToken) =>
        Call($"user {email}", () =>
        {
            RequireKey(firm);
            if (!_users.TryGetValue(email, out var id))
            {
                id = Guid.NewGuid();
                _users[email] = id;
            }

            return id;
        });

    public async Task OpenAccountAsync(FirmTrading firm, string accountId, decimal initialBalance, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var created = await Call($"open {accountId}", () =>
        {
            RequireKey(firm);
            return _accounts.TryAdd(accountId, new Account(initialBalance, ownerUserId, firm.Group));
        });
        if (created)
        {
            Publish(accountId, "AccountCreated", a => new JsonObject { ["balance"] = a.Balance }, (s, t, id, raw, a) => new TradingAccountCreated(s, t, id, raw, a.Balance));
        }
    }

    public async Task SetFloorAsync(FirmTrading firm, string accountId, string floorId, FloorSpec floor, CancellationToken cancellationToken)
    {
        var level = await Call($"floor {accountId} {floorId}", () =>
        {
            var account = _accounts[accountId];
            decimal? set = account.Disabled ? null : floor switch
            {
                StartOfDayFloor start => account.Balance - start.Distance,
                FixedFloor fixedFloor => fixedFloor.Level,
                TrailingFloor trailing => Math.Min(account.Balance - trailing.Distance, trailing.LockLevel),
                _ => throw new ArgumentException("Unknown floor.", nameof(floor)),
            };
            if (set is { } value)
            {
                account.Floors[floorId] = value;
            }

            return set;
        });
        if (level is { } value)
        {
            Publish(accountId, "EquityFloorSet", _ => new JsonObject { ["floorId"] = floorId, ["level"] = value }, (s, t, id, raw, _) => new TradingFloorSet(s, t, id, raw, floorId, value));
        }
    }

    public async Task CloseAccountAsync(FirmTrading firm, string accountId, CancellationToken cancellationToken)
    {
        if (await Call($"close {accountId}", () => !_accounts[accountId].Disabled))
        {
            Disable(accountId);
        }
    }

    /// <summary>Like ours: a disabled account opens again with the balance and no floors. Done if it is open.</summary>
    public async Task ReopenAccountAsync(FirmTrading firm, string accountId, decimal balance, CancellationToken cancellationToken)
    {
        var reopened = await Call($"reopen {accountId} {balance.ToString(CultureInfo.InvariantCulture)}", () =>
        {
            var account = _accounts[accountId];
            if (!account.Disabled)
            {
                return false;
            }

            account.Disabled = false;
            account.Balance = balance;
            account.Floors.Clear();
            return true;
        });
        if (reopened)
        {
            Publish(accountId, "AccountReopened", _ => new JsonObject { ["balance"] = balance }, (s, t, id, raw, _) => new TradingAccountReopened(s, t, id, raw, balance));
        }
    }

    public Task SuspendAccountAsync(FirmTrading firm, string accountId, CancellationToken cancellationToken) =>
        SetSuspendedAsync(accountId, suspended: true);

    public Task ResumeAccountAsync(FirmTrading firm, string accountId, CancellationToken cancellationToken) =>
        SetSuspendedAsync(accountId, suspended: false);

    /// <summary>Like ours: applied once per operation id, and refused for a disabled account or when too little would be left.</summary>
    public async Task WithdrawAsync(FirmTrading firm, string accountId, string operationId, decimal amount, decimal minBalance, CancellationToken cancellationToken)
    {
        var withdrawn = await Call($"withdraw {accountId} {amount.ToString(CultureInfo.InvariantCulture)}", () =>
        {
            var account = _accounts[accountId];
            if (!account.Operations.Add(operationId))
            {
                return false;
            }

            if (account.Disabled || account.Balance - amount < minBalance)
            {
                account.Operations.Remove(operationId);
                throw new TradingPlatformRejectedException("The fake trading platform refused the withdrawal.", account.Disabled ? "AccountDisabled" : "InsufficientFunds");
            }

            account.Balance -= amount;
            return true;
        });
        if (withdrawn)
        {
            Publish(
                accountId,
                "BalanceAdjusted",
                a => new JsonObject { ["operationId"] = operationId, ["amount"] = -amount, ["balanceAfter"] = a.Balance },
                (s, t, id, raw, a) => new TradingBalanceAdjusted(s, t, id, raw, operationId, -amount, a.Balance));
        }
    }

    /// <summary>Like ours: applied once per operation id, and refused for a disabled account.</summary>
    public async Task DepositAsync(FirmTrading firm, string accountId, string operationId, decimal amount, CancellationToken cancellationToken)
    {
        var deposited = await Call($"deposit {accountId} {amount.ToString(CultureInfo.InvariantCulture)}", () =>
        {
            var account = _accounts[accountId];
            if (account.Disabled)
            {
                throw new TradingPlatformRejectedException("The fake trading platform refused the deposit.", "AccountDisabled");
            }

            if (!account.Operations.Add(operationId))
            {
                return false;
            }

            account.Balance += amount;
            return true;
        });
        if (deposited)
        {
            Publish(
                accountId,
                "BalanceAdjusted",
                a => new JsonObject { ["operationId"] = operationId, ["amount"] = amount, ["balanceAfter"] = a.Balance },
                (s, t, id, raw, a) => new TradingBalanceAdjusted(s, t, id, raw, operationId, amount, a.Balance));
        }
    }

    public Task DescribeAccountAsync(FirmTrading firm, string accountId, TradingAccountDetails details, CancellationToken cancellationToken) =>
        Call($"describe {accountId}", () => _details[accountId] = details);

    public Task SetTradingDayAsync(FirmTrading firm, string accountId, TradingDayDefinition day, CancellationToken cancellationToken) =>
        Call($"trading day {accountId}", () => _tradingDays[accountId] = day);

    /// <summary>When the account's trading day starts, as last told. Null before that.</summary>
    public TradingDayDefinition? TradingDayOf(string accountId)
    {
        lock (_lock)
        {
            return _tradingDays.GetValueOrDefault(accountId);
        }
    }

    /// <summary>The trader set limits for themselves in the terminal, which the account now shows (ADR 0054).</summary>
    public void SetOwnLimits(string accountId, TradingOwnLimits limits)
    {
        lock (_lock)
        {
            _accounts[accountId].OwnLimits = limits;
        }
    }

    /// <summary>The trader's own limit, or the trader, locked new orders until the next trading day, as ours does.</summary>
    public void LockDay(string accountId, OwnLockReason reason, DateTimeOffset until, decimal? limit, decimal dayResult, int positionsClosed) =>
        Publish(
            accountId,
            "TradingLocked",
            _ => new JsonObject
            {
                ["reason"] = reason.ToString(),
                ["until"] = until.ToString("O", CultureInfo.InvariantCulture),
                ["limit"] = limit,
                ["dayResult"] = dayResult,
                ["positionsClosed"] = positionsClosed,
            },
            (s, t, id, raw, _) => new TradingDayLocked(s, t, id, raw, reason, until, limit, dayResult, positionsClosed));

    /// <summary>What the terminal shows about the account, as last told. Null before that.</summary>
    public TradingAccountDetails? DetailsOf(string accountId)
    {
        lock (_lock)
        {
            return _details.GetValueOrDefault(accountId);
        }
    }

    private readonly Dictionary<(string AccountId, string PositionId), TradeReceipt> _receipts = [];
    private readonly Dictionary<string, BreachReport> _breachReports = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TradingNotice> _notices = new(StringComparer.Ordinal);
    private TradingImpact? _impact;

    /// <summary>The receipt the platform gives for the position.</summary>
    public void SetReceipt(TradeReceipt receipt)
    {
        lock (_lock)
        {
            _receipts[(receipt.AccountId, receipt.PositionId)] = receipt;
        }
    }

    /// <summary>The report the platform gives for the account's broken loss limit.</summary>
    public void SetBreachReport(BreachReport report)
    {
        lock (_lock)
        {
            _breachReports[report.AccountId] = report;
        }
    }

    /// <summary>What the platform says a period did to the firm's accounts, whatever period is asked for.</summary>
    public void SetImpact(TradingImpact impact)
    {
        lock (_lock)
        {
            _impact = impact;
        }
    }

    /// <summary>The periods the platform was asked about, in order.</summary>
    public List<(DateTimeOffset From, DateTimeOffset To)> ImpactRequests { get; } = [];

    /// <summary>How the price feed is doing. Healthy, with every market open, until a test says otherwise.</summary>
    public PriceFeedStatus? PriceFeed { get; set; }

    public Task<TradeReceipt?> GetReceiptAsync(FirmTrading firm, string accountId, string positionId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_receipts.GetValueOrDefault((accountId, positionId)));
        }
    }

    public Task<BreachReport?> GetBreachReportAsync(FirmTrading firm, string accountId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_breachReports.GetValueOrDefault(accountId));
        }
    }

    public Task<TradingImpact> GetImpactAsync(FirmTrading firm, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            ImpactRequests.Add((from, to));
            return Task.FromResult(_impact is null ? new TradingImpact(from, to, []) : _impact with { From = from, To = to });
        }
    }

    public Task SetNoticeAsync(FirmTrading firm, TradingNotice? notice, CancellationToken cancellationToken) =>
        Call($"notice {firm.Server}", () =>
        {
            if (notice is null)
            {
                _notices.Remove(firm.Server);
            }
            else
            {
                _notices[firm.Server] = notice;
            }

            return true;
        });

    /// <summary>The notice the firm's terminals show, or null.</summary>
    public TradingNotice? NoticeOf(string server)
    {
        lock (_lock)
        {
            return _notices.GetValueOrDefault(server);
        }
    }

    // The terminals' profiles and the traders' names are kept up to date in the background (ADR 0058), so they are not
    // counted among the commands.
    private readonly Dictionary<string, TerminalProfile> _profiles = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string?> _names = [];

    /// <summary>The profile a server has until it is set, the same as on our trading platform.</summary>
    public static readonly TerminalProfile StandardProfile = new(
        TerminalKind.Prop,
        TerminalModules.All,
        false,
        new TerminalStartingSize(StartingSizeKind.Smallest, null),
        true,
        new TerminalLinks(null, null, null, null, null),
        null);

    public Task<TerminalProfile> GetTerminalProfileAsync(FirmTrading firm, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_profiles.GetValueOrDefault(firm.Server) ?? StandardProfile);
        }
    }

    public Task SetTerminalProfileAsync(FirmTrading firm, TerminalProfile profile, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _profiles[firm.Server] = profile;
            ProfilesSet++;
            return Task.CompletedTask;
        }
    }

    /// <summary>How many times a profile was set, on any server.</summary>
    public int ProfilesSet { get; private set; }

    /// <summary>The server's terminal profile as last set, or null before that.</summary>
    public TerminalProfile? ProfileOf(string server)
    {
        lock (_lock)
        {
            return _profiles.GetValueOrDefault(server);
        }
    }

    public Task SetUserNameAsync(FirmTrading firm, Guid userId, string? name, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _names[userId] = name;
            return Task.CompletedTask;
        }
    }

    /// <summary>The user on the platform with the email, or null.</summary>
    public Guid? UserIdOf(string email)
    {
        lock (_lock)
        {
            return _users.TryGetValue(email, out var id) ? id : null;
        }
    }

    /// <summary>The name the terminal shows for the user, and whether one was ever told.</summary>
    public (bool Told, string? Name) NameOf(Guid userId)
    {
        lock (_lock)
        {
            return _names.TryGetValue(userId, out var name) ? (true, name) : (false, null);
        }
    }

    // Asked every few seconds, so it is not counted among the commands.
    public Task<PriceFeedStatus> GetPriceFeedAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return Task.FromResult(PriceFeed ?? new PriceFeedStatus("Synthetic", now, now, [new SymbolFeedStatus("EURUSD", now, true)]));
    }

    public Task DescribeRulesAsync(FirmTrading firm, string accountId, TradingAccountRules rules, CancellationToken cancellationToken) =>
        Call($"rules {accountId}", () => _rules[accountId] = rules);

    /// <summary>The account's rules as the terminal shows them, as last told. Null before that.</summary>
    public TradingAccountRules? RulesOf(string accountId)
    {
        lock (_lock)
        {
            return _rules.GetValueOrDefault(accountId);
        }
    }

    /// <summary>The balance changes on the platform without an event yet, as when a position closes just before a withdrawal.</summary>
    public void ChangeBalanceQuietly(string accountId, decimal change)
    {
        lock (_lock)
        {
            _accounts[accountId].Balance += change;
        }
    }

    public async Task<TradingEventPage> ReadEventsAsync(FirmTrading firm, long after, int limit, int waitSeconds, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task newEvents;
            lock (_lock)
            {
                RequireKey(firm);
                var events = _events.Where(e => e.Group == firm.Group && e.Event.Sequence > after).Select(e => e.Event).Take(limit).ToList();
                if (events.Count > 0)
                {
                    return new TradingEventPage(events, events[^1].Sequence);
                }

                newEvents = _newEvents.Task;
            }

            // A short real-time wait keeps the tests quick. The stream itself waits up to 30 seconds.
            if (await Task.WhenAny(newEvents, Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken)) != newEvents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new TradingEventPage([], after);
            }
        }
    }

    /// <summary>Open positions are now worth this much, so the account's equity differs from its balance.</summary>
    public void SetEquity(string accountId, decimal equity)
    {
        lock (_lock)
        {
            _accounts[accountId].Equity = equity;
        }
    }

    public Task<TradingAccountSnapshot?> GetAccountAsync(FirmTrading firm, string accountId, CancellationToken cancellationToken) =>
        Call($"get {accountId}", () =>
        {
            if (!_accounts.TryGetValue(accountId, out var account))
            {
                return null;
            }

            var equity = account.Equity ?? account.Balance;
            return (TradingAccountSnapshot?)new TradingAccountSnapshot(
                account.Balance,
                equity,
                [.. account.Floors.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => new TradingFloorSnapshot(f.Key, f.Value, equity - f.Value))],
                account.OwnLimits);
        });

    public Task<TradingLoginLink> CreateLoginLinkAsync(FirmTrading firm, Guid userId, string? accountId, CancellationToken cancellationToken) =>
        Call($"link {accountId}", () => new TradingLoginLink(new Uri($"https://trade.test/login/link?token=fake&account={accountId}"), time.GetUtcNow().AddMinutes(2)));

    // Like ours: a repeated suspend or resume, or one on a disabled account, is done without an event.
    private async Task SetSuspendedAsync(string accountId, bool suspended)
    {
        var changed = await Call($"{(suspended ? "suspend" : "resume")} {accountId}", () =>
        {
            var account = _accounts[accountId];
            if (account.Disabled || account.Suspended == suspended)
            {
                return false;
            }

            account.Suspended = suspended;
            return true;
        });
        if (changed)
        {
            Publish(accountId, suspended ? "AccountSuspended" : "AccountResumed", _ => [], (s, t, id, raw, _) => new TradingOtherEvent(s, t, id, raw));
        }
    }

    // Servers made through the partner API take only their newest key. Configured ones are not checked.
    private void RequireKey(FirmTrading firm)
    {
        if (_servers.TryGetValue(firm.Server, out var server) && server.Key != firm.ApiKey)
        {
            throw new TradingPlatformRejectedException($"The fake trading platform refused the key for {firm.Server}.");
        }
    }

    private void Disable(string accountId)
    {
        lock (_lock)
        {
            _accounts[accountId].Disabled = true;
        }

        Publish(accountId, "AccountDisabled", _ => [], (s, t, id, raw, _) => new TradingAccountDisabled(s, t, id, raw));
    }

    private Task<T> Call<T>(string command, Func<T> action)
    {
        lock (_lock)
        {
            if (_failures > 0)
            {
                _failures--;
                return Task.FromException<T>(new TradingPlatformUnavailableException("The fake trading platform is down."));
            }

            Commands.Add(command);
            return Task.FromResult(action());
        }
    }

    private void Publish(
        string accountId,
        string kind,
        Func<Account, JsonObject> fields,
        Func<long, DateTimeOffset, string, string, Account, TradingEvent> create)
    {
        lock (_lock)
        {
            var account = _accounts[accountId];
            var now = time.GetUtcNow();
            var raw = fields(account);
            raw["kind"] = kind;
            raw["accountId"] = accountId;
            raw["timestamp"] = now.ToString("O", CultureInfo.InvariantCulture);
            _events.Add((account.Group, create(++_sequence, now, accountId, raw.ToJsonString(), account)));
            _newEvents.TrySetResult();
            _newEvents = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    internal sealed class Account(decimal balance, Guid ownerUserId, string group)
    {
        public decimal Balance { get; set; } = balance;

        public Guid OwnerUserId { get; } = ownerUserId;

        /// <summary>The firm's group the account is in. Only that firm reads its events.</summary>
        public string Group { get; } = group;

        public decimal? Equity { get; set; }

        public bool Disabled { get; set; }

        /// <summary>No new positions can be opened, as on ours while the firm's month is unpaid.</summary>
        public bool Suspended { get; set; }

        public Dictionary<string, decimal> Floors { get; } = new(StringComparer.Ordinal);

        public TradingOwnLimits? OwnLimits { get; set; }

        public HashSet<string> Operations { get; } = new(StringComparer.Ordinal);

        /// <summary>The open positions, oldest first.</summary>
        public List<Position> Positions { get; } = [];
    }

    internal sealed record Position(string Id, string Symbol, TradeSide Side, decimal Volume, decimal OpenPrice);
}
