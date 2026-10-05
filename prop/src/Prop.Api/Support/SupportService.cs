using System.Globalization;

using Npgsql;

using Prop.Api.Challenges;
using Prop.Api.Email;
using Prop.Api.Files;
using Prop.Api.Firms;
using Prop.Api.Portal;

namespace Prop.Api.Support;

/// <summary>
/// Support tickets between a firm's traders and the firm (ADR 0041). A trader opens a ticket about anything, or about one
/// of their accounts, or the firm writes to a trader first, and the trader and the firm's administrators write in it until
/// one of them closes it. The firm's administrators are emailed when a ticket starts to wait for them, and the trader when
/// the firm writes.
/// </summary>
internal sealed class SupportService(
    SupportStore store,
    ChallengeQueries accounts,
    PortalUsers users,
    Notifications notifications,
    SecretProtector secrets,
    WorkSignals signals,
    TimeProvider time)
{
    /// <summary>The trader opens a ticket with its first message, optionally about one of the trader's accounts.</summary>
    public async Task<SupportResult> OpenAsync(
        Firm firm,
        Guid traderId,
        string? subject,
        string? body,
        Guid? accountId,
        IReadOnlyList<UploadedAttachment> files,
        CancellationToken cancellationToken)
    {
        var cleanSubject = CleanSubject(subject);
        if ((SubjectRefusal(cleanSubject, "Write what your question is about.") ?? Refusal(body, files)) is { } refused)
        {
            return refused;
        }

        var (message, attachments) = Prepare(body, files);
        if (!await IsTradersAccountAsync(firm, traderId, accountId, cancellationToken))
        {
            return new SupportResult.Refused(StatusCodes.Status422UnprocessableEntity, "Choose one of your accounts, or none.", "accountId");
        }

        var now = time.GetUtcNow();
        var ticketId = Guid.CreateVersion7(now);
        await using var connection = await store.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SupportStore.LockTraderAsync(connection, traderId, cancellationToken);
        if (await SupportStore.CountActiveAsync(connection, firm.Id, traderId, cancellationToken) >= SupportRules.MaxOpenTicketsPerTrader)
        {
            return new SupportResult.Refused(
                StatusCodes.Status409Conflict,
                $"You have {SupportRules.MaxOpenTicketsPerTrader} tickets that are not closed. Write in one of them, or close one, before you open another.");
        }

        if (await LimitRefusalAsync(connection, firm.Id, traderId, null, attachments, now, cancellationToken) is { } limited)
        {
            return limited;
        }

        var number = await SupportStore.NextNumberAsync(connection, firm.Id, cancellationToken);
        await SupportStore.InsertTicketAsync(connection, ticketId, firm.Id, number, traderId, accountId, cleanSubject, SupportAuthor.Trader, now, cancellationToken);
        var ticket = (await SupportStore.LockAsync(connection, firm.Id, ticketId, traderId, cancellationToken))!;
        await SupportStore.AddMessageAsync(connection, ticket, SupportAuthor.Trader, null, message, attachments, Protect(firm), now, cancellationToken);
        await SupportStore.TraderWroteAsync(connection, ticketId, now, cancellationToken);
        await notifications.QueueSupportTicketAsync(connection, firm, ticket, message, attachments.Count, opened: true, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        signals.Emails.Set();
        return new SupportResult.Done(ticketId);
    }

    /// <summary>
    /// An administrator writes to one of the firm's traders first, in a new ticket that waits for the trader, optionally
    /// about one of the trader's accounts. The trader gets the message by email. Tickets the firm opens do not count toward
    /// the trader's limit.
    /// </summary>
    public async Task<SupportResult> FirmOpensAsync(
        Firm firm,
        string adminEmail,
        string? traderEmail,
        string? subject,
        string? body,
        Guid? accountId,
        IReadOnlyList<UploadedAttachment> files,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(traderEmail) || await users.FindTraderAsync(firm.Id, traderEmail, cancellationToken) is not { } trader)
        {
            return new SupportResult.Refused(StatusCodes.Status422UnprocessableEntity, "No trader at your firm has this email.", "traderEmail");
        }

        var cleanSubject = CleanSubject(subject);
        if ((SubjectRefusal(cleanSubject, "Write what the ticket is about.") ?? Refusal(body, files)) is { } refused)
        {
            return refused;
        }

        var (message, attachments) = Prepare(body, files);
        if (!await IsTradersAccountAsync(firm, trader.Id, accountId, cancellationToken))
        {
            return new SupportResult.Refused(StatusCodes.Status422UnprocessableEntity, "Choose one of the trader's accounts, or none.", "accountId");
        }

        var now = time.GetUtcNow();
        var ticketId = Guid.CreateVersion7(now);
        await using var connection = await store.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (await LimitRefusalAsync(connection, firm.Id, null, null, attachments, now, cancellationToken) is { } limited)
        {
            return limited;
        }

        var number = await SupportStore.NextNumberAsync(connection, firm.Id, cancellationToken);
        await SupportStore.InsertTicketAsync(connection, ticketId, firm.Id, number, trader.Id, accountId, cleanSubject, SupportAuthor.Firm, now, cancellationToken);
        var ticket = (await SupportStore.LockAsync(connection, firm.Id, ticketId, null, cancellationToken))!;
        await SupportStore.AddMessageAsync(connection, ticket, SupportAuthor.Firm, adminEmail, message, attachments, Protect(firm), now, cancellationToken);
        await SupportStore.FirmWroteAsync(connection, ticketId, adminEmail, close: false, now, cancellationToken);
        await Notifications.QueueSupportToTraderAsync(connection, firm, ticket, message, attachments.Count, opened: true, closed: false, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        signals.Emails.Set();
        return new SupportResult.Done(ticketId);
    }

    /// <summary>The trader writes in their ticket. A ticket that was answered or closed waits for the firm again.</summary>
    public async Task<SupportResult> TraderWritesAsync(Firm firm, Guid traderId, Guid ticketId, string? body, IReadOnlyList<UploadedAttachment> files, CancellationToken cancellationToken)
    {
        if (Refusal(body, files) is { } refused)
        {
            return refused;
        }

        var (message, attachments) = Prepare(body, files);

        var now = time.GetUtcNow();
        await using var connection = await store.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SupportStore.LockTraderAsync(connection, traderId, cancellationToken);
        if (await SupportStore.LockAsync(connection, firm.Id, ticketId, traderId, cancellationToken) is not { } ticket)
        {
            return UnknownTicket();
        }

        if (await LimitRefusalAsync(connection, firm.Id, traderId, ticket, attachments, now, cancellationToken) is { } limited)
        {
            return limited;
        }

        await SupportStore.AddMessageAsync(connection, ticket, SupportAuthor.Trader, null, message, attachments, Protect(firm), now, cancellationToken);
        await SupportStore.TraderWroteAsync(connection, ticketId, now, cancellationToken);
        if (ticket.Status != SupportTicketStatus.Open)
        {
            await notifications.QueueSupportTicketAsync(connection, firm, ticket, message, attachments.Count, opened: false, now, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        signals.Emails.Set();
        return new SupportResult.Done(ticketId);
    }

    /// <summary>An administrator answers the ticket in the firm's name, and closes it with <paramref name="close"/>.</summary>
    public async Task<SupportResult> FirmWritesAsync(
        Firm firm,
        string adminEmail,
        Guid ticketId,
        string? body,
        IReadOnlyList<UploadedAttachment> files,
        bool close,
        CancellationToken cancellationToken)
    {
        if (Refusal(body, files) is { } refused)
        {
            return refused;
        }

        var (message, attachments) = Prepare(body, files);

        var now = time.GetUtcNow();
        await using var connection = await store.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (await SupportStore.LockAsync(connection, firm.Id, ticketId, null, cancellationToken) is not { } ticket)
        {
            return UnknownTicket();
        }

        if (await LimitRefusalAsync(connection, firm.Id, null, ticket, attachments, now, cancellationToken) is { } limited)
        {
            return limited;
        }

        await SupportStore.AddMessageAsync(connection, ticket, SupportAuthor.Firm, adminEmail, message, attachments, Protect(firm), now, cancellationToken);
        await SupportStore.FirmWroteAsync(connection, ticketId, adminEmail, close, now, cancellationToken);
        await Notifications.QueueSupportToTraderAsync(connection, firm, ticket, message, attachments.Count, opened: false, close, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        signals.Emails.Set();
        return new SupportResult.Done(ticketId);
    }

    /// <summary>
    /// Closes the ticket, for the trader whose it is, or for an administrator when <paramref name="traderId"/> is null.
    /// A closed ticket stays closed. Nobody is emailed.
    /// </summary>
    public async Task<SupportResult> CloseAsync(Firm firm, Guid ticketId, Guid? traderId, string? adminEmail, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (await SupportStore.LockAsync(connection, firm.Id, ticketId, traderId, cancellationToken) is not { } ticket)
        {
            return UnknownTicket();
        }

        if (ticket.Status != SupportTicketStatus.Closed)
        {
            await SupportStore.CloseAsync(connection, ticketId, traderId is null ? adminEmail ?? "" : SupportRules.ClosedByTrader, time.GetUtcNow(), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new SupportResult.Done(ticketId);
    }

    /// <summary>The trader has read the ticket. Unknown when the trader has no such ticket.</summary>
    public async Task<SupportResult> MarkReadAsync(Firm firm, Guid traderId, Guid ticketId, CancellationToken cancellationToken) =>
        await store.MarkReadAsync(firm.Id, ticketId, traderId, time.GetUtcNow(), cancellationToken) == 0 ? UnknownTicket() : new SupportResult.Done(ticketId);

    /// <summary>The ticket with every message, for the trader whose it is, or for the firm when <paramref name="traderId"/> is null.</summary>
    public async Task<SupportTicketResponse?> ViewAsync(Firm firm, Guid ticketId, Guid? traderId, CancellationToken cancellationToken) =>
        await store.GetAsync(firm.Id, ticketId, traderId, cancellationToken) is { } ticket
            ? SupportTicketResponse.From(ticket, await store.MessagesAsync(ticket.Id, cancellationToken), forAdmin: traderId is null)
            : null;

    /// <summary>The attachment's file, decrypted, for the trader whose ticket it is in, or for the firm. Null when there is none.</summary>
    public async Task<(SupportAttachment Attachment, byte[] Content)?> ReadAttachmentAsync(Firm firm, Guid attachmentId, Guid? traderId, CancellationToken cancellationToken) =>
        await store.GetAttachmentAsync(firm.Id, attachmentId, traderId, cancellationToken) is { } found
            ? (found.Attachment, secrets.UnprotectBytes(found.ProtectedContent, Purpose(firm.Id, found.Attachment.Id)))
            : null;

    /// <summary>The subject on one line, without spaces around it.</summary>
    public static string CleanSubject(string? subject) =>
        string.Join(' ', (subject ?? "").Split((char[])['\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Trim();

    /// <summary>The message with its own line breaks as newlines, without blank lines or spaces around it.</summary>
    public static string CleanMessage(string? body) => (body ?? "").ReplaceLineEndings("\n").Trim();

    /// <summary>Why the message or its files cannot be sent. Null when they can.</summary>
    public static SupportResult.Refused? Refusal(string? body, IReadOnlyList<UploadedAttachment> files)
    {
        var message = CleanMessage(body);
        if (message.Length == 0)
        {
            return new SupportResult.Refused(StatusCodes.Status422UnprocessableEntity, "Write a message.", "body");
        }

        if (message.Length > SupportRules.MaxMessageLength)
        {
            return new SupportResult.Refused(StatusCodes.Status422UnprocessableEntity, $"Keep the message to {SupportRules.MaxMessageLength.ToString("N0", CultureInfo.InvariantCulture)} characters.", "body");
        }

        if (files.Count > SupportRules.MaxAttachmentsPerMessage)
        {
            return new SupportResult.Refused(StatusCodes.Status422UnprocessableEntity, $"Add at most {SupportRules.MaxAttachmentsPerMessage} files to a message.", "files");
        }

        if (files.Any(f => f.Content.Length > SupportRules.MaxAttachmentBytes))
        {
            return new SupportResult.Refused(StatusCodes.Status413PayloadTooLarge, "A file can be at most 5 MB.", "files");
        }

        return files.Any(f => UploadedFiles.ContentTypeOf(f.Content) is null)
            ? new SupportResult.Refused(StatusCodes.Status415UnsupportedMediaType, "Add PDF, PNG or JPEG files, such as a screenshot.", "files")
            : null;
    }

    // Why the subject, already cleaned, cannot be used. Null when it can.
    private static SupportResult.Refused? SubjectRefusal(string subject, string missing) =>
        subject.Length == 0 ? new SupportResult.Refused(StatusCodes.Status422UnprocessableEntity, missing, "subject")
        : subject.Length > SupportRules.MaxSubjectLength ? new SupportResult.Refused(StatusCodes.Status422UnprocessableEntity, $"Keep the subject to {SupportRules.MaxSubjectLength} characters.", "subject")
        : null;

    // Whether the account, if one is chosen, is one of the trader's at the firm.
    private async Task<bool> IsTradersAccountAsync(Firm firm, Guid traderId, Guid? accountId, CancellationToken cancellationToken) =>
        accountId is not { } id || (await accounts.GetAsync(firm.Id, id, cancellationToken))?.Account.TraderId == traderId;

    // The message and the files ready to save, once Refusal has found nothing wrong with them.
    // Caps that keep one trader, or one firm, from filling our database (ADR 0045): messages in a ticket, and files in a day.
    // Without a trader, the firm's administrators write.
    private static async Task<SupportResult.Refused?> LimitRefusalAsync(
        NpgsqlConnection connection,
        string firmId,
        Guid? traderId,
        SupportTicket? ticket,
        IReadOnlyList<CheckedAttachment> attachments,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (ticket?.Messages >= SupportRules.MaxMessagesPerTicket)
        {
            return new SupportResult.Refused(
                StatusCodes.Status409Conflict,
                $"This ticket has {SupportRules.MaxMessagesPerTicket} messages, as many as a ticket can have. Open a new ticket to write more.");
        }

        var adding = attachments.Sum(a => (long)a.Content.Length);
        var most = traderId is null ? SupportRules.MaxFirmFileBytesPerDay : SupportRules.MaxTraderFileBytesPerDay;
        if (adding > 0 && await SupportStore.FileBytesSinceAsync(connection, firmId, traderId, now - TimeSpan.FromDays(1), cancellationToken) + adding > most)
        {
            return new SupportResult.Refused(
                StatusCodes.Status409Conflict,
                FormattableString.Invariant($"At most {most / (1024 * 1024)} MB of files can be added in a day, and this would go past it. Write without files, or add them tomorrow."),
                "files");
        }

        return null;
    }

    private static (string Message, List<CheckedAttachment> Attachments) Prepare(string? body, IReadOnlyList<UploadedAttachment> files) =>
        (CleanMessage(body), [.. files.Select(f => new CheckedAttachment(UploadedFiles.CleanFileName(f.FileName, "attachment"), UploadedFiles.ContentTypeOf(f.Content)!, f.Content))]);

    private Func<Guid, byte[], byte[]> Protect(Firm firm) => (attachmentId, content) => secrets.ProtectBytes(content, Purpose(firm.Id, attachmentId));

    // Ties the encrypted file to its firm and attachment, so it cannot be moved to another.
    private static string Purpose(string firmId, Guid attachmentId) => $"firm:{firmId}:support:{attachmentId}";

    private static SupportResult.Refused UnknownTicket() => new(StatusCodes.Status404NotFound, "No such ticket.");
}
