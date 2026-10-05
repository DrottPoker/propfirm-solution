using System.Security.Claims;

using Microsoft.AspNetCore.Http.HttpResults;

using Prop.Api.Files;
using Prop.Api.Firms;
using Prop.Api.Portal;

namespace Prop.Api.Review;

/// <summary>The firm's side of our review (ADR 0021): its application, documents and sending it, in its admin panel.</summary>
internal static class ReviewEndpoints
{
    /// <summary>Maps the review on the admin group.</summary>
    public static RouteGroupBuilder MapAdminVerification(this RouteGroupBuilder admin)
    {
        admin.MapGet("/verification", GetAsync);
        admin.MapPut("/verification/application", SaveApplicationAsync);

        // A form, which needs no antiforgery token: the session cookie is SameSite=Lax, so another site's form never carries it.
        admin.MapPost("/verification/documents", AddDocumentAsync).DisableAntiforgery();
        admin.MapGet("/verification/documents/{documentId:guid}", GetDocumentAsync);
        admin.MapDelete("/verification/documents/{documentId:guid}", RemoveDocumentAsync);
        admin.MapPost("/verification/submit", SubmitAsync);
        return admin;
    }

    /// <summary>The firm's review: its status, the application, the documents, our message and the deposit.</summary>
    private static async Task<Ok<VerificationResponse>> GetAsync(HttpContext context, ReviewService reviews, ReviewStore store, CancellationToken cancellationToken) =>
        TypedResults.Ok(await ViewAsync(PortalFirmFilter.FirmOf(context), reviews, store, cancellationToken));

    /// <summary>Saves the application as a draft. Only the fields that are filled in are checked.</summary>
    private static async Task<Results<Ok<VerificationResponse>, ProblemHttpResult>> SaveApplicationAsync(
        FirmApplication request,
        HttpContext context,
        ClaimsPrincipal principal,
        ReviewService reviews,
        ReviewStore store,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        return await reviews.SaveApplicationAsync(firm, request, EmailOf(principal), cancellationToken) is ReviewResult.Refused refused
            ? ProblemOf(refused)
            : TypedResults.Ok(await ViewAsync(firm, reviews, store, cancellationToken));
    }

    /// <summary>Adds a PDF, PNG or JPEG document of at most 10 MB, in the form field file.</summary>
    private static async Task<Results<Created<DocumentResponse>, ProblemHttpResult>> AddDocumentAsync(
        IFormFile? file,
        HttpContext context,
        ClaimsPrincipal principal,
        ReviewService reviews,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return ProblemOf(new ReviewResult.Refused(StatusCodes.Status422UnprocessableEntity, "Choose a file to add."));
        }

        if (file.Length > ReviewService.MaxDocumentBytes)
        {
            return ProblemOf(new ReviewResult.Refused(StatusCodes.Status413PayloadTooLarge, "A document can be at most 10 MB."));
        }

        using var content = new MemoryStream((int)file.Length);
        await file.CopyToAsync(content, cancellationToken);
        var firm = PortalFirmFilter.FirmOf(context);
        var (result, document) = await reviews.AddDocumentAsync(firm, file.FileName, content.ToArray(), EmailOf(principal), cancellationToken);
        return result is ReviewResult.Refused refused
            ? ProblemOf(refused)
            : TypedResults.Created($"/api/portal/admin/verification/documents/{document!.Id}", DocumentResponse.From(document));
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> GetDocumentAsync(
        Guid documentId,
        HttpContext context,
        ReviewService reviews,
        CancellationToken cancellationToken) =>
        await DocumentAsync(context, PortalFirmFilter.FirmOf(context).Id, documentId, reviews, cancellationToken);

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveDocumentAsync(
        Guid documentId,
        HttpContext context,
        ClaimsPrincipal principal,
        ReviewService reviews,
        CancellationToken cancellationToken) =>
        await reviews.RemoveDocumentAsync(PortalFirmFilter.FirmOf(context), documentId, EmailOf(principal), cancellationToken) is ReviewResult.Refused refused
            ? ProblemOf(refused)
            : TypedResults.NoContent();

    /// <summary>
    /// Sends the application. Answers with a checkout page when the deposit is to be paid first, and with no page
    /// when the application was sent at once.
    /// </summary>
    private static async Task<Results<Ok<SubmitResponse>, ProblemHttpResult>> SubmitAsync(
        HttpContext context,
        ClaimsPrincipal principal,
        ReviewService reviews,
        CancellationToken cancellationToken) =>
        await reviews.SubmitAsync(PortalFirmFilter.FirmOf(context), EmailOf(principal), cancellationToken) switch
        {
            ReviewResult.Checkout checkout => TypedResults.Ok(new SubmitResponse(checkout.Url)),
            ReviewResult.Refused refused => ProblemOf(refused),
            _ => TypedResults.Ok(new SubmitResponse(null)),
        };

    internal static async Task<VerificationResponse> ViewAsync(Firm firm, ReviewService reviews, ReviewStore store, CancellationToken cancellationToken)
    {
        var review = await reviews.GetAsync(firm, cancellationToken);
        var documents = await store.ListDocumentsAsync(firm.Id, cancellationToken);
        var (amount, currency, paid) = await reviews.DepositAsync(firm, cancellationToken);
        var identity = await reviews.IdentityReadinessAsync(firm.Id, cancellationToken);
        return new VerificationResponse(
            review.Status,
            review.Application,
            [.. documents.Select(DocumentResponse.From)],
            review.Message,
            review.SubmittedAt,
            review.DecidedAt,
            new DepositResponse(amount, currency, paid),
            review.CanEdit && firm.Status != FirmStatus.Live,
            ReviewService.SubmitProblem(firm, review),
            ReviewService.MaxDocuments,
            ReviewService.MaxDocumentBytes,
            VatNumbers.EuCountries,
            review.CanEdit && firm.Status != FirmStatus.Live ? ApplicationRules.Problems(review.Application, complete: true) : [],
            identity);
    }

    /// <summary>A document as a download, never shown in the browser, so a file cannot run as a page on our address.</summary>
    internal static async Task<Results<FileContentHttpResult, ProblemHttpResult>> DocumentAsync(
        HttpContext context,
        string firmId,
        Guid documentId,
        ReviewService reviews,
        CancellationToken cancellationToken)
    {
        if (await reviews.ReadDocumentAsync(firmId, documentId, cancellationToken) is not { } found)
        {
            return ProblemOf(new ReviewResult.Refused(StatusCodes.Status404NotFound, "The firm has no such document."));
        }

        return UploadedFiles.Download(context, found.Content, found.Document.ContentType, found.Document.FileName);
    }

    internal static ProblemHttpResult ProblemOf(ReviewResult.Refused refused) =>
        TypedResults.Problem(
            statusCode: refused.StatusCode,
            title: refused.Problem,
            extensions: refused.Field is null ? null : new Dictionary<string, object?> { ["field"] = refused.Field });

    private static string EmailOf(ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.Email) ?? "";
}
