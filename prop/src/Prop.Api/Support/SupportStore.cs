using Common.Postgres;

using Npgsql;

using NpgsqlTypes;

namespace Prop.Api.Support;

/// <summary>Support tickets, their messages and attachments in the database. Changes that must happen together run in the caller's transaction.</summary>
internal sealed class SupportStore(NpgsqlDataSource dataSource, DatabaseSchema schema)
{
    private const string TicketColumns =
        """
        t.id, t.firm_id, t.number, t.trader_id, tr.email, tr.name, t.account_id, a.number, a.state -> 'definition' ->> 'name',
        t.subject, t.status, t.created_at, t.updated_at, t.waiting_since, t.answered_at, t.trader_read_at, t.closed_at, t.closed_by, t.messages
        """;

    private const string TicketJoins =
        """
        from support_tickets t
        join traders tr on tr.id = t.trader_id
        left join challenge_accounts a on a.id = t.account_id
        """;

    // Part of the trader's email or the subject, in capitals, or the ticket's number, in the parameters $n and $n+1.
    private static string SearchCondition(int n) =>
        $"(${n}::text is null or strpos(tr.normalized_email, ${n}) > 0 or strpos(upper(t.subject), ${n}) > 0 or t.number = ${n + 1}::bigint)";

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        return await dataSource.OpenConnectionAsync(cancellationToken);
    }

    /// <summary>The firm's ticket, also only when it is the trader's. Null when there is none.</summary>
    public async Task<SupportTicket?> GetAsync(string firmId, Guid ticketId, Guid? traderId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await GetAsync(connection, firmId, ticketId, traderId, forUpdate: false, cancellationToken);
    }

    /// <summary>The ticket locked until the caller's transaction ends. Null when the firm, or the trader, has no such ticket.</summary>
    public static Task<SupportTicket?> LockAsync(NpgsqlConnection connection, string firmId, Guid ticketId, Guid? traderId, CancellationToken cancellationToken) =>
        GetAsync(connection, firmId, ticketId, traderId, forUpdate: true, cancellationToken);

    /// <summary>
    /// A page of the firm's tickets, or only the trader's: those the search finds in the group. Open tickets come the
    /// longest waiting first, the others the latest written in first.
    /// </summary>
    public async Task<List<SupportTicketItem>> ListAsync(
        string firmId,
        Guid? traderId,
        SupportTicketGroup group,
        string? search,
        SupportCursor? after,
        int limit,
        CancellationToken cancellationToken)
    {
        var (order, cursor) = group == SupportTicketGroup.Open
            ? ("t.waiting_since, t.id", "(t.waiting_since, t.id) > ($5, $6::uuid)")
            : ("t.updated_at desc, t.id desc", "(t.updated_at, t.id) < ($5, $6::uuid)");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(
            connection,
            $"""
            select {TicketColumns}, last.author, last.body
            {TicketJoins}
            left join lateral (select m.author, m.body from support_messages m where m.ticket_id = t.id order by m.position desc limit 1) last on true
            where t.firm_id = $1 and ($2::uuid is null or t.trader_id = $2) and {SearchCondition(3)} and {GroupCondition(group)}
              and ($5::timestamptz is null or {cursor})
            order by {order}
            limit $7
            """,
            [
                firmId,
                Nullable(traderId, NpgsqlDbType.Uuid),
                .. SearchValues(search),
                Nullable(after?.Time, NpgsqlDbType.TimestampTz),
                Nullable(after?.Id, NpgsqlDbType.Uuid),
                limit,
            ]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<SupportTicketItem>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new SupportTicketItem(ReadTicket(reader), Enum.Parse<SupportAuthor>(reader.GetString(19)), reader.GetString(20)));
        }

        return items;
    }

    /// <summary>How many of the firm's tickets the search finds in each group.</summary>
    public async Task<SupportTicketCounts> CountAsync(string firmId, string? search, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(
            connection,
            $"""
            select count(*) filter (where t.status = 'Open'), count(*) filter (where t.status = 'Answered'), count(*) filter (where t.status = 'Closed'), count(*)
            from support_tickets t join traders tr on tr.id = t.trader_id
            where t.firm_id = $1 and {SearchCondition(2)}
            """,
            [firmId, .. SearchValues(search)]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new SupportTicketCounts((int)reader.GetInt64(0), (int)reader.GetInt64(1), (int)reader.GetInt64(2), (int)reader.GetInt64(3));
    }

    public async Task<TraderSupportSummary> TraderSummaryAsync(string firmId, Guid traderId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(
            connection,
            """
            select count(*) filter (where status <> 'Closed'), count(*) filter (where answered_at > coalesce(trader_read_at, '-infinity'))
            from support_tickets where firm_id = $1 and trader_id = $2
            """,
            [firmId, traderId]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new TraderSupportSummary((int)reader.GetInt64(0), (int)reader.GetInt64(1));
    }

    public async Task<FirmSupportSummary> FirmSummaryAsync(string firmId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(
            connection,
            """
            select count(*) filter (where status = 'Open'), min(waiting_since) filter (where status = 'Open'), count(*) filter (where status = 'Answered')
            from support_tickets where firm_id = $1
            """,
            [firmId]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new FirmSupportSummary((int)reader.GetInt64(0), NullableTime(reader, 1), (int)reader.GetInt64(2));
    }

    /// <summary>The ticket's messages, oldest first, each with its attachments in the order they were added.</summary>
    public async Task<List<SupportMessage>> MessagesAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var attachments = new List<(Guid MessageId, SupportAttachment Attachment)>();
        await using (var command = Command(
            connection,
            "select message_id, id, ticket_id, file_name, content_type, size from support_attachments where ticket_id = $1 order by message_id, position",
            [ticketId]))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                attachments.Add((reader.GetGuid(0), new SupportAttachment(reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4), reader.GetInt32(5))));
            }
        }

        var byMessage = attachments.ToLookup(a => a.MessageId, a => a.Attachment);
        await using var messages = Command(
            connection,
            "select id, position, author, admin_email, body, created_at from support_messages where ticket_id = $1 order by position",
            [ticketId]);
        await using var messageReader = await messages.ExecuteReaderAsync(cancellationToken);
        var list = new List<SupportMessage>();
        while (await messageReader.ReadAsync(cancellationToken))
        {
            var id = messageReader.GetGuid(0);
            list.Add(new SupportMessage(
                id,
                messageReader.GetInt32(1),
                Enum.Parse<SupportAuthor>(messageReader.GetString(2)),
                messageReader.IsDBNull(3) ? null : messageReader.GetString(3),
                messageReader.GetString(4),
                messageReader.GetFieldValue<DateTimeOffset>(5),
                [.. byMessage[id]]));
        }

        return list;
    }

    /// <summary>The firm's attachment with its encrypted content, also only when its ticket is the trader's. Null when there is none.</summary>
    public async Task<(SupportAttachment Attachment, byte[] ProtectedContent)?> GetAttachmentAsync(
        string firmId,
        Guid attachmentId,
        Guid? traderId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(
            connection,
            """
            select f.id, f.ticket_id, f.file_name, f.content_type, f.size, f.content
            from support_attachments f join support_tickets t on t.id = f.ticket_id
            where t.firm_id = $1 and f.id = $2 and ($3::uuid is null or t.trader_id = $3)
            """,
            [firmId, attachmentId, Nullable(traderId, NpgsqlDbType.Uuid)]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (new SupportAttachment(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4)), reader.GetFieldValue<byte[]>(5))
            : null;
    }

    /// <summary>Locks the trader until the caller's transaction ends, so two new tickets at once count each other.</summary>
    public static Task<int> LockTraderAsync(NpgsqlConnection connection, Guid traderId, CancellationToken cancellationToken) =>
        ExecuteAsync(connection, "select 1 from traders where id = $1 for update", [traderId], cancellationToken);

    /// <summary>The trader's tickets that are not closed.</summary>
    public static async Task<int> CountActiveAsync(NpgsqlConnection connection, string firmId, Guid traderId, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, "select count(*) from support_tickets where firm_id = $1 and trader_id = $2 and status <> 'Closed'", [firmId, traderId]);
        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>The firm's next ticket number, from 1.</summary>
    public static async Task<long> NextNumberAsync(NpgsqlConnection connection, string firmId, CancellationToken cancellationToken)
    {
        await using var command = Command(
            connection,
            """
            insert into support_ticket_counters (firm_id, next_number) values ($1, 2)
            on conflict (firm_id) do update set next_number = support_ticket_counters.next_number + 1
            returning next_number - 1
            """,
            [firmId]);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>A new ticket from the trader, waiting for the firm, without messages yet.</summary>
    public static Task<int> InsertTicketAsync(
        NpgsqlConnection connection,
        Guid ticketId,
        string firmId,
        long number,
        Guid traderId,
        Guid? accountId,
        string subject,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            """
            insert into support_tickets (id, firm_id, number, trader_id, account_id, subject, status, created_at, updated_at, waiting_since, trader_read_at)
            values ($1, $2, $3, $4, $5, $6, $7, $8, $8, $8, $8)
            """,
            [ticketId, firmId, number, traderId, Nullable(accountId, NpgsqlDbType.Uuid), subject, SupportTicketStatus.Open.ToString(), now],
            cancellationToken);

    /// <summary>Adds the message as the ticket's next, with its files encrypted by <paramref name="protect"/>. Returns the message's id.</summary>
    public static async Task<Guid> AddMessageAsync(
        NpgsqlConnection connection,
        SupportTicket ticket,
        SupportAuthor author,
        string? adminEmail,
        string body,
        IReadOnlyList<CheckedAttachment> files,
        Func<Guid, byte[], byte[]> protect,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var messageId = Guid.CreateVersion7(now);
        await ExecuteAsync(
            connection,
            "insert into support_messages (id, ticket_id, position, author, admin_email, body, created_at) values ($1, $2, $3, $4, $5, $6, $7)",
            [messageId, ticket.Id, ticket.Messages + 1, author.ToString(), Nullable(adminEmail, NpgsqlDbType.Text), body, now],
            cancellationToken);
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var attachmentId = Guid.CreateVersion7(now);
            await ExecuteAsync(
                connection,
                """
                insert into support_attachments (id, message_id, ticket_id, position, file_name, content_type, size, sha256, content, created_at)
                values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
                """,
                [attachmentId, messageId, ticket.Id, i, file.FileName, file.ContentType, file.Content.Length, System.Security.Cryptography.SHA256.HashData(file.Content), protect(attachmentId, file.Content), now],
                cancellationToken);
        }

        return messageId;
    }

    /// <summary>The trader wrote: the ticket waits for the firm, since now unless it already did. The trader has seen it.</summary>
    public static Task<int> TraderWroteAsync(NpgsqlConnection connection, Guid ticketId, DateTimeOffset now, CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            """
            update support_tickets
            set waiting_since = case when status = 'Open' then waiting_since else $2 end, status = 'Open', updated_at = $2, trader_read_at = $2,
                closed_at = null, closed_by = null, messages = messages + 1
            where id = $1
            """,
            [ticketId, now],
            cancellationToken);

    /// <summary>The firm answered: the ticket waits for the trader, or is closed with the answer.</summary>
    public static Task<int> FirmWroteAsync(NpgsqlConnection connection, Guid ticketId, string adminEmail, bool close, DateTimeOffset now, CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            """
            update support_tickets
            set status = $3, waiting_since = null, answered_at = $2, updated_at = $2, messages = messages + 1,
                closed_at = case when $4 then $2 end, closed_by = case when $4 then $5 end
            where id = $1
            """,
            [ticketId, now, (close ? SupportTicketStatus.Closed : SupportTicketStatus.Answered).ToString(), close, adminEmail],
            cancellationToken);

    /// <summary>Closes the ticket. <paramref name="closedBy"/> is <see cref="SupportRules.ClosedByTrader"/> or an administrator's email.</summary>
    public static Task<int> CloseAsync(NpgsqlConnection connection, Guid ticketId, string closedBy, DateTimeOffset now, CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            "update support_tickets set status = 'Closed', waiting_since = null, closed_at = $2, closed_by = $3, updated_at = $2 where id = $1",
            [ticketId, now, closedBy],
            cancellationToken);

    /// <summary>The trader has seen the ticket and its answers until now. Returns 0 when the firm, or the trader, has no such ticket.</summary>
    public async Task<int> MarkReadAsync(string firmId, Guid ticketId, Guid traderId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await ExecuteAsync(
            connection,
            "update support_tickets set trader_read_at = greatest(trader_read_at, $4) where firm_id = $1 and id = $2 and trader_id = $3",
            [firmId, ticketId, traderId, now],
            cancellationToken);
    }

    /// <summary>Which tickets are in the group, in a query on support_tickets t.</summary>
    private static string GroupCondition(SupportTicketGroup group) => group == SupportTicketGroup.All ? "true" : $"t.status = '{group}'";

    private static async Task<SupportTicket?> GetAsync(NpgsqlConnection connection, string firmId, Guid ticketId, Guid? traderId, bool forUpdate, CancellationToken cancellationToken)
    {
        await using var command = Command(
            connection,
            $"select {TicketColumns} {TicketJoins} where t.firm_id = $1 and t.id = $2 and ($3::uuid is null or t.trader_id = $3){(forUpdate ? " for update of t" : "")}",
            [firmId, ticketId, Nullable(traderId, NpgsqlDbType.Uuid)]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadTicket(reader) : null;
    }

    private static SupportTicket ReadTicket(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetGuid(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : new SupportTicketAccount(reader.GetGuid(6), reader.GetInt64(7), reader.IsDBNull(8) ? "" : reader.GetString(8)),
            reader.GetString(9),
            Enum.Parse<SupportTicketStatus>(reader.GetString(10)),
            reader.GetFieldValue<DateTimeOffset>(11),
            reader.GetFieldValue<DateTimeOffset>(12),
            NullableTime(reader, 13),
            NullableTime(reader, 14),
            NullableTime(reader, 15),
            NullableTime(reader, 16),
            reader.IsDBNull(17) ? null : reader.GetString(17),
            reader.GetInt32(18));

    // The search in capitals, as emails are kept, and as a ticket number, with or without #.
    private static object[] SearchValues(string? search)
    {
        var text = string.IsNullOrWhiteSpace(search) ? null : search.Trim().ToUpperInvariant();
        var number = text is not null && long.TryParse(text.TrimStart('#'), out var parsed) ? parsed : (long?)null;
        return [Nullable(text, NpgsqlDbType.Text), Nullable(number, NpgsqlDbType.Bigint)];
    }

    private static NpgsqlParameter Nullable(object? value, NpgsqlDbType type) => new() { Value = value ?? DBNull.Value, NpgsqlDbType = type };

    private static DateTimeOffset? NullableTime(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);

    private static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql, object[] parameters, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, sql, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NpgsqlCommand Command(NpgsqlConnection connection, string sql, object[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter as NpgsqlParameter ?? new NpgsqlParameter { Value = parameter });
        }

        return command;
    }
}

/// <summary>A file that passed the checks: its clean name, its content type known from its content, and the content.</summary>
internal sealed record CheckedAttachment(string FileName, string ContentType, byte[] Content);
