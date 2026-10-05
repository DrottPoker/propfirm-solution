using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.Extensions.Options;

using Prop.Api.Billing;
using Prop.Api.Challenges;
using Prop.Api.Configuration;
using Prop.Api.Email;
using Prop.Api.Files;
using Prop.Api.Firms;
using Prop.Api.Identity;
using Prop.Api.Json;
using Prop.Api.Ops;
using Prop.Api.Portal;

namespace Prop.Api.Review;

/// <summary>What a request about a review led to: done, a checkout page for the deposit, or a refusal with the reason.</summary>
internal abstract record ReviewResult
{
    public sealed record Done : ReviewResult;

    public sealed record Checkout(Uri Url) : ReviewResult;

    /// <summary><paramref name="Field"/> is the application's field the problem is with, when there is one.</summary>
    public sealed record Refused(int StatusCode, string Problem, string? Field = null) : ReviewResult;
}

/// <summary>
/// Our review of firms before they go live, and our suspension of firms (ADR 0021). The firm fills in its
/// application in its admin panel and sends it, paying the deposit the first time. Our staff approve it, ask for
/// changes or reject it. Each change locks the firm's review, so two decisions never cross, and is recorded in the
/// firm's events with who made it.
/// </summary>
internal sealed partial class ReviewService(
    ReviewStore store,
    IdentityStore identities,
    BillingService billing,
    FirmStore firmStore,
    FirmCatalog firms,
    FirmAdmins admins,
    StaffNotifier staff,
    SecretProtector secrets,
    IEmailSender email,
    WorkSignals signals,
    IOptions<PlatformOptions> platform,
    TimeProvider time,
    ILogger<ReviewService> logger)
{
    public const int MaxDocuments = 10;

    public const int MaxDocumentBytes = 10 * 1024 * 1024;

    public const int MaxMessageLength = 2_000;

    /// <summary>The firm's review. A firm without one has an empty draft, and a live firm without one needs none.</summary>
    public async Task<FirmReview> GetAsync(Firm firm, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(cancellationToken);
        var review = await ReviewStore.GetAsync(connection, firm.Id, forUpdate: false, cancellationToken)
            ?? new FirmReview(firm.Id, firm.Status == FirmStatus.Live ? ReviewStatus.Approved : ReviewStatus.Draft, FirmApplication.Empty, null, null, null, null);
        return review with { Application = WithFirmTerms(firm, review.Application) };
    }

    /// <summary>
    /// The application with the firm's terms for traders, one address that the shop and the application share. The
    /// application keeps its own only for a firm that has none.
    /// </summary>
    public static FirmApplication WithFirmTerms(Firm firm, FirmApplication application) =>
        firm.Payments.TermsUrl is { } terms ? application with { TermsUrl = terms.ToString() } : application;

    /// <summary>The deposit: what the firm paid, or else what it pays when it sends its application.</summary>
    public async Task<(decimal Amount, string Currency, bool Paid)> DepositAsync(Firm firm, CancellationToken cancellationToken)
    {
        var terms = billing.Terms;
        await using var connection = await store.OpenAsync(cancellationToken);
        var paid = await BillingStore.DepositPaidAsync(connection, firm.Id, terms.Currency, cancellationToken);
        return paid > 0 ? (paid, terms.Currency, true) : (terms.ReviewDeposit, terms.Currency, false);
    }

    /// <summary>Why the firm cannot send its application now. Null when it can.</summary>
    public static string? SubmitProblem(Firm firm, FirmReview review) =>
        EditProblem(firm, review)
        ?? (firm.Suspension is not null ? "The firm is suspended." : null)
        ?? ApplicationRules.Problem(review.Application, complete: true)?.Problem;

    /// <summary>Whether the firm's KYC is ready, which going live waits for, though our review does not (ADR 0042).</summary>
    public async Task<IdentityReadiness> IdentityReadinessAsync(string firmId, CancellationToken cancellationToken) =>
        IdentitySettings.ReadinessOf(await identities.GetSettingsAsync(firmId, cancellationToken));

    /// <summary>Saves the application as a draft. Only the fields that are filled in are checked.</summary>
    public async Task<ReviewResult> SaveApplicationAsync(Firm firm, FirmApplication application, string adminEmail, CancellationToken cancellationToken)
    {
        var normalized = ApplicationRules.Normalize(application);
        if (ApplicationRules.Problem(normalized, complete: false) is { } problem)
        {
            return new ReviewResult.Refused(StatusCodes.Status422UnprocessableEntity, problem.Problem, problem.Field);
        }

        var now = time.GetUtcNow();
        await using var connection = await store.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var review = await ReviewStore.LockAsync(connection, firm.Id, now, cancellationToken);
        if (EditProblem(firm, review) is { } refused)
        {
            return new ReviewResult.Refused(StatusCodes.Status409Conflict, refused);
        }

        await ReviewStore.SaveApplicationAsync(connection, firm.Id, normalized, now, cancellationToken);

        // The terms are the firm's, so saving them here changes them in the shop too.
        var termsChanged = normalized.TermsUrl != firm.Payments.TermsUrl?.ToString();
        if (termsChanged)
        {
            await FirmStore.SetTermsUrlAsync(connection, firm.Id, normalized.TermsUrl, now, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        if (termsChanged)
        {
            firms.Put(await firmStore.GetAsync(firm.Id, cancellationToken) ?? throw new InvalidOperationException($"Firm {firm.Id} disappeared."));
        }

        return new ReviewResult.Done();
    }

    /// <summary>
    /// Adds a document to the application, encrypted. Only PDF, PNG and JPEG, known from the content, up to
    /// <see cref="MaxDocumentBytes"/> each and <see cref="MaxDocuments"/> per firm.
    /// </summary>
    public async Task<(ReviewResult Result, FirmDocument? Document)> AddDocumentAsync(
        Firm firm,
        string? fileName,
        byte[] content,
        string adminEmail,
        CancellationToken cancellationToken)
    {
        if (content.Length == 0)
        {
            return (new ReviewResult.Refused(StatusCodes.Status422UnprocessableEntity, "The file is empty."), null);
        }

        if (content.Length > MaxDocumentBytes)
        {
            return (new ReviewResult.Refused(StatusCodes.Status413PayloadTooLarge, "A document can be at most 10 MB."), null);
        }

        if (UploadedFiles.ContentTypeOf(content) is not { } contentType)
        {
            return (new ReviewResult.Refused(StatusCodes.Status415UnsupportedMediaType, "Add a PDF, PNG or JPEG file."), null);
        }

        var now = time.GetUtcNow();
        var document = new FirmDocument(Guid.CreateVersion7(now), firm.Id, UploadedFiles.CleanFileName(fileName, "document"), contentType, content.Length, adminEmail, now);
        await using var connection = await store.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var review = await ReviewStore.LockAsync(connection, firm.Id, now, cancellationToken);
        if (EditProblem(firm, review) is { } refused)
        {
            return (new ReviewResult.Refused(StatusCodes.Status409Conflict, refused), null);
        }

        if ((await ReviewStore.ListDocumentsAsync(connection, firm.Id, cancellationToken)).Count >= MaxDocuments)
        {
            return (new ReviewResult.Refused(StatusCodes.Status409Conflict, $"Add at most {MaxDocuments} documents. Remove one to add another."), null);
        }

        await ReviewStore.InsertDocumentAsync(connection, document, SHA256.HashData(content), secrets.ProtectBytes(content, DocumentPurpose(document)), cancellationToken);
        await ReviewStore.AddEventAsync(connection, firm.Id, "document_added", adminEmail, Detail(("documentId", document.Id), ("fileName", document.FileName)), now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (new ReviewResult.Done(), document);
    }

    public async Task<ReviewResult> RemoveDocumentAsync(Firm firm, Guid documentId, string adminEmail, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await using var connection = await store.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var review = await ReviewStore.LockAsync(connection, firm.Id, now, cancellationToken);
        if (EditProblem(firm, review) is { } refused)
        {
            return new ReviewResult.Refused(StatusCodes.Status409Conflict, refused);
        }

        if (await ReviewStore.DeleteDocumentAsync(connection, firm.Id, documentId, cancellationToken) is not { } removed)
        {
            return new ReviewResult.Refused(StatusCodes.Status404NotFound, "The firm has no such document.");
        }

        await ReviewStore.AddEventAsync(connection, firm.Id, "document_removed", adminEmail, Detail(("documentId", removed.Id), ("fileName", removed.FileName)), now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ReviewResult.Done();
    }

    /// <summary>The firm's document, decrypted. Null when the firm has no such document.</summary>
    public async Task<(FirmDocument Document, byte[] Content)?> ReadDocumentAsync(string firmId, Guid documentId, CancellationToken cancellationToken) =>
        await store.GetDocumentAsync(firmId, documentId, cancellationToken) is { } found
            ? (found.Document, secrets.UnprotectBytes(found.ProtectedContent, DocumentPurpose(found.Document)))
            : null;

    /// <summary>
    /// Sends the application. The first time, the firm pays the deposit on a checkout page and the application is
    /// sent once it is paid. After we asked for changes, or without a deposit, it is sent at once.
    /// </summary>
    public async Task<ReviewResult> SubmitAsync(Firm firm, string adminEmail, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await using (var connection = await store.OpenAsync(cancellationToken))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var review = await ReviewStore.LockAsync(connection, firm.Id, now, cancellationToken);
            if ((EditProblem(firm, review) ?? (firm.Suspension is not null ? "The firm is suspended." : null)) is { } refused)
            {
                return new ReviewResult.Refused(StatusCodes.Status409Conflict, refused);
            }

            // Sent with the firm's terms as they are now, which we review.
            var application = WithFirmTerms(firm, review.Application);
            if (ApplicationRules.Problem(application, complete: true) is { } problem)
            {
                return new ReviewResult.Refused(StatusCodes.Status422UnprocessableEntity, problem.Problem, problem.Field);
            }

            if (application != review.Application)
            {
                await ReviewStore.SaveApplicationAsync(connection, firm.Id, application, now, cancellationToken);
            }

            var depositDue = review.Status == ReviewStatus.Draft
                && billing.Terms.ReviewDeposit > 0
                && await BillingStore.DepositPaidAsync(connection, firm.Id, billing.Terms.Currency, cancellationToken) == 0;
            if (!depositDue)
            {
                await ReviewStore.SubmitAsync(connection, firm.Id, now, cancellationToken);
                await ReviewStore.AddEventAsync(connection, firm.Id, "submitted", adminEmail, ReviewStore.Snapshot(application), now, cancellationToken);
                var goLiveUrl = new Uri(firm.Portal.Url, "admin/go-live");
                foreach (var admin in await FirmAdmins.EmailsAsync(connection, firm.Id, cancellationToken))
                {
                    await EmailOutbox.AddAsync(
                        connection, PlatformEmails.ApplicationReceived(platform.Value.Name, firm.Name, admin, null, goLiveUrl), "application_received", firm.Id, now, cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                signals.Emails.Set();
                await staff.ApplicationSubmittedAsync(firm, cancellationToken);
                return new ReviewResult.Done();
            }

            await transaction.CommitAsync(cancellationToken);
        }

        return await billing.StartDepositAsync(firm, adminEmail, cancellationToken) switch
        {
            BillingResult.Checkout checkout => new ReviewResult.Checkout(checkout.Url),
            BillingResult.Refused refused => new ReviewResult.Refused(refused.StatusCode, refused.Problem),
            _ => throw new InvalidOperationException("A checkout page was expected for the deposit."),
        };
    }

    /// <summary>Approves an application that waits for us. The firm can go live by paying, once its KYC is ready.</summary>
    public Task<ReviewResult> ApproveAsync(string firmId, string staffEmail, string? message, CancellationToken cancellationToken) =>
        DecideAsync(
            firmId,
            staffEmail,
            Clean(message),
            ReviewStatus.Approved,
            "approved",
            review => review.Status == ReviewStatus.Submitted ? null : "Only an application that waits for review can be approved.",
            (firm, to, text) => PlatformEmails.ApplicationApproved(platform.Value.Name, firm.Name, to, text, new Uri(firm.Portal.Url, "admin/go-live")),
            messageRequired: false,
            cancellationToken);

    /// <summary>Asks the firm to change its application. It sends it again without a new deposit.</summary>
    public Task<ReviewResult> RequestChangesAsync(string firmId, string staffEmail, string? message, CancellationToken cancellationToken) =>
        DecideAsync(
            firmId,
            staffEmail,
            Clean(message),
            ReviewStatus.ChangesRequested,
            "changes_requested",
            review => review.Status == ReviewStatus.Submitted ? null : "Only an application that waits for review can get a request for changes.",
            (firm, to, text) => PlatformEmails.ChangesRequested(platform.Value.Name, firm.Name, to, text!, new Uri(firm.Portal.Url, "admin/go-live")),
            messageRequired: true,
            cancellationToken);

    /// <summary>Rejects the firm for good. It cannot go live, and the deposit is not paid back.</summary>
    public Task<ReviewResult> RejectAsync(string firmId, string staffEmail, string? message, CancellationToken cancellationToken) =>
        DecideAsync(
            firmId,
            staffEmail,
            Clean(message),
            ReviewStatus.Rejected,
            "rejected",
            review => review.Status is ReviewStatus.Submitted or ReviewStatus.ChangesRequested ? null : "Only an application that was sent can be rejected.",
            (firm, to, text) => PlatformEmails.ApplicationRejected(platform.Value.Name, firm.Name, to, text!),
            messageRequired: true,
            cancellationToken);

    /// <summary>
    /// Ticks or unticks one of our checks in the firm's review (ADR 0024), while its application waits for us or for its
    /// changes. Answers with the checks that are ticked.
    /// </summary>
    public async Task<(ReviewResult Result, IReadOnlyList<ReviewCheck> Checks)> SetCheckAsync(
        string firmId,
        string item,
        bool done,
        string staffEmail,
        CancellationToken cancellationToken)
    {
        if (firms.ById(firmId) is null)
        {
            return (UnknownFirm(), []);
        }

        if (!ReviewChecks.All.Contains(item, StringComparer.Ordinal))
        {
            return (new ReviewResult.Refused(StatusCodes.Status404NotFound, "There is no such check."), []);
        }

        await using var connection = await store.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (await ReviewStore.GetAsync(connection, firmId, forUpdate: true, cancellationToken) is not { Status: ReviewStatus.Submitted or ReviewStatus.ChangesRequested })
        {
            return (new ReviewResult.Refused(StatusCodes.Status409Conflict, "Checks are made while an application waits for review or for its changes."), []);
        }

        await ReviewStore.SetCheckAsync(connection, firmId, item, done ? staffEmail : null, time.GetUtcNow(), cancellationToken);
        var checks = await ReviewStore.ListChecksAsync(connection, firmId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (new ReviewResult.Done(), checks);
    }

    /// <summary>
    /// Suspends the firm: no challenge can start, its shop closes and its challenges are paused until we lift it.
    /// The firm's slots are locked while it is saved, so a challenge starting at the same time is paused too.
    /// </summary>
    public async Task<ReviewResult> SuspendAsync(string firmId, string staffEmail, string? reason, CancellationToken cancellationToken)
    {
        if (firms.ById(firmId) is null)
        {
            return UnknownFirm();
        }

        var text = Clean(reason);
        if (MessageProblem(text, required: true) is { } invalid)
        {
            return new ReviewResult.Refused(StatusCodes.Status422UnprocessableEntity, invalid, "reason");
        }

        return await SetSuspensionAsync(firmId, staffEmail, text, cancellationToken);
    }

    /// <summary>Lifts the firm's suspension. Its challenges go on unless its month is unpaid.</summary>
    public Task<ReviewResult> UnsuspendAsync(string firmId, string staffEmail, CancellationToken cancellationToken) =>
        firms.ById(firmId) is null ? Task.FromResult<ReviewResult>(UnknownFirm()) : SetSuspensionAsync(firmId, staffEmail, null, cancellationToken);

    // Why the firm cannot change its application now. Null when it can.
    private static string? EditProblem(Firm firm, FirmReview review) =>
        firm.Status == FirmStatus.Live ? "The firm is already live."
        : review.Status switch
        {
            ReviewStatus.Submitted => "We are reviewing your application, so it cannot be changed now.",
            ReviewStatus.Approved => "Your application is approved.",
            ReviewStatus.Rejected => "Your application was not approved.",
            _ => null,
        };

    private static string? MessageProblem(string? message, bool required) =>
        message is null
            ? required ? "Write a message to the firm." : null
            : message.Length > MaxMessageLength ? FormattableString.Invariant($"Write at most {MaxMessageLength:N0} characters.") : null;

    private async Task<ReviewResult> DecideAsync(
        string firmId,
        string staffEmail,
        string? message,
        ReviewStatus decision,
        string eventType,
        Func<FirmReview, string?> refusal,
        Func<Firm, string, string?, EmailMessage> emailOf,
        bool messageRequired,
        CancellationToken cancellationToken)
    {
        if (firms.ById(firmId) is not { } firm)
        {
            return UnknownFirm();
        }

        if (MessageProblem(message, messageRequired) is { } invalid)
        {
            return new ReviewResult.Refused(StatusCodes.Status422UnprocessableEntity, invalid, "message");
        }

        var now = time.GetUtcNow();
        await using (var connection = await store.OpenAsync(cancellationToken))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var review = await ReviewStore.LockAsync(connection, firmId, now, cancellationToken);
            if (refusal(review) is { } refused)
            {
                return new ReviewResult.Refused(StatusCodes.Status409Conflict, refused);
            }

            // The checks that were ticked are kept with the decision.
            var checks = (await ReviewStore.ListChecksAsync(connection, firmId, cancellationToken)).Select(c => c.Item).Order(StringComparer.Ordinal).ToArray();
            await ReviewStore.DecideAsync(connection, firmId, decision, message, staffEmail, now, cancellationToken);
            await ReviewStore.AddEventAsync(connection, firmId, eventType, staffEmail, Detail(("message", message), ("checks", checks)), now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        LogDecided(logger, firmId, decision, staffEmail);
        await EmailAdminsAsync(firm, to => emailOf(firm, to, message), cancellationToken);
        return new ReviewResult.Done();
    }

    private async Task<ReviewResult> SetSuspensionAsync(string firmId, string staffEmail, string? reason, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await using (var connection = await store.OpenAsync(cancellationToken))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await SlotService.LockAsync(connection, firmId, cancellationToken);
            var suspended = (await BillingStore.GoLiveStateAsync(connection, firmId, forUpdate: true, cancellationToken)).Suspended;
            if (suspended == reason is not null)
            {
                return new ReviewResult.Refused(StatusCodes.Status409Conflict, suspended ? "The firm is already suspended." : "The firm is not suspended.");
            }

            await FirmStore.SetSuspensionAsync(connection, firmId, reason is null ? null : new FirmSuspension(now, reason), now, cancellationToken);
            await ReviewStore.AddEventAsync(connection, firmId, reason is null ? "suspension_lifted" : "suspended", staffEmail, reason is null ? null : Detail(("reason", reason)), now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var firm = await firmStore.GetAsync(firmId, cancellationToken) ?? throw new InvalidOperationException($"Firm {firmId} disappeared.");
        firms.Put(firm);
        LogSuspension(logger, firmId, reason is not null, staffEmail);
        await billing.KeepChallengesStandingAsync(firm, cancellationToken);
        var adminUrl = new Uri(firm.Portal.Url, "admin");
        await EmailAdminsAsync(
            firm,
            to => reason is null
                ? PlatformEmails.SuspensionLifted(platform.Value.Name, firm.Name, to, adminUrl)
                : PlatformEmails.FirmSuspended(platform.Value.Name, firm.Name, to, reason, adminUrl),
            cancellationToken);
        return new ReviewResult.Done();
    }

    // Best effort: the admin panel shows the same, and a failed email is only logged.
    private async Task EmailAdminsAsync(Firm firm, Func<string, EmailMessage> message, CancellationToken cancellationToken)
    {
        foreach (var admin in await admins.ListAsync(firm.Id, cancellationToken))
        {
            try
            {
                await email.SendAsync(message(admin.Email), cancellationToken);
            }
            catch (EmailNotSentException exception)
            {
                LogEmailNotSent(logger, exception, firm.Id);
            }
        }
    }

    // Ties the encrypted content to its firm and document, so it cannot be moved to another.
    private static string DocumentPurpose(FirmDocument document) => $"firm:{document.FirmId}:document:{document.Id}";

    private static ReviewResult.Refused UnknownFirm() => new(StatusCodes.Status404NotFound, "There is no such firm.");

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string Detail(params (string Key, object? Value)[] values) =>
        JsonSerializer.Serialize(values.ToDictionary(v => v.Key, v => v.Value), PropJson.Options);

    [LoggerMessage(Level = LogLevel.Information, Message = "The review of firm {FirmId} is now {Decision}, decided by {Staff}")]
    private static partial void LogDecided(ILogger logger, string firmId, ReviewStatus decision, string staff);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Firm {FirmId} suspended: {Suspended}, by {Staff}")]
    private static partial void LogSuspension(ILogger logger, string firmId, bool suspended, string staff);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A review email to an administrator of firm {FirmId} could not be sent")]
    private static partial void LogEmailNotSent(ILogger logger, Exception exception, string firmId);
}
