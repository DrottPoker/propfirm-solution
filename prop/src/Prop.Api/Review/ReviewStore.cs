using System.Text.Json;

using Common.Postgres;

using Npgsql;

using NpgsqlTypes;

using Prop.Api.Json;

namespace Prop.Api.Review;

/// <summary>Where a firm is in our review before it may go live (ADR 0021).</summary>
public enum ReviewStatus
{
    /// <summary>The firm fills in its application, also while its deposit is not paid yet.</summary>
    Draft,

    /// <summary>Sent, and waiting for us.</summary>
    Submitted,

    /// <summary>We need changes. The firm changes its application and sends it again, without a new deposit.</summary>
    ChangesRequested,

    /// <summary>The firm may go live by paying.</summary>
    Approved,

    /// <summary>The firm may not go live. Final.</summary>
    Rejected,
}

/// <summary>
/// Our review of a firm. <paramref name="Message"/> is our latest word to the firm: the changes we need, or why it
/// was not approved. <paramref name="DecidedBy"/> is the staff member who decided last.
/// </summary>
internal sealed record FirmReview(
    string FirmId,
    ReviewStatus Status,
    FirmApplication Application,
    string? Message,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? DecidedAt,
    string? DecidedBy)
{
    /// <summary>The firm can change its application, and add or remove documents.</summary>
    public bool CanEdit => Status is ReviewStatus.Draft or ReviewStatus.ChangesRequested;
}

/// <summary>A document the firm added to its application. Its content is kept encrypted.</summary>
internal sealed record FirmDocument(Guid Id, string FirmId, string FileName, string ContentType, int Size, string UploadedBy, DateTimeOffset UploadedAt);

/// <summary>Something that happened in a firm's review or with its suspension, and who did it.</summary>
internal sealed record FirmEvent(long Id, string Type, DateTimeOffset RecordedAt, string Actor, string? Detail);

/// <summary>One of our checks during a review, ticked by a staff member.</summary>
internal sealed record ReviewCheck(string Item, string DoneBy, DateTimeOffset DoneAt);

/// <summary>The checks our staff make while they review a firm (ADR 0024), in the order they are shown.</summary>
internal static class ReviewChecks
{
    /// <summary>
    /// The VAT number in VIES, the company in its business register, the owners against the register, the terms on how
    /// traders are paid, and the website and links.
    /// </summary>
    public static readonly IReadOnlyList<string> All = ["vat", "register", "owners", "terms", "website"];
}

/// <summary>Who did something, when it was not a person.</summary>
internal static class ReviewActors
{
    public const string Platform = "platform";
}

/// <summary>Reviews, documents and events in the database. Changes that must happen together run in the caller's transaction.</summary>
internal sealed class ReviewStore(NpgsqlDataSource dataSource, DatabaseSchema schema)
{
    private const string SelectReview =
        "select firm_id, status, application, message, submitted_at, decided_at, decided_by from firm_reviews";

    private const string SelectDocument =
        "select id, firm_id, file_name, content_type, size, uploaded_by, uploaded_at from firm_documents";

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        return await dataSource.OpenConnectionAsync(cancellationToken);
    }

    /// <summary>The firm's review, or null when it has none yet.</summary>
    public static async Task<FirmReview?> GetAsync(NpgsqlConnection connection, string firmId, bool forUpdate, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, $"{SelectReview} where firm_id = $1{(forUpdate ? " for update" : "")}", [firmId]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new FirmReview(
                reader.GetString(0),
                Enum.Parse<ReviewStatus>(reader.GetString(1)),
                JsonSerializer.Deserialize<FirmApplication>(reader.GetString(2), PropJson.Options) ?? FirmApplication.Empty,
                reader.IsDBNull(3) ? null : reader.GetString(3),
                NullableTime(reader, 4),
                NullableTime(reader, 5),
                reader.IsDBNull(6) ? null : reader.GetString(6))
            : null;
    }

    /// <summary>The firm's review, locked until the caller's transaction ends. Made as an empty draft when the firm has none.</summary>
    public static async Task<FirmReview> LockAsync(NpgsqlConnection connection, string firmId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            "insert into firm_reviews (firm_id, status, application, created_at, updated_at) values ($1, $2, $3, $4, $4) on conflict (firm_id) do nothing",
            [firmId, ReviewStatus.Draft.ToString(), Jsonb(FirmApplication.Empty), now],
            cancellationToken);
        return (await GetAsync(connection, firmId, forUpdate: true, cancellationToken))!;
    }

    public static Task<int> SaveApplicationAsync(NpgsqlConnection connection, string firmId, FirmApplication application, DateTimeOffset now, CancellationToken cancellationToken) =>
        ExecuteAsync(connection, "update firm_reviews set application = $2, updated_at = $3 where firm_id = $1", [firmId, Jsonb(application), now], cancellationToken);

    /// <summary>The application is sent and waits for us. Our earlier message no longer applies.</summary>
    public static Task<int> SubmitAsync(NpgsqlConnection connection, string firmId, DateTimeOffset now, CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            "update firm_reviews set status = $2, message = null, submitted_at = $3, updated_at = $3 where firm_id = $1",
            [firmId, ReviewStatus.Submitted.ToString(), now],
            cancellationToken);

    public static Task<int> DecideAsync(
        NpgsqlConnection connection,
        string firmId,
        ReviewStatus status,
        string? message,
        string staffEmail,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            "update firm_reviews set status = $2, message = $3, decided_at = $4, decided_by = $5, updated_at = $4 where firm_id = $1",
            [firmId, status.ToString(), Text(message), now, staffEmail],
            cancellationToken);

    /// <summary>
    /// The firm's deposit is paid: a draft that is complete is sent. Done in the caller's transaction. True when it
    /// was sent, so our staff are told.
    /// </summary>
    public static async Task<bool> SubmitPaidDraftAsync(NpgsqlConnection connection, string firmId, long depositCharge, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var review = await LockAsync(connection, firmId, now, cancellationToken);
        if (review.Status != ReviewStatus.Draft || ApplicationRules.Problem(review.Application, complete: true) is not null)
        {
            return false;
        }

        await SubmitAsync(connection, firmId, now, cancellationToken);
        await AddEventAsync(connection, firmId, "submitted", ReviewActors.Platform, Snapshot(review.Application, depositCharge), now, cancellationToken);
        return true;
    }

    /// <summary>The application as it was sent, and the deposit's charge when paying it sent it, for the firm's events.</summary>
    public static string Snapshot(FirmApplication application, long? depositCharge = null) =>
        JsonSerializer.Serialize(new { application, depositCharge }, PropJson.Options);

    public static Task<int> AddEventAsync(NpgsqlConnection connection, string firmId, string type, string actor, string? detail, DateTimeOffset now, CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            "insert into firm_events (firm_id, type, recorded_at, actor, detail) values ($1, $2, $3, $4, $5)",
            [firmId, type, now, actor, detail is null ? new NpgsqlParameter { Value = DBNull.Value, NpgsqlDbType = NpgsqlDbType.Jsonb } : JsonbText(detail)],
            cancellationToken);

    /// <summary>The firm's newest events first.</summary>
    public async Task<List<FirmEvent>> ListEventsAsync(string firmId, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(
            connection,
            "select id, type, recorded_at, actor, detail from firm_events where firm_id = $1 order by id desc limit $2",
            [firmId, limit]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var events = new List<FirmEvent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new FirmEvent(reader.GetInt64(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return events;
    }

    public static Task<int> InsertDocumentAsync(NpgsqlConnection connection, FirmDocument document, byte[] sha256, byte[] protectedContent, CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            """
            insert into firm_documents (id, firm_id, file_name, content_type, size, sha256, content, uploaded_by, uploaded_at)
            values ($1, $2, $3, $4, $5, $6, $7, $8, $9)
            """,
            [document.Id, document.FirmId, document.FileName, document.ContentType, document.Size, sha256, protectedContent, document.UploadedBy, document.UploadedAt],
            cancellationToken);

    public static async Task<List<FirmDocument>> ListDocumentsAsync(NpgsqlConnection connection, string firmId, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, $"{SelectDocument} where firm_id = $1 order by uploaded_at, id", [firmId]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var documents = new List<FirmDocument>();
        while (await reader.ReadAsync(cancellationToken))
        {
            documents.Add(ReadDocument(reader));
        }

        return documents;
    }

    public async Task<List<FirmDocument>> ListDocumentsAsync(string firmId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await ListDocumentsAsync(connection, firmId, cancellationToken);
    }

    /// <summary>The firm's document with its encrypted content, or null when the firm has no such document.</summary>
    public async Task<(FirmDocument Document, byte[] ProtectedContent)?> GetDocumentAsync(string firmId, Guid documentId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(
            connection,
            "select id, firm_id, file_name, content_type, size, uploaded_by, uploaded_at, content from firm_documents where firm_id = $1 and id = $2",
            [firmId, documentId]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? (ReadDocument(reader), reader.GetFieldValue<byte[]>(7)) : null;
    }

    /// <summary>Removes the firm's document and returns it. Null when the firm has no such document.</summary>
    public static async Task<FirmDocument?> DeleteDocumentAsync(NpgsqlConnection connection, string firmId, Guid documentId, CancellationToken cancellationToken)
    {
        await using var command = Command(
            connection,
            "delete from firm_documents where firm_id = $1 and id = $2 returning id, firm_id, file_name, content_type, size, uploaded_by, uploaded_at",
            [firmId, documentId]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadDocument(reader) : null;
    }

    /// <summary>The checks our staff have ticked in the firm's review.</summary>
    public static async Task<List<ReviewCheck>> ListChecksAsync(NpgsqlConnection connection, string firmId, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, "select item, done_by, done_at from firm_review_checks where firm_id = $1", [firmId]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var checks = new List<ReviewCheck>();
        while (await reader.ReadAsync(cancellationToken))
        {
            checks.Add(new ReviewCheck(reader.GetString(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2)));
        }

        return checks;
    }

    public async Task<List<ReviewCheck>> ListChecksAsync(string firmId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await ListChecksAsync(connection, firmId, cancellationToken);
    }

    /// <summary>Ticks the check for the staff member, keeping who ticked it first, or unticks it when <paramref name="staffEmail"/> is null.</summary>
    public static Task<int> SetCheckAsync(NpgsqlConnection connection, string firmId, string item, string? staffEmail, DateTimeOffset now, CancellationToken cancellationToken) =>
        staffEmail is null
            ? ExecuteAsync(connection, "delete from firm_review_checks where firm_id = $1 and item = $2", [firmId, item], cancellationToken)
            : ExecuteAsync(
                connection,
                "insert into firm_review_checks (firm_id, item, done_by, done_at) values ($1, $2, $3, $4) on conflict (firm_id, item) do nothing",
                [firmId, item, staffEmail, now],
                cancellationToken);

    /// <summary>When the firm signed up and whether it was configured.</summary>
    public async Task<(DateTimeOffset CreatedAt, bool Configured)> FirmInfoAsync(string firmId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(connection, "select created_at, configured from firms where id = $1", [firmId]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return (reader.GetFieldValue<DateTimeOffset>(0), reader.GetBoolean(1));
    }

    private static FirmDocument ReadDocument(NpgsqlDataReader reader) =>
        new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6));

    private static NpgsqlParameter Jsonb(FirmApplication application) => JsonbText(JsonSerializer.Serialize(application, PropJson.Options));

    private static NpgsqlParameter JsonbText(string json) => new() { Value = json, NpgsqlDbType = NpgsqlDbType.Jsonb };

    private static NpgsqlParameter Text(string? value) => new() { Value = (object?)value ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Text };

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
