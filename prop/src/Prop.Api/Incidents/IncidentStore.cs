using Common.Postgres;

using Npgsql;

using NpgsqlTypes;

namespace Prop.Api.Incidents;

/// <summary>What went wrong (ADR 0053).</summary>
public enum IncidentKind
{
    /// <summary>No prices came from the price feed. Orders, closes and stop changes were refused meanwhile.</summary>
    PriceFeedOutage,

    /// <summary>The trading platform could not be reached.</summary>
    PlatformDown,

    /// <summary>Prices came, but late or seldom.</summary>
    SlowPrices,

    Other,
}

/// <summary>Where an incident is: written but not shown yet, shown and going on, or over.</summary>
public enum IncidentStatus
{
    Draft,
    Open,
    Resolved,
}

/// <summary>The parts of the service a status page shows.</summary>
public enum ServicePart
{
    Trading,
    Prices,
    Terminal,
    Portal,
}

/// <summary>
/// An outage of the trading platform (ADR 0053). <paramref name="Firms"/> are the firms it concerns, or null for every
/// firm. <paramref name="Detected"/> when the platform found it by itself. <paramref name="Updates"/> are what was said
/// as it went on, oldest first.
/// </summary>
internal sealed record Incident(
    Guid Id,
    IncidentKind Kind,
    string Title,
    string PublicText,
    string InternalNote,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    IncidentStatus Status,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<string>? Firms,
    bool Detected,
    string? CreatedBy,
    DateTimeOffset CreatedAt,
    IReadOnlyList<IncidentUpdate> Updates)
{
    public bool Concerns(string firmId) => Firms is null || Firms.Contains(firmId, StringComparer.Ordinal);

    /// <summary>The parts of the service the kind of incident affects, as the status page shows them.</summary>
    public IReadOnlyList<ServicePart> Parts => Kind switch
    {
        IncidentKind.PriceFeedOutage => [ServicePart.Trading, ServicePart.Prices],
        IncidentKind.PlatformDown => [ServicePart.Trading, ServicePart.Terminal],
        IncidentKind.SlowPrices => [ServicePart.Prices],
        _ => [ServicePart.Trading],
    };
}

/// <summary>Something said about an incident. <paramref name="By"/> is the staff member, or null for the platform.</summary>
internal sealed record IncidentUpdate(DateTimeOffset At, IncidentStatus Status, string Text, string? By);

/// <summary>A firm's own words about an incident, for its traders.</summary>
internal sealed record IncidentNote(string Text, DateTimeOffset UpdatedAt, string UpdatedBy);

/// <summary>What a firm did for an account after an incident.</summary>
public enum IncidentDecisionKind
{
    /// <summary>The stage a broken loss limit ended was opened again, on the same trading account.</summary>
    Reinstated,

    /// <summary>Money was put on the account, for example for a close that was refused.</summary>
    Credited,
}

internal sealed record IncidentDecision(
    Guid Id,
    Guid AccountId,
    IncidentDecisionKind Kind,
    decimal Amount,
    string Reason,
    string DecidedBy,
    DateTimeOffset DecidedAt);

/// <summary>Incidents, their updates, the firms' notes and decisions in the database. Changes that belong together run in the caller's transaction.</summary>
internal sealed class IncidentStore(NpgsqlDataSource dataSource, DatabaseSchema schema)
{
    private const string SelectIncident =
        "select id, kind, title, public_text, internal_note, started_at, ended_at, status, published_at, firms, detected, created_by, created_at from incidents";

    /// <summary>Incidents that began after the time, newest first. With <paramref name="publishedOnly"/>, only those shown.</summary>
    public async Task<IReadOnlyList<Incident>> ListAsync(DateTimeOffset since, bool publishedOnly, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await ReadAsync(
            connection,
            $"{SelectIncident} where dismissed_at is null and (started_at >= $1 or ended_at is null) and ($2 = false or published_at is not null) order by started_at desc",
            [since, publishedOnly],
            cancellationToken);
    }

    public async Task<Incident?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await FindAsync(connection, id, forUpdate: false, cancellationToken);
    }

    public static async Task<Incident?> FindAsync(NpgsqlConnection connection, Guid id, bool forUpdate, CancellationToken cancellationToken) =>
        (await ReadAsync(connection, $"{SelectIncident} where id = $1 and dismissed_at is null{(forUpdate ? " for update" : "")}", [id], cancellationToken)).SingleOrDefault();

    /// <summary>
    /// The incident the platform found and nobody has ended yet, of the kind, if there is one. A dismissed one counts, so
    /// the same gap is not found again.
    /// </summary>
    public static async Task<Incident?> FindOngoingDetectedAsync(NpgsqlConnection connection, IncidentKind kind, CancellationToken cancellationToken) =>
        (await ReadAsync(
            connection,
            $"{SelectIncident} where detected and kind = $1 and ended_at is null and status <> 'Resolved' order by started_at desc limit 1 for update",
            [kind.ToString()],
            cancellationToken)).SingleOrDefault();

    /// <summary>The published incidents that still go on, the latest started first.</summary>
    public static Task<IReadOnlyList<Incident>> ListOpenAsync(NpgsqlConnection connection, CancellationToken cancellationToken) =>
        ReadAsync(
            connection,
            $"{SelectIncident} where dismissed_at is null and published_at is not null and status = 'Open' order by started_at desc, id desc",
            [],
            cancellationToken);

    /// <summary>How many drafts wait for our staff to publish or dismiss them.</summary>
    public async Task<int> CountDraftsAsync(CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("select count(*)::int from incidents where published_at is null and dismissed_at is null");
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>The firm's notes on the incidents, by incident, in the caller's transaction.</summary>
    public static async Task<IReadOnlyDictionary<Guid, string>> NotesAsync(NpgsqlConnection connection, string firmId, Guid[] incidentIds, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("select incident_id, text from incident_firm_notes where firm_id = $1 and incident_id = any($2)", connection);
        command.Parameters.AddWithValue(firmId);
        command.Parameters.AddWithValue(incidentIds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var notes = new Dictionary<Guid, string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            notes[reader.GetGuid(0)] = reader.GetString(1);
        }

        return notes;
    }

    public static async Task InsertAsync(NpgsqlConnection connection, Incident incident, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into incidents (id, kind, title, public_text, internal_note, started_at, ended_at, status, published_at, firms, detected, created_by, created_at, updated_at)
            values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $13)
            """,
            connection);
        AddFields(command, incident);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task UpdateAsync(NpgsqlConnection connection, Incident incident, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            update incidents set kind = $2, title = $3, public_text = $4, internal_note = $5, started_at = $6, ended_at = $7, status = $8,
                published_at = $9, firms = $10, detected = $11, created_by = $12, created_at = $13, updated_at = $14
            where id = $1
            """,
            connection);
        AddFields(command, incident);
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Hides an incident that was never shown, such as a false alarm. False when there is no such draft.</summary>
    public static async Task<bool> DismissAsync(NpgsqlConnection connection, Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "update incidents set dismissed_at = $2, updated_at = $2 where id = $1 and published_at is null and dismissed_at is null",
            connection);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(now);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public static async Task AddUpdateAsync(NpgsqlConnection connection, Guid incidentId, IncidentUpdate update, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "insert into incident_updates (incident_id, posted_at, status, text, posted_by) values ($1, $2, $3, $4, $5)",
            connection);
        command.Parameters.AddWithValue(incidentId);
        command.Parameters.AddWithValue(update.At);
        command.Parameters.AddWithValue(update.Status.ToString());
        command.Parameters.AddWithValue(update.Text);
        command.Parameters.AddWithValue((object?)update.By ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IncidentNote?> NoteAsync(Guid incidentId, string firmId, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("select text, updated_at, updated_by from incident_firm_notes where incident_id = $1 and firm_id = $2");
        command.Parameters.AddWithValue(incidentId);
        command.Parameters.AddWithValue(firmId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new IncidentNote(reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1), reader.GetString(2)) : null;
    }

    /// <summary>The firm's notes on the incidents, by incident.</summary>
    public async Task<IReadOnlyDictionary<Guid, IncidentNote>> NotesAsync(string firmId, IReadOnlyCollection<Guid> incidentIds, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("select incident_id, text, updated_at, updated_by from incident_firm_notes where firm_id = $1 and incident_id = any($2)");
        command.Parameters.AddWithValue(firmId);
        command.Parameters.AddWithValue(incidentIds.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var notes = new Dictionary<Guid, IncidentNote>();
        while (await reader.ReadAsync(cancellationToken))
        {
            notes[reader.GetGuid(0)] = new IncidentNote(reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetString(3));
        }

        return notes;
    }

    public static async Task SetNoteAsync(NpgsqlConnection connection, Guid incidentId, string firmId, IncidentNote? note, CancellationToken cancellationToken)
    {
        await using var command = note is null
            ? new NpgsqlCommand("delete from incident_firm_notes where incident_id = $1 and firm_id = $2", connection)
            : new NpgsqlCommand(
                """
                insert into incident_firm_notes (incident_id, firm_id, text, updated_at, updated_by) values ($1, $2, $3, $4, $5)
                on conflict (incident_id, firm_id) do update set text = excluded.text, updated_at = excluded.updated_at, updated_by = excluded.updated_by
                """,
                connection);
        command.Parameters.AddWithValue(incidentId);
        command.Parameters.AddWithValue(firmId);
        if (note is not null)
        {
            command.Parameters.AddWithValue(note.Text);
            command.Parameters.AddWithValue(note.UpdatedAt);
            command.Parameters.AddWithValue(note.UpdatedBy);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IncidentDecision>> DecisionsAsync(Guid incidentId, string firmId, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            """
            select id, challenge_account_id, kind, amount, reason, decided_by, decided_at from incident_decisions
            where incident_id = $1 and firm_id = $2 order by decided_at
            """);
        command.Parameters.AddWithValue(incidentId);
        command.Parameters.AddWithValue(firmId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var decisions = new List<IncidentDecision>();
        while (await reader.ReadAsync(cancellationToken))
        {
            decisions.Add(new IncidentDecision(
                reader.GetGuid(0),
                reader.GetGuid(1),
                Enum.Parse<IncidentDecisionKind>(reader.GetString(2)),
                reader.GetDecimal(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetFieldValue<DateTimeOffset>(6)));
        }

        return decisions;
    }

    public static async Task AddDecisionAsync(NpgsqlConnection connection, Guid incidentId, string firmId, IncidentDecision decision, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into incident_decisions (id, incident_id, firm_id, challenge_account_id, kind, amount, reason, decided_by, decided_at)
            values ($1, $2, $3, $4, $5, $6, $7, $8, $9)
            """,
            connection);
        command.Parameters.AddWithValue(decision.Id);
        command.Parameters.AddWithValue(incidentId);
        command.Parameters.AddWithValue(firmId);
        command.Parameters.AddWithValue(decision.AccountId);
        command.Parameters.AddWithValue(decision.Kind.ToString());
        command.Parameters.AddWithValue(decision.Amount);
        command.Parameters.AddWithValue(decision.Reason);
        command.Parameters.AddWithValue(decision.DecidedBy);
        command.Parameters.AddWithValue(decision.DecidedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddFields(NpgsqlCommand command, Incident incident)
    {
        command.Parameters.AddWithValue(incident.Id);
        command.Parameters.AddWithValue(incident.Kind.ToString());
        command.Parameters.AddWithValue(incident.Title);
        command.Parameters.AddWithValue(incident.PublicText);
        command.Parameters.AddWithValue(incident.InternalNote);
        command.Parameters.AddWithValue(incident.StartedAt);
        command.Parameters.AddWithValue((object?)incident.EndedAt ?? DBNull.Value);
        command.Parameters.AddWithValue(incident.Status.ToString());
        command.Parameters.AddWithValue((object?)incident.PublishedAt ?? DBNull.Value);
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)incident.Firms?.ToArray() ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text });
        command.Parameters.AddWithValue(incident.Detected);
        command.Parameters.AddWithValue((object?)incident.CreatedBy ?? DBNull.Value);
        command.Parameters.AddWithValue(incident.CreatedAt);
    }

    private static async Task<IReadOnlyList<Incident>> ReadAsync(NpgsqlConnection connection, string sql, object[] parameters, CancellationToken cancellationToken)
    {
        var incidents = new List<Incident>();
        await using (var command = new NpgsqlCommand(sql, connection))
        {
            foreach (var parameter in parameters)
            {
                command.Parameters.AddWithValue(parameter);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                incidents.Add(new Incident(
                    reader.GetGuid(0),
                    Enum.Parse<IncidentKind>(reader.GetString(1)),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetFieldValue<DateTimeOffset>(5),
                    reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                    Enum.Parse<IncidentStatus>(reader.GetString(7)),
                    reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                    reader.IsDBNull(9) ? null : reader.GetFieldValue<string[]>(9),
                    reader.GetBoolean(10),
                    reader.IsDBNull(11) ? null : reader.GetString(11),
                    reader.GetFieldValue<DateTimeOffset>(12),
                    []));
            }
        }

        if (incidents.Count == 0)
        {
            return incidents;
        }

        // The updates of every incident read, in one go.
        await using var updates = new NpgsqlCommand(
            "select incident_id, posted_at, status, text, posted_by from incident_updates where incident_id = any($1) order by id",
            connection);
        updates.Parameters.AddWithValue(incidents.Select(i => i.Id).ToArray());
        var byIncident = new Dictionary<Guid, List<IncidentUpdate>>();
        await using (var reader = await updates.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetGuid(0);
                if (!byIncident.TryGetValue(id, out var list))
                {
                    byIncident[id] = list = [];
                }

                list.Add(new IncidentUpdate(reader.GetFieldValue<DateTimeOffset>(1), Enum.Parse<IncidentStatus>(reader.GetString(2)), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
            }
        }

        return [.. incidents.Select(i => byIncident.TryGetValue(i.Id, out var list) ? i with { Updates = list } : i)];
    }
}
