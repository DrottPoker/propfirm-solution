using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

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
        return app;
    }

    private static Ok<PlatformResponse> GetPlatform(IOptions<PlatformOptions> platform, IOptions<SignupOptions> signup, IOptions<LoginOptions> login) =>
        TypedResults.Ok(new PlatformResponse(
            platform.Value.Name,
            signup.Value.TermsVersion,
            signup.Value.TermsUrl,
            signup.Value.DpaUrl,
            platform.Value.FirmPortalUrl,
            login.Value.MinimumPasswordLength,
            signup.Value.RequireEmailVerification));

    /// <summary>Whether the short name can be chosen, checked while it is typed. Does not ask the trading platform.</summary>
    private static async Task<Ok<AvailabilityResponse>> CheckAsync(string? firmId, SignupService signups, CancellationToken cancellationToken)
    {
        var availability = await signups.CheckAsync(firmId, null, askTradingPlatform: false, cancellationToken);
        return TypedResults.Ok(new AvailabilityResponse(firmId ?? "", availability.Available, availability.Reason));
    }

    /// <summary>
    /// Signs a firm up. With email confirmation, 202 and an email with a link. Without, 200 and a link that logs
    /// the administrator in on the firm's portal.
    /// </summary>
    private static async Task<Results<Ok<SignupResponse>, Accepted<SignupResponse>, ProblemHttpResult>> SignUpAsync(
        SignupRequest request,
        SignupService signups,
        CancellationToken cancellationToken)
    {
        var outcome = await signups.SignUpAsync(request.FirmName, request.FirmId, request.Email, request.Password, request.AcceptTerms, cancellationToken);
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
/// The platform, for the sign-up page: its name, the terms firms accept, the address new portals get
/// (<paramref name="FirmPortalUrl"/> with {firm} for the short name), and whether the email is confirmed first.
/// </summary>
public sealed record PlatformResponse(
    string Name,
    string TermsVersion,
    Uri? TermsUrl,
    Uri? DpaUrl,
    string FirmPortalUrl,
    int MinimumPasswordLength,
    bool EmailVerification);

public sealed record AvailabilityResponse(string FirmId, bool Available, string? Reason);

/// <summary><paramref name="FirmId"/> is the short name: the portal's subdomain and the server on the trading platform.</summary>
public sealed record SignupRequest(string? FirmName, string? FirmId, string? Email, string? Password, bool AcceptTerms);

/// <summary>Either the email with the confirmation link is sent, or the firm is created and <paramref name="AdminUrl"/> logs its administrator in.</summary>
public sealed record SignupResponse(bool VerificationRequired, string FirmId, Uri? AdminUrl);

public sealed record VerifySignupRequest(string? Token);

/// <summary>Open <paramref name="AdminUrl"/> once to be logged in to the new firm's admin panel.</summary>
public sealed record VerifySignupResponse(string FirmId, Uri AdminUrl);
