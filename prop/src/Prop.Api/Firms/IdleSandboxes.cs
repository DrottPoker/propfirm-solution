using System.Collections.Concurrent;
using System.Globalization;

using Common.Postgres;

using Microsoft.Extensions.Options;

using Npgsql;

using Prop.Api.Billing;
using Prop.Api.Challenges;
using Prop.Api.Configuration;
using Prop.Api.Email;
using Prop.Api.Portal;
using Prop.Rules;

namespace Prop.Api.Firms;

/// <summary>
/// When a firm's administrators last used its admin panel (ADR 0045). Written at most once an hour per firm, since the
/// service runs as one instance. Coming back opens a sandbox that closed while nobody used it.
/// </summary>
internal sealed partial class FirmActivity(NpgsqlDataSource dataSource, DatabaseSchema schema, TimeProvider time, ILogger<FirmActivity> logger)
{
    private static readonly TimeSpan WriteInterval = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _written = new(StringComparer.Ordinal);

    public async Task SeenAsync(string firmId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (_written.TryGetValue(firmId, out var last) && now - last < WriteInterval)
        {
            return;
        }

        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            """
            update firms f set active_at = $2, idle_warned_at = null, sandbox_closed_at = null
            from (select id, sandbox_closed_at from firms where id = $1 for update) old
            where f.id = old.id
            returning old.sandbox_closed_at is not null
            """);
        command.Parameters.AddWithValue(firmId);
        command.Parameters.AddWithValue(now);
        if (await command.ExecuteScalarAsync(cancellationToken) is true)
        {
            LogReopened(logger, firmId);
        }

        _written[firmId] = now;
    }

    /// <summary>Forgets when the firm was last written, so the next visit is written at once, such as after its sandbox closed.</summary>
    public void Forget(string firmId) => _written.TryRemove(firmId, out _);

    [LoggerMessage(Level = LogLevel.Information, Message = "The sandbox of firm {FirmId} opened again, since an administrator came back")]
    private static partial void LogReopened(ILogger logger, string firmId);
}

/// <summary>
/// Closes the sandboxes nobody uses (ADR 0045): a firm that is not live, whose administrators have not used its admin panel
/// for <see cref="SandboxOptions.IdleDays"/>, is warned a week before, and then its test accounts end and no new ones
/// start. It opens again as soon as an administrator comes back. Only the admin panel counts, so calls to the firm API
/// keep no sandbox open.
/// </summary>
internal sealed partial class IdleSandboxWorker(
    NpgsqlDataSource dataSource,
    DatabaseSchema schema,
    FirmCatalog firms,
    ChallengeService challenges,
    FirmActivity activity,
    WorkSignals signals,
    IOptions<SandboxOptions> sandbox,
    IOptions<PlatformOptions> platform,
    TimeProvider time,
    ILogger<IdleSandboxWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>How long before closing the administrators are warned.</summary>
    public static readonly TimeSpan Warning = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await schema.EnsureAsync(stoppingToken);
        await firms.Ready.WaitAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception);
            }

            await Task.Delay(Interval, time, stoppingToken);
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var idle = TimeSpan.FromDays(sandbox.Value.IdleDays);
        foreach (var firmId in await FirmsAsync("f.idle_warned_at is null and coalesce(f.active_at, f.created_at) < $1", now - idle + Warning, cancellationToken))
        {
            await WarnAsync(firmId, now, cancellationToken);
        }

        foreach (var firmId in await FirmsAsync("f.idle_warned_at <= $1", now - Warning, cancellationToken))
        {
            await CloseAsync(firmId, now, cancellationToken);
        }
    }

    // Firms that are not live, whose sandbox is open, and that match the condition.
    private async Task<List<string>> FirmsAsync(string condition, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"select f.id from firms f where f.status <> 'Live' and f.configured = false and f.sandbox_closed_at is null and {condition} order by f.id");
        command.Parameters.AddWithValue(at);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var ids = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    private async Task WarnAsync(string firmId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (firms.ById(firmId) is not { } firm)
        {
            return;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (await ExecuteAsync(connection, "update firms set idle_warned_at = $2 where id = $1 and idle_warned_at is null", [firmId, now], cancellationToken) == 0)
        {
            return;
        }

        var login = new Uri(firm.Portal.Url, "admin/login");
        foreach (var admin in await FirmAdmins.EmailsAsync(connection, firmId, cancellationToken))
        {
            await EmailOutbox.AddAsync(
                connection, PlatformEmails.SandboxClosing(platform.Value.Name, firm.Name, admin, sandbox.Value.IdleDays, now + Warning, login), "sandbox_closing", firmId, now, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        signals.Emails.Set();
    }

    private async Task CloseAsync(string firmId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (firms.ById(firmId) is not { } firm)
        {
            return;
        }

        int ended;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await SlotService.LockAsync(connection, firmId, cancellationToken);

            // An administrator who came back since clears the warning, and then the sandbox stays open.
            if (await ExecuteAsync(
                connection,
                "update firms set sandbox_closed_at = $2 where id = $1 and status <> 'Live' and sandbox_closed_at is null and idle_warned_at <= $3",
                [firmId, now, now - Warning],
                cancellationToken) == 0)
            {
                return;
            }

            var accounts = await BillingStore.OpenAccountsToEndAsync(connection, firmId, cancellationToken);
            foreach (var account in accounts)
            {
                await challenges.ApplyAsync(
                    connection,
                    firm,
                    account.Id,
                    _ => new CancelChallenge(now, string.Create(CultureInfo.InvariantCulture, $"The sandbox was not used for {sandbox.Value.IdleDays} days, so its test accounts end.")),
                    null,
                    cancellationToken);
            }

            ended = accounts.Count;
            var login = new Uri(firm.Portal.Url, "admin/login");
            foreach (var admin in await FirmAdmins.EmailsAsync(connection, firmId, cancellationToken))
            {
                await EmailOutbox.AddAsync(
                    connection, PlatformEmails.SandboxClosed(platform.Value.Name, firm.Name, admin, sandbox.Value.IdleDays, ended, login), "sandbox_closed", firmId, now, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        // The next visit of an administrator opens the sandbox again, even within the hour.
        activity.Forget(firmId);
        challenges.Notify(firm);
        LogClosed(logger, firmId, ended);
    }

    private static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql, object[] parameters, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Closed the idle sandbox of firm {FirmId} and ended {Accounts} test accounts")]
    private static partial void LogClosed(ILogger logger, string firmId, int accounts);

    [LoggerMessage(Level = LogLevel.Error, Message = "Closing idle sandboxes failed; trying again later")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
