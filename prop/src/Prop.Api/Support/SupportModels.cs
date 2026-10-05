namespace Prop.Api.Support;

/// <summary>Where a support ticket is (ADR 0041).</summary>
public enum SupportTicketStatus
{
    /// <summary>The trader wrote last, and the ticket waits for the firm.</summary>
    Open,

    /// <summary>The firm answered last, and the ticket waits for the trader.</summary>
    Answered,

    /// <summary>The trader or the firm closed it. A new message opens it again.</summary>
    Closed,
}

/// <summary>Who wrote a message: the trader, or the firm. Traders never see which of the firm's administrators wrote.</summary>
public enum SupportAuthor
{
    Trader,
    Firm,
}

/// <summary>Which of the firm's tickets the admin panel lists.</summary>
public enum SupportTicketGroup
{
    Open,
    Answered,
    Closed,
    All,
}

/// <summary>The limits of support tickets, the same for every firm.</summary>
internal static class SupportRules
{
    public const int MaxSubjectLength = 120;

    public const int MaxMessageLength = 5_000;

    public const int MaxAttachmentsPerMessage = 3;

    public const int MaxAttachmentBytes = 5 * 1024 * 1024;

    /// <summary>Open and answered tickets a trader can have at once, so one trader cannot flood the firm.</summary>
    public const int MaxOpenTicketsPerTrader = 10;

    public const int MaxTicketsPerPage = 100;

    /// <summary>How much of the latest message a list shows.</summary>
    public const int PreviewLength = 140;

    /// <summary>Who closed a ticket, when it was the trader. Otherwise the administrator's email.</summary>
    public const string ClosedByTrader = "trader";
}

/// <summary>
/// A ticket with its trader and the account it is about. <paramref name="WaitingSince"/> is set while it waits for the
/// firm, and the trader has an unread answer while <paramref name="AnsweredAt"/> is after <paramref name="TraderReadAt"/>.
/// <paramref name="ClosedBy"/> is <see cref="SupportRules.ClosedByTrader"/> or an administrator's email.
/// </summary>
internal sealed record SupportTicket(
    Guid Id,
    string FirmId,
    long Number,
    Guid TraderId,
    string TraderEmail,
    string? TraderName,
    SupportTicketAccount? Account,
    string Subject,
    SupportTicketStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? WaitingSince,
    DateTimeOffset? AnsweredAt,
    DateTimeOffset? TraderReadAt,
    DateTimeOffset? ClosedAt,
    string? ClosedBy,
    int Messages)
{
    public bool UnreadByTrader => AnsweredAt is { } answered && (TraderReadAt is not { } read || answered > read);
}

/// <summary>The challenge account a ticket is about.</summary>
internal sealed record SupportTicketAccount(Guid Id, long Number, string ChallengeName);

/// <summary>A ticket in a list, with its latest message.</summary>
internal sealed record SupportTicketItem(SupportTicket Ticket, SupportAuthor LastAuthor, string LastBody);

/// <summary>A message of a ticket. <paramref name="AdminEmail"/> is the administrator who wrote for the firm.</summary>
internal sealed record SupportMessage(Guid Id, int Position, SupportAuthor Author, string? AdminEmail, string Body, DateTimeOffset CreatedAt, IReadOnlyList<SupportAttachment> Attachments);

/// <summary>A file added to a message. Its content is kept encrypted and fetched on its own.</summary>
internal sealed record SupportAttachment(Guid Id, Guid TicketId, string FileName, string ContentType, int Size);

/// <summary>A file as it was uploaded, before it is checked.</summary>
internal sealed record UploadedAttachment(string? FileName, byte[] Content);

/// <summary>How many of the firm's tickets the search finds in each group.</summary>
internal sealed record SupportTicketCounts(int Open, int Answered, int Closed, int All);

/// <summary>Where the trader's tickets are, for the portal's menu.</summary>
internal sealed record TraderSupportSummary(int Active, int Unread);

/// <summary>What waits for the firm: its open tickets, and since when the oldest has waited.</summary>
internal sealed record FirmSupportSummary(int Open, DateTimeOffset? OldestWaiting, int Answered);

/// <summary>Where a page of tickets starts: after the ticket with this sort time and id, from the previous page.</summary>
internal sealed record SupportCursor(DateTimeOffset Time, Guid Id)
{
    public override string ToString() => $"{Time.UtcTicks}.{Id:N}";

    public static bool TryParse(string? text, out SupportCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        var parts = text.Split('.');
        if (parts.Length != 2
            || !long.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTimeOffset.MaxValue.UtcTicks
            || !Guid.TryParseExact(parts[1], "N", out var id))
        {
            return false;
        }

        cursor = new SupportCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}

/// <summary>What a change to a ticket came to: done, or refused with a status code, the reason and the field it is about.</summary>
internal abstract record SupportResult
{
    public sealed record Done(Guid TicketId) : SupportResult;

    public sealed record Refused(int StatusCode, string Problem, string? Field = null) : SupportResult;
}

/// <summary>The account a ticket is about, as the portal shows it.</summary>
public sealed record SupportAccountResponse(Guid Id, long Number, string ChallengeName)
{
    internal static SupportAccountResponse? From(SupportTicketAccount? account) =>
        account is null ? null : new SupportAccountResponse(account.Id, account.Number, account.ChallengeName);
}

/// <summary>A file added to a message, fetched from the attachments path of the trader's or the admin panel's API.</summary>
public sealed record SupportAttachmentResponse(Guid Id, string FileName, string ContentType, int Size)
{
    internal static SupportAttachmentResponse From(SupportAttachment attachment) =>
        new(attachment.Id, attachment.FileName, attachment.ContentType, attachment.Size);
}

/// <summary>A message of a ticket. <paramref name="AdminEmail"/> is who wrote for the firm, and only the admin panel sees it.</summary>
public sealed record SupportMessageResponse(Guid Id, SupportAuthor Author, string? AdminEmail, string Body, DateTimeOffset CreatedAt, IReadOnlyList<SupportAttachmentResponse> Attachments)
{
    internal static SupportMessageResponse From(SupportMessage message, bool forAdmin) =>
        new(message.Id, message.Author, forAdmin ? message.AdminEmail : null, message.Body, message.CreatedAt, [.. message.Attachments.Select(SupportAttachmentResponse.From)]);
}

/// <summary>
/// A ticket in a list: its trader and account, when it was last written in, since when it has waited for the firm, how
/// many messages it has and the start of the latest, and whether the trader has an answer to read.
/// </summary>
public sealed record SupportTicketSummaryResponse(
    Guid Id,
    long Number,
    string Subject,
    SupportTicketStatus Status,
    string TraderEmail,
    SupportAccountResponse? Account,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? WaitingSince,
    int Messages,
    SupportAuthor LastAuthor,
    string Preview,
    bool Unread)
{
    internal static SupportTicketSummaryResponse From(SupportTicketItem item)
    {
        var ticket = item.Ticket;
        return new(
            ticket.Id,
            ticket.Number,
            ticket.Subject,
            ticket.Status,
            ticket.TraderEmail,
            SupportAccountResponse.From(ticket.Account),
            ticket.CreatedAt,
            ticket.UpdatedAt,
            ticket.WaitingSince,
            ticket.Messages,
            item.LastAuthor,
            PreviewOf(item.LastBody),
            ticket.UnreadByTrader);
    }

    // The start of a message on one line.
    private static string PreviewOf(string body)
    {
        var line = string.Join(' ', body.Split((char[])['\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return line.Length <= SupportRules.PreviewLength ? line : string.Concat(line.AsSpan(0, SupportRules.PreviewLength - 3).TrimEnd(), "...");
    }
}

/// <summary>
/// A ticket with every message, oldest first. <paramref name="ClosedBy"/> is who closed it, and
/// <paramref name="ClosedByAdmin"/> which administrator, which only the admin panel sees. <paramref name="Unread"/> is
/// whether the trader has an answer to read, and is always false in the admin panel.
/// </summary>
public sealed record SupportTicketResponse(
    Guid Id,
    long Number,
    string Subject,
    SupportTicketStatus Status,
    string TraderEmail,
    string? TraderName,
    SupportAccountResponse? Account,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? WaitingSince,
    DateTimeOffset? ClosedAt,
    SupportAuthor? ClosedBy,
    string? ClosedByAdmin,
    bool Unread,
    IReadOnlyList<SupportMessageResponse> Messages)
{
    internal static SupportTicketResponse From(SupportTicket ticket, IReadOnlyList<SupportMessage> messages, bool forAdmin)
    {
        SupportAuthor? closedBy = ticket.ClosedBy switch
        {
            null => null,
            SupportRules.ClosedByTrader => SupportAuthor.Trader,
            _ => SupportAuthor.Firm,
        };
        return new(
            ticket.Id,
            ticket.Number,
            ticket.Subject,
            ticket.Status,
            ticket.TraderEmail,
            ticket.TraderName,
            SupportAccountResponse.From(ticket.Account),
            ticket.CreatedAt,
            ticket.UpdatedAt,
            ticket.WaitingSince,
            ticket.ClosedAt,
            closedBy,
            forAdmin && closedBy == SupportAuthor.Firm ? ticket.ClosedBy : null,
            !forAdmin && ticket.UnreadByTrader,
            [.. messages.Select(m => SupportMessageResponse.From(m, forAdmin))]);
    }
}

/// <summary>A page of tickets, and <paramref name="Next"/> to ask for the next page with, if there is one.</summary>
public sealed record SupportTicketsResponse(IReadOnlyList<SupportTicketSummaryResponse> Tickets, string? Next);

/// <summary>How many of the firm's tickets the search finds in each group.</summary>
public sealed record SupportTicketCountsResponse(int Open, int Answered, int Closed, int All)
{
    internal static SupportTicketCountsResponse From(SupportTicketCounts counts) => new(counts.Open, counts.Answered, counts.Closed, counts.All);
}

/// <summary>A page of the firm's tickets in a group, the counts in every group, and <paramref name="Next"/> for the next page.</summary>
public sealed record AdminSupportTicketsResponse(IReadOnlyList<SupportTicketSummaryResponse> Tickets, SupportTicketCountsResponse Counts, string? Next);

/// <summary>The trader's tickets that are not closed, and those with an answer the trader has not read.</summary>
public sealed record TraderSupportSummaryResponse(int Active, int Unread);

/// <summary>The firm's tickets that wait for it, since when the oldest has waited, and those that wait for the trader.</summary>
public sealed record FirmSupportSummaryResponse(int Open, DateTimeOffset? OldestWaiting, int Answered);
