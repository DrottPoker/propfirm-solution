using System.Globalization;
using System.Text.Json.Nodes;

using Prop.Api.Firms;
using Prop.Api.Trading;
using Prop.Rules;

namespace Prop.Api.Tests.Support;

/// <summary>
/// A trading platform in memory that behaves like ours for the prop platform: opening an account, setting a
/// floor, closing an account and withdrawing become events in the stream of the firm whose group the account
/// is in, in order. Tests trade on it, breach floors and make it unreachable.
/// </summary>
internal sealed class FakeTradingPlatform(TimeProvider time) : ITradingPlatform
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Guid> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Account> _accounts = new(StringComparer.Ordinal);
    private readonly List<(string Group, TradingEvent Event)> _events = [];
    private TaskCompletionSource _newEvents = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _sequence;
    private int _failures;

    /// <summary>Every command that reached the platform, in order, such as "open demo-firm-1001-1".</summary>
    public List<string> Commands { get; } = [];

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

    public void OpenPosition(string accountId) =>
        Publish(accountId, "PositionOpened", a => new JsonObject { ["balanceAfter"] = a.Balance }, (s, t, id, raw, a) => new TradingPositionOpened(s, t, id, raw, a.Balance));

    public void ClosePosition(string accountId, decimal profit)
    {
        lock (_lock)
        {
            _accounts[accountId].Balance += profit;
        }

        Publish(accountId, "PositionClosed", a => new JsonObject { ["balanceAfter"] = a.Balance }, (s, t, id, raw, a) => new TradingPositionClosed(s, t, id, raw, a.Balance));
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
            if (!_users.TryGetValue(email, out var id))
            {
                id = Guid.NewGuid();
                _users[email] = id;
            }

            return id;
        });

    public async Task OpenAccountAsync(FirmTrading firm, string accountId, decimal initialBalance, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var created = await Call($"open {accountId}", () => _accounts.TryAdd(accountId, new Account(initialBalance, ownerUserId, firm.Group)));
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
                [.. account.Floors.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => new TradingFloorSnapshot(f.Key, f.Value, equity - f.Value))]);
        });

    public Task<TradingLoginLink> CreateLoginLinkAsync(FirmTrading firm, Guid userId, string? accountId, CancellationToken cancellationToken) =>
        Call($"link {accountId}", () => new TradingLoginLink(new Uri($"https://trade.test/login/link?token=fake&account={accountId}"), time.GetUtcNow().AddMinutes(2)));

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

        public Dictionary<string, decimal> Floors { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Operations { get; } = new(StringComparer.Ordinal);
    }
}
