using System.Security.Claims;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

using Prop.Api.Files;
using Prop.Api.Firms;
using Prop.Api.Portal;

namespace Prop.Api.Support;

/// <summary>
/// Support tickets in the portal (ADR 0041): the trader's own tickets, and the firm's in its admin panel, where it can also
/// write to a trader first. A message, with up to three files, is sent as a form, so the files go with it in one request.
/// </summary>
internal static class SupportEndpoints
{
    /// <summary>Limits how fast tickets and messages are written (ADR 0045).</summary>
    public const string WriteRateLimit = "support-writes";

    public static RouteGroupBuilder MapTraderSupport(this RouteGroupBuilder portal)
    {
        var support = portal.MapGroup("/support").RequireAuthorization(PortalAuth.TraderPolicy);
        support.MapGet("/summary", GetMySummaryAsync);
        support.MapGet("/tickets", ListMyTicketsAsync);
        support.MapPost("/tickets", OpenTicketAsync).DisableAntiforgery().RequireRateLimiting(WriteRateLimit);
        support.MapGet("/tickets/{ticketId:guid}", GetMyTicketAsync);
        support.MapPost("/tickets/{ticketId:guid}/messages", WriteInMyTicketAsync).DisableAntiforgery().RequireRateLimiting(WriteRateLimit);
        support.MapPost("/tickets/{ticketId:guid}/close", CloseMyTicketAsync);
        support.MapPost("/tickets/{ticketId:guid}/read", MarkReadAsync);
        support.MapGet("/attachments/{attachmentId:guid}", GetMyAttachmentAsync);
        return portal;
    }

    public static RouteGroupBuilder MapAdminSupport(this RouteGroupBuilder admin)
    {
        admin.MapGet("/support/summary", GetFirmSummaryAsync);
        admin.MapGet("/support/tickets", ListTicketsAsync);
        admin.MapPost("/support/tickets", OpenTicketWithTraderAsync).DisableAntiforgery().RequireRateLimiting(WriteRateLimit);
        admin.MapGet("/support/tickets/{ticketId:guid}", GetTicketAsync);
        admin.MapPost("/support/tickets/{ticketId:guid}/messages", AnswerAsync).DisableAntiforgery().RequireRateLimiting(WriteRateLimit);
        admin.MapPost("/support/tickets/{ticketId:guid}/close", CloseTicketAsync);
        admin.MapGet("/support/attachments/{attachmentId:guid}", GetAttachmentAsync);
        return admin;
    }

    /// <summary>The trader's tickets that are not closed, and those with an answer the trader has not read.</summary>
    private static async Task<Ok<TraderSupportSummaryResponse>> GetMySummaryAsync(
        ClaimsPrincipal principal,
        HttpContext context,
        SupportStore store,
        CancellationToken cancellationToken)
    {
        var summary = await store.TraderSummaryAsync(PortalFirmFilter.FirmOf(context).Id, PortalAuth.UserIdOf(principal), cancellationToken);
        return TypedResults.Ok(new TraderSupportSummaryResponse(summary.Active, summary.Unread));
    }

    /// <summary>A page of the trader's tickets, the latest written in first. <paramref name="cursor"/> is the <c>next</c> of the previous page.</summary>
    private static async Task<Results<Ok<SupportTicketsResponse>, ProblemHttpResult>> ListMyTicketsAsync(
        ClaimsPrincipal principal,
        HttpContext context,
        SupportStore store,
        CancellationToken cancellationToken,
        string? cursor = null,
        int limit = 50)
    {
        if (PageProblem(cursor, limit, out var after) is { } problem)
        {
            return problem;
        }

        var items = await store.ListAsync(PortalFirmFilter.FirmOf(context).Id, PortalAuth.UserIdOf(principal), SupportTicketGroup.All, null, after, limit, cancellationToken);
        return TypedResults.Ok(new SupportTicketsResponse([.. items.Select(SupportTicketSummaryResponse.From)], Next(items, SupportTicketGroup.All, limit)));
    }

    /// <summary>
    /// The trader opens a ticket: <c>subject</c>, <c>body</c>, optionally <c>accountId</c>, one of the trader's accounts,
    /// and up to three PDF, PNG or JPEG files of at most 5 MB. 409 when the trader has too many tickets that are not closed.
    /// </summary>
    private static async Task<Results<Created<SupportTicketResponse>, ProblemHttpResult>> OpenTicketAsync(
        [FromForm] string? subject,
        [FromForm] string? body,
        [FromForm] Guid? accountId,
        IFormFileCollection? files,
        ClaimsPrincipal principal,
        HttpContext context,
        SupportService support,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        var traderId = PortalAuth.UserIdOf(principal);
        var result = await support.OpenAsync(firm, traderId, subject, body, accountId, await ReadFilesAsync(files, cancellationToken), cancellationToken);
        if (result is SupportResult.Refused refused)
        {
            return ProblemOf(refused);
        }

        var ticketId = ((SupportResult.Done)result).TicketId;
        return TypedResults.Created($"/api/portal/support/tickets/{ticketId}", await support.ViewAsync(firm, ticketId, traderId, cancellationToken));
    }

    /// <summary>The trader's ticket with every message, oldest first. 404 for another trader's.</summary>
    private static async Task<Results<Ok<SupportTicketResponse>, ProblemHttpResult>> GetMyTicketAsync(
        Guid ticketId,
        ClaimsPrincipal principal,
        HttpContext context,
        SupportService support,
        CancellationToken cancellationToken) =>
        await support.ViewAsync(PortalFirmFilter.FirmOf(context), ticketId, PortalAuth.UserIdOf(principal), cancellationToken) is { } ticket
            ? TypedResults.Ok(ticket)
            : UnknownTicket();

    /// <summary>The trader writes in the ticket, with <c>body</c> and up to three files. An answered or closed ticket waits for the firm again.</summary>
    private static async Task<Results<Ok<SupportTicketResponse>, ProblemHttpResult>> WriteInMyTicketAsync(
        Guid ticketId,
        [FromForm] string? body,
        IFormFileCollection? files,
        ClaimsPrincipal principal,
        HttpContext context,
        SupportService support,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        var traderId = PortalAuth.UserIdOf(principal);
        return await ViewOfAsync(
            await support.TraderWritesAsync(firm, traderId, ticketId, body, await ReadFilesAsync(files, cancellationToken), cancellationToken),
            () => support.ViewAsync(firm, ticketId, traderId, cancellationToken));
    }

    /// <summary>The trader closes the ticket, for example when the answer helped.</summary>
    private static async Task<Results<Ok<SupportTicketResponse>, ProblemHttpResult>> CloseMyTicketAsync(
        Guid ticketId,
        ClaimsPrincipal principal,
        HttpContext context,
        SupportService support,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        var traderId = PortalAuth.UserIdOf(principal);
        return await ViewOfAsync(
            await support.CloseAsync(firm, ticketId, traderId, null, cancellationToken),
            () => support.ViewAsync(firm, ticketId, traderId, cancellationToken));
    }

    /// <summary>The trader has read the ticket's answers, so it no longer counts as unread.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> MarkReadAsync(
        Guid ticketId,
        ClaimsPrincipal principal,
        HttpContext context,
        SupportService support,
        CancellationToken cancellationToken) =>
        await support.MarkReadAsync(PortalFirmFilter.FirmOf(context), PortalAuth.UserIdOf(principal), ticketId, cancellationToken) is SupportResult.Refused refused
            ? ProblemOf(refused)
            : TypedResults.NoContent();

    /// <summary>A file in one of the trader's tickets, as a download.</summary>
    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> GetMyAttachmentAsync(
        Guid attachmentId,
        ClaimsPrincipal principal,
        HttpContext context,
        SupportService support,
        CancellationToken cancellationToken) =>
        await support.ReadAttachmentAsync(PortalFirmFilter.FirmOf(context), attachmentId, PortalAuth.UserIdOf(principal), cancellationToken) is { } found
            ? UploadedFiles.Download(context, found.Content, found.Attachment.ContentType, found.Attachment.FileName)
            : UnknownAttachment();

    /// <summary>The firm's tickets that wait for it, since when the oldest has waited, and those that wait for the trader.</summary>
    private static async Task<Ok<FirmSupportSummaryResponse>> GetFirmSummaryAsync(HttpContext context, SupportStore store, CancellationToken cancellationToken)
    {
        var summary = await store.FirmSummaryAsync(PortalFirmFilter.FirmOf(context).Id, cancellationToken);
        return TypedResults.Ok(new FirmSupportSummaryResponse(summary.Open, summary.OldestWaiting, summary.Answered));
    }

    /// <summary>
    /// A page of the firm's tickets in the group: open ones the longest waiting first, the others the latest written in
    /// first, with how many the search finds in every group. <paramref name="search"/> is part of the trader's email or
    /// the subject, or the ticket's number with or without #.
    /// </summary>
    private static async Task<Results<Ok<AdminSupportTicketsResponse>, ProblemHttpResult>> ListTicketsAsync(
        HttpContext context,
        SupportStore store,
        CancellationToken cancellationToken,
        SupportTicketGroup group = SupportTicketGroup.Open,
        string? search = null,
        string? cursor = null,
        int limit = 50)
    {
        if (PageProblem(cursor, limit, out var after) is { } problem)
        {
            return problem;
        }

        var firmId = PortalFirmFilter.FirmOf(context).Id;
        var items = await store.ListAsync(firmId, null, group, search, after, limit, cancellationToken);
        var counts = await store.CountAsync(firmId, search, cancellationToken);
        return TypedResults.Ok(new AdminSupportTicketsResponse([.. items.Select(SupportTicketSummaryResponse.From)], SupportTicketCountsResponse.From(counts), Next(items, group, limit)));
    }

    /// <summary>
    /// The administrator writes to one of the firm's traders first: <c>traderEmail</c>, <c>subject</c>, <c>body</c>,
    /// optionally <c>accountId</c>, one of the trader's accounts, and up to three files. The ticket waits for the trader,
    /// who is emailed the message.
    /// </summary>
    private static async Task<Results<Created<SupportTicketResponse>, ProblemHttpResult>> OpenTicketWithTraderAsync(
        [FromForm] string? traderEmail,
        [FromForm] string? subject,
        [FromForm] string? body,
        [FromForm] Guid? accountId,
        IFormFileCollection? files,
        ClaimsPrincipal principal,
        HttpContext context,
        SupportService support,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        var result = await support.FirmOpensAsync(firm, AdminEmailOf(principal), traderEmail, subject, body, accountId, await ReadFilesAsync(files, cancellationToken), cancellationToken);
        if (result is SupportResult.Refused refused)
        {
            return ProblemOf(refused);
        }

        var ticketId = ((SupportResult.Done)result).TicketId;
        return TypedResults.Created($"/api/portal/admin/support/tickets/{ticketId}", await support.ViewAsync(firm, ticketId, null, cancellationToken));
    }

    /// <summary>The firm's ticket with every message, oldest first, and which administrator wrote each answer.</summary>
    private static async Task<Results<Ok<SupportTicketResponse>, ProblemHttpResult>> GetTicketAsync(
        Guid ticketId,
        HttpContext context,
        SupportService support,
        CancellationToken cancellationToken) =>
        await support.ViewAsync(PortalFirmFilter.FirmOf(context), ticketId, null, cancellationToken) is { } ticket ? TypedResults.Ok(ticket) : UnknownTicket();

    /// <summary>
    /// The administrator answers in the firm's name, with <c>body</c> and up to three files, and with <c>close</c> closes
    /// the ticket too. The trader is emailed the answer.
    /// </summary>
    private static async Task<Results<Ok<SupportTicketResponse>, ProblemHttpResult>> AnswerAsync(
        Guid ticketId,
        [FromForm] string? body,
        [FromForm] bool? close,
        IFormFileCollection? files,
        ClaimsPrincipal principal,
        HttpContext context,
        SupportService support,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        return await ViewOfAsync(
            await support.FirmWritesAsync(firm, AdminEmailOf(principal), ticketId, body, await ReadFilesAsync(files, cancellationToken), close == true, cancellationToken),
            () => support.ViewAsync(firm, ticketId, null, cancellationToken));
    }

    /// <summary>The administrator closes the ticket without answering. The trader is not emailed.</summary>
    private static async Task<Results<Ok<SupportTicketResponse>, ProblemHttpResult>> CloseTicketAsync(
        Guid ticketId,
        ClaimsPrincipal principal,
        HttpContext context,
        SupportService support,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        return await ViewOfAsync(
            await support.CloseAsync(firm, ticketId, null, AdminEmailOf(principal), cancellationToken),
            () => support.ViewAsync(firm, ticketId, null, cancellationToken));
    }

    /// <summary>A file in one of the firm's tickets, as a download.</summary>
    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> GetAttachmentAsync(
        Guid attachmentId,
        HttpContext context,
        SupportService support,
        CancellationToken cancellationToken) =>
        await support.ReadAttachmentAsync(PortalFirmFilter.FirmOf(context), attachmentId, null, cancellationToken) is { } found
            ? UploadedFiles.Download(context, found.Content, found.Attachment.ContentType, found.Attachment.FileName)
            : UnknownAttachment();

    // The files of the form, each read up to one byte past the limit and one file past the most, so the service can say
    // what is wrong without a large file being read whole.
    private static async Task<List<UploadedAttachment>> ReadFilesAsync(IFormFileCollection? files, CancellationToken cancellationToken)
    {
        var read = new List<UploadedAttachment>();
        foreach (var file in (files ?? (IEnumerable<IFormFile>)[]).Take(SupportRules.MaxAttachmentsPerMessage + 1))
        {
            var content = new byte[Math.Min(file.Length, SupportRules.MaxAttachmentBytes + 1L)];
            await using var stream = file.OpenReadStream();
            await stream.ReadExactlyAsync(content, cancellationToken);
            read.Add(new UploadedAttachment(file.FileName, content));
        }

        return read;
    }

    private static async Task<Results<Ok<SupportTicketResponse>, ProblemHttpResult>> ViewOfAsync(SupportResult result, Func<Task<SupportTicketResponse?>> view) =>
        result is SupportResult.Refused refused ? ProblemOf(refused)
        : await view() is { } ticket ? TypedResults.Ok(ticket)
        : UnknownTicket();

    private static ProblemHttpResult? PageProblem(string? cursor, int limit, out SupportCursor? after)
    {
        if (!SupportCursor.TryParse(cursor, out after))
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "cursor must be the next of an earlier page.");
        }

        return limit is < 1 or > SupportRules.MaxTicketsPerPage ? Problem(StatusCodes.Status422UnprocessableEntity, $"limit must be 1 to {SupportRules.MaxTicketsPerPage}.") : null;
    }

    // Where the next page starts, when this one is full.
    private static string? Next(List<SupportTicketItem> items, SupportTicketGroup group, int limit)
    {
        if (items.Count < limit)
        {
            return null;
        }

        var last = items[^1].Ticket;
        return new SupportCursor(group == SupportTicketGroup.Open && last.WaitingSince is { } waiting ? waiting : last.UpdatedAt, last.Id).ToString();
    }

    private static string AdminEmailOf(ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.Email) ?? "";

    private static ProblemHttpResult UnknownTicket() => Problem(StatusCodes.Status404NotFound, "No such ticket.");

    private static ProblemHttpResult UnknownAttachment() => Problem(StatusCodes.Status404NotFound, "No such file.");

    private static ProblemHttpResult Problem(int statusCode, string title) => TypedResults.Problem(statusCode: statusCode, title: title);

    private static ProblemHttpResult ProblemOf(SupportResult.Refused refused) =>
        TypedResults.Problem(
            statusCode: refused.StatusCode,
            title: refused.Problem,
            extensions: refused.Field is null ? null : new Dictionary<string, object?> { ["field"] = refused.Field });
}
