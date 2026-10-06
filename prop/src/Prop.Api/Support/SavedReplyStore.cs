using System.Globalization;

using Common.Postgres;

using Npgsql;

namespace Prop.Api.Support;

/// <summary>
/// The firm's saved replies (ADR 0041): answers its administrators save once and start an answer in a ticket from. Every
/// query is limited to one firm, so a firm never reaches another's.
/// </summary>
internal sealed class SavedReplyStore(NpgsqlDataSource dataSource, DatabaseSchema schema)
{
    private const string SelectReply = "select id, firm_id, title, body, created_at, updated_at from support_saved_replies";

    /// <summary>The firm's saved replies by title, in any case.</summary>
    public async Task<List<SavedReply>> ListAsync(string firmId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(connection, $"{SelectReply} where firm_id = $1 order by lower(title), id", [firmId]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var replies = new List<SavedReply>();
        while (await reader.ReadAsync(cancellationToken))
        {
            replies.Add(new SavedReply(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4), reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return replies;
    }

    /// <summary>Saves a new reply, unless the firm has as many as it can have or one with the same title.</summary>
    public async Task<SavedReplyOutcome> AddAsync(SavedReply reply, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Two replies saved at once count each other.
        await using (var locking = Command(connection, "select pg_advisory_xact_lock(hashtextextended('saved_replies:' || $1, 0))", [reply.FirmId]))
        {
            await locking.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var counting = Command(connection, "select count(*) from support_saved_replies where firm_id = $1", [reply.FirmId]))
        {
            if ((long)(await counting.ExecuteScalarAsync(cancellationToken))! >= SupportRules.MaxSavedReplies)
            {
                return SavedReplyOutcome.TooMany;
            }
        }

        await using var command = Command(
            connection,
            "insert into support_saved_replies (id, firm_id, title, body, created_at, updated_at) values ($1, $2, $3, $4, $5, $6)",
            [reply.Id, reply.FirmId, reply.Title, reply.Body, reply.CreatedAt, reply.UpdatedAt]);
        if (!await TitleIsFreeAsync(command, cancellationToken))
        {
            return SavedReplyOutcome.TitleTaken;
        }

        await transaction.CommitAsync(cancellationToken);
        return SavedReplyOutcome.Saved;
    }

    /// <summary>Changes the firm's reply. The reply keeps its id, so a list open elsewhere finds it again.</summary>
    public async Task<SavedReplyOutcome> ChangeAsync(string firmId, Guid replyId, string title, string body, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(
            connection,
            "update support_saved_replies set title = $3, body = $4, updated_at = $5 where firm_id = $1 and id = $2",
            [firmId, replyId, title, body, now]);
        try
        {
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1 ? SavedReplyOutcome.Saved : SavedReplyOutcome.Unknown;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return SavedReplyOutcome.TitleTaken;
        }
    }

    /// <summary>Removes the firm's reply. False when the firm has no such reply.</summary>
    public async Task<bool> RemoveAsync(string firmId, Guid replyId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(connection, "delete from support_saved_replies where firm_id = $1 and id = $2", [firmId, replyId]);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    // Runs the insert, and tells whether the title was free. A taken title leaves the transaction to be rolled back.
    private static async Task<bool> TitleIsFreeAsync(NpgsqlCommand insert, CancellationToken cancellationToken)
    {
        try
        {
            await insert.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        return await dataSource.OpenConnectionAsync(cancellationToken);
    }

    private static NpgsqlCommand Command(NpgsqlConnection connection, string sql, object[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = parameter });
        }

        return command;
    }
}

/// <summary>What a saved reply needs before it is saved, the same for every firm.</summary>
internal static class SavedReplyRules
{
    /// <summary>The title on one line as a ticket's subject, and the text with its line breaks kept as a message, both without spaces around them.</summary>
    public static (string Title, string Body) Clean(SavedReplyRequest request) => (SupportService.CleanSubject(request.Title), SupportService.CleanMessage(request.Body));

    /// <summary>Why the reply, already cleaned, cannot be saved. Null when it can.</summary>
    public static SupportResult.Refused? Refusal(string title, string body)
    {
        if (title.Length == 0)
        {
            return new SupportResult.Refused(StatusCodes.Status422UnprocessableEntity, "Write a title to find the reply by.", "title");
        }

        if (title.Length > SupportRules.MaxSavedReplyTitleLength)
        {
            return new SupportResult.Refused(StatusCodes.Status422UnprocessableEntity, $"Keep the title to {SupportRules.MaxSavedReplyTitleLength} characters.", "title");
        }

        if (body.Length == 0)
        {
            return new SupportResult.Refused(StatusCodes.Status422UnprocessableEntity, "Write the reply.", "body");
        }

        return body.Length > SupportRules.MaxSavedReplyLength
            ? new SupportResult.Refused(
                StatusCodes.Status422UnprocessableEntity,
                $"Keep the reply to {SupportRules.MaxSavedReplyLength.ToString("N0", CultureInfo.InvariantCulture)} characters.",
                "body")
            : null;
    }
}
