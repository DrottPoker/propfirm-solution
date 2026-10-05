using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Prop.Api.Billing;
using Prop.Api.Challenges;
using Prop.Api.Configuration;
using Prop.Api.Portal;

namespace Prop.Api.Signup;

/// <summary>
/// Sign-up for firms, on the platform's own address (ADR 0017). Under /api/portal so the portal passes it on, but
/// only on the platform's host: a firm's portal never shows it.
/// </summary>
internal static class SignupEndpoints
{
    public static IEndpointRouteBuilder MapSignupApi(this IEndpointRouteBuilder app)
    {
        var platform = app.MapGroup("/api/portal").WithTags("Signup").AddEndpointFilter<PlatformHostFilter>();
        platform.MapGet("/platform", GetPlatform);
        platform.MapGet("/signup/availability", CheckAsync);
        platform.MapPost("/signup", SignUpAsync).RequireRateLimiting(PortalAuth.LoginRateLimit);
        platform.MapPost("/signup/verify", VerifyAsync).RequireRateLimiting(PortalAuth.LoginRateLimit);
        platform.MapPost("/login-help", LoginHelpAsync).RequireRateLimiting(PortalAuth.LoginRateLimit);
        return app;
    }

    private static Ok<PlatformResponse> GetPlatform(
        IOptions<PlatformOptions> platform,
        IOptions<SignupOptions> signup,
        IOptions<LoginOptions> login,
        IOptions<SandboxOptions> sandbox,
        IOptions<RobotCheckOptions> robots,
        BillingService billing,
        IOptions<BillingOptions> billingOptions) =>
        TypedResults.Ok(new PlatformResponse(
            platform.Value.Name,
            signup.Value.TermsVersion,
            signup.Value.TermsUrl,
            signup.Value.DpaUrl,
            platform.Value.FirmPortalUrl,
            login.Value.MinimumPasswordLength,
            signup.Value.RequireEmailVerification,
            BillingEndpoints.PricesOf(billing.Terms, billingOptions.Value),
            sandbox.Value.MaxOpenAccounts,
            signup.Value.Currencies.Count > 0 ? signup.Value.Currencies : [signup.Value.DefaultCurrency],
            robots.Value.Enabled ? robots.Value.SiteKey : null));

    /// <summary>
    /// Emails a one-time login link to each firm the email administers, for an administrator who does not remember the firm's
    /// address. Always 202, so the answer never tells who has a firm.
    /// </summary>
    private static async Task<Accepted> LoginHelpAsync(
        PasswordResetRequest request,
        FirmAdmins admins,
        IOptions<PlatformOptions> platform,
        WorkSignals signals,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            await admins.SendLoginHelpAsync(request.Email.Trim(), platform.Value.Name, signals, time.GetUtcNow(), cancellationToken);
        }

        return TypedResults.Accepted((string?)null);
    }

    /// <summary>Whether the short name can be chosen, checked while it is typed. Does not ask the trading platform.</summary>
    private static async Task<Ok<AvailabilityResponse>> CheckAsync(string? firmId, SignupService signups, CancellationToken cancellationToken)
    {
        var availability = await signups.CheckAsync(firmId, null, askTradingPlatform: false, cancellationToken);
        return TypedResults.Ok(new AvailabilityResponse(firmId ?? "", availability.Available, availability.Reason, availability.Suggestions ?? []));
    }

    /// <summary>
    /// Signs a firm up. With email confirmation, 202 and an email with a link. Without, 200 and a link that logs
    /// the administrator in on the firm's portal. The robot check comes first (ADR 0045).
    /// </summary>
    private static async Task<Results<Ok<SignupResponse>, Accepted<SignupResponse>, ProblemHttpResult>> SignUpAsync(
        SignupRequest request,
        HttpContext context,
        SignupService signups,
        RobotCheck robots,
        CancellationToken cancellationToken)
    {
        switch (await robots.VerifyAsync(request.RobotCheck, context.Connection.RemoteIpAddress?.ToString(), cancellationToken))
        {
            case RobotCheckResult.Failed:
                return ProblemOf(new SignupOutcome.Invalid("robotCheck", "Show that you are not a robot, then sign up again."));
            case RobotCheckResult.Unavailable:
                return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "We could not check that you are not a robot. Try again shortly.");
        }

        var outcome = await signups.SignUpAsync(request.FirmName, request.FirmId, request.Email, request.Password, request.AcceptTerms, request.Currency, cancellationToken);
        return outcome switch
        {
            SignupOutcome.VerificationSent => TypedResults.Accepted((string?)null, new SignupResponse(true, request.FirmId!, null)),
            SignupOutcome.Completed completed => TypedResults.Ok(new SignupResponse(false, completed.FirmId, completed.AdminUrl)),
            _ => ProblemOf(outcome),
        };
    }

    /// <summary>Confirms the email address and creates the firm. Answers with a link that logs the administrator in on the firm's portal.</summary>
    private static async Task<Results<Ok<VerifySignupResponse>, ProblemHttpResult>> VerifyAsync(
        VerifySignupRequest request,
        SignupService signups,
        CancellationToken cancellationToken)
    {
        var outcome = await signups.VerifyAsync(request.Token, cancellationToken);
        return outcome is SignupOutcome.Completed completed
            ? TypedResults.Ok(new VerifySignupResponse(completed.FirmId, completed.AdminUrl))
            : ProblemOf(outcome);
    }

    private static ProblemHttpResult ProblemOf(SignupOutcome outcome) => outcome switch
    {
        SignupOutcome.Invalid { Field: "token" } invalid => TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: invalid.Problem),
        SignupOutcome.Invalid invalid => TypedResults.Problem(
            statusCode: StatusCodes.Status422UnprocessableEntity,
            title: invalid.Problem,
            extensions: new Dictionary<string, object?> { ["field"] = invalid.Field }),
        SignupOutcome.Taken taken => TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: taken.Problem,
            extensions: new Dictionary<string, object?> { ["field"] = "firmId" }),
        SignupOutcome.EmailNotSent => TypedResults.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "The confirmation email could not be sent. Try again shortly."),
        SignupOutcome.TooManyEmails => TypedResults.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "We have sent this email address as many confirmation emails as a day allows. Use the link in the latest one, or try again tomorrow.",
            extensions: new Dictionary<string, object?> { ["field"] = "email" }),
        _ => throw new InvalidOperationException($"Unexpected outcome {outcome.GetType().Name}."),
    };
}

/// <summary>The platform's own requests come only on its own host. Elsewhere they look like they do not exist.</summary>
internal sealed class PlatformHostFilter(IOptions<PlatformOptions> platform) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!string.Equals(PortalAuth.HostOf(context.HttpContext), platform.Value.Url?.Host, StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Sign-up is not at this address.");
        }

        return await next(context);
    }
}

/// <summary>
/// The platform, for its front page and the sign-up page: its name, the terms firms accept, the address new portals get
/// (<paramref name="FirmPortalUrl"/> with {firm} for the short name), whether the email is confirmed first, what firms
/// pay when they go live, how many test accounts the sandbox has room for and the account currencies a firm may
/// choose, the first by default. <paramref name="TermsVersion"/> is recorded with the firm, not shown.
/// <paramref name="RobotCheckSiteKey"/> shows the robot check on the sign-up page, and is null while it is off.
/// </summary>
public sealed record PlatformResponse(
    string Name,
    string TermsVersion,
    Uri? TermsUrl,
    Uri? DpaUrl,
    string FirmPortalUrl,
    int MinimumPasswordLength,
    bool EmailVerification,
    PricesResponse Prices,
    int SandboxMaxOpenAccounts,
    IReadOnlyList<string> Currencies,
    string? RobotCheckSiteKey);

/// <summary>Whether the short name can be chosen, and when it is taken or reserved up to three free names like it.</summary>
public sealed record AvailabilityResponse(string FirmId, bool Available, string? Reason, IReadOnlyList<string> Suggestions);

/// <summary>
/// <paramref name="FirmId"/> is the short name: the portal's subdomain and the server on the trading platform.
/// <paramref name="Currency"/> is the currency of the firm's accounts, one of the platform's, or its first when left out.
/// </summary>
public sealed record SignupRequest(string? FirmName, string? FirmId, string? Email, string? Password, bool AcceptTerms, string? Currency = null, string? RobotCheck = null);

/// <summary>Either the email with the confirmation link is sent, or the firm is created and <paramref name="AdminUrl"/> logs its administrator in.</summary>
public sealed record SignupResponse(bool VerificationRequired, string FirmId, Uri? AdminUrl);

public sealed record VerifySignupRequest(string? Token);

/// <summary>Open <paramref name="AdminUrl"/> once to be logged in to the new firm's admin panel.</summary>
public sealed record VerifySignupResponse(string FirmId, Uri AdminUrl);
