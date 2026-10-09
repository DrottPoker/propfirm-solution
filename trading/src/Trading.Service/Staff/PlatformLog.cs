using System.Text.Json;

using Common.Postgres;

using Npgsql;

using NpgsqlTypes;

namespace Trading.Service.Staff;

/// <summary>What can happen on the platform that our staff see in the staff panel's log (ADR 0057).</summary>
public enum PlatformEventKind
{
    /// <summary>The service started. Detail: <c>replayed</c> inputs, <c>milliseconds</c> it took.</summary>
    ServiceStarted,

    /// <summary>A server was made. Detail: <c>name</c>, <c>currency</c>, <c>kind</c> when our staff made it, and <c>partner</c> when a partner did.</summary>
    ServerCreated,

    /// <summary>A server was put on the list traders choose from. Detail: <c>partner</c> when a partner did it.</summary>
    ServerListed,

    /// <summary>A server was taken off the list. Detail: <c>partner</c> when a partner did it.</summary>
    ServerUnlisted,

    /// <summary>A server's admin key was replaced. Detail: <c>partner</c> when a partner asked, or the staff's <c>reason</c>.</summary>
    AdminKeyReplaced,

    /// <summary>A symbol got no prices while its market was open. Detail: <c>symbol</c>, <c>feed</c>, <c>lastPrice</c>.</summary>
    SymbolSilent,

    /// <summary>A silent symbol got prices again. Detail: <c>symbol</c>, <c>feed</c>, <c>silentSeconds</c>.</summary>
    SymbolBack,

    /// <summary>The whole feed gave no prices while a market was open. Detail: <c>feed</c>, <c>lastPrice</c>.</summary>
    FeedSilent,

    /// <summary>The feed gave prices again. Detail: <c>feed</c>, <c>silentSeconds</c>.</summary>
    FeedBack,

    /// <summary>A gap in the charts was filled. Detail: <c>from</c>, <c>until</c>, <c>bars</c>.</summary>
    ChartGapFilled,

    /// <summary>A gap in the charts could not be filled. Detail: <c>from</c>, <c>until</c>, <c>problem</c>.</summary>
    ChartGapNotFilled,

    /// <summary>The charts' history was loaded again. Detail: <c>bars</c>, or <c>problem</c> when it failed.</summary>
    ChartHistoryReloaded,

    /// <summary>A server's kind of business changed (ADR 0058). Detail: <c>kind</c> and the kind it was, <c>from</c>.</summary>
    TerminalKindChanged,
}

/// <summary>
/// Something that happened on the platform. <paramref name="ServerId"/> is the server it concerns, if any, and
/// <paramref name="StaffEmail"/> the staff member who did it, if any. <paramref name="Detail"/> depends on the kind.
/// </summary>
public sealed record PlatformLogEntry(
    long Id,
    DateTimeOffset At,
    PlatformEventKind Kind,
    string? ServerId,
    string? StaffEmail,
    IReadOnlyDictionary<string, string> Detail);

/// <summary>The platform's log. Only added to.</summary>
public interface IPlatformLog
{
    Task AddAsync(DateTimeOffset at, PlatformEventKind kind, string? serverId, string? staffEmail, IReadOnlyDictionary<string, string> detail, CancellationToken cancellationToken);

    /// <summary>The latest entries, newest first, of the whole platform or of one server.</summary>
    Task<IReadOnlyList<PlatformLogEntry>> LatestAsync(string? serverId, int limit, CancellationToken cancellationToken);
}

/// <summary>
/// Writes to the platform's log without letting a failure stop what was logged: the log is for our staff, while the
/// change it describes has already happened.
/// </summary>
internal sealed partial class PlatformEvents(IPlatformLog log, TimeProvider time, ILogger<PlatformEvents> logger)
{
    public async Task RecordAsync(PlatformEventKind kind, string? serverId, string? staffEmail, Dictionary<string, string> detail)
    {
        try
        {
            await log.AddAsync(time.GetUtcNow(), kind, serverId, staffEmail, detail, CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogNotRecorded(logger, kind, exception);
        }
    }

    public Task<IReadOnlyList<PlatformLogEntry>> LatestAsync(string? serverId, int limit, CancellationToken cancellationToken) =>
        log.LatestAsync(serverId, limit, cancellationToken);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not add {Kind} to the platform's log")]
    private static partial void LogNotRecorded(ILogger logger, PlatformEventKind kind, Exception exception);
}

internal sealed class PostgresPlatformLog(NpgsqlDataSource dataSource, DatabaseSchema schema) : IPlatformLog
{
    public async Task AddAsync(
        DateTimeOffset at,
        PlatformEventKind kind,
        string? serverId,
        string? staffEmail,
        IReadOnlyDictionary<string, string> detail,
        CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            "insert into platform_log (at, kind, server_id, staff_email, detail) values ($1, $2, $3, $4, $5)");
        command.Parameters.AddWithValue(at);
        command.Parameters.AddWithValue(kind.ToString());
        command.Parameters.AddWithValue((object?)serverId ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)staffEmail ?? DBNull.Value);
        command.Parameters.Add(new NpgsqlParameter { Value = JsonSerializer.Serialize(detail), NpgsqlDbType = NpgsqlDbType.Jsonb });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlatformLogEntry>> LatestAsync(string? serverId, int limit, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            serverId is null
                ? "select id, at, kind, server_id, staff_email, detail from platform_log order by at desc, id desc limit $1"
                : "select id, at, kind, server_id, staff_email, detail from platform_log where server_id = $2 order by at desc, id desc limit $1");
        command.Parameters.AddWithValue(limit);
        if (serverId is not null)
        {
            command.Parameters.AddWithValue(serverId);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<PlatformLogEntry>();
        while (await reader.ReadAsync(cancellationToken))
        {
            // A kind from a later version is skipped rather than failing the whole log.
            if (!Enum.TryParse<PlatformEventKind>(reader.GetString(2), out var kind))
            {
                continue;
            }

            entries.Add(new PlatformLogEntry(
                reader.GetInt64(0),
                reader.GetFieldValue<DateTimeOffset>(1),
                kind,
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(5)) ?? []));
        }

        return entries;
    }
}
