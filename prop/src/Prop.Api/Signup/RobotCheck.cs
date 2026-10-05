using System.Net.Http.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Options;

namespace Prop.Api.Signup;

/// <summary>The robot check on sign-up, Cloudflare Turnstile (ADR 0045). Off without keys, which only development may lack.</summary>
public sealed class RobotCheckOptions
{
    public const string SectionName = "RobotCheck";

    /// <summary>The public key the sign-up page shows the check with.</summary>
    public string SiteKey { get; init; } = "";

    /// <summary>The secret key the answer is checked with. Never shown.</summary>
    public string SecretKey { get; init; } = "";

    /// <summary>Where answers are checked.</summary>
    public Uri VerifyUrl { get; init; } = new("https://challenges.cloudflare.com/turnstile/v0/siteverify");

    public bool Enabled => SecretKey.Length > 0;
}

internal enum RobotCheckResult
{
    Passed,
    Failed,

    /// <summary>The check could not be asked, so whether it passed is not known.</summary>
    Unavailable,
}

/// <summary>Checks the answer the sign-up page got from Turnstile, so scripts cannot sign firms up in bulk.</summary>
internal sealed partial class RobotCheck(IHttpClientFactory clients, IOptions<RobotCheckOptions> options, ILogger<RobotCheck> logger)
{
    public const string HttpClientName = "robot-check";

    public async Task<RobotCheckResult> VerifyAsync(string? answer, string? remoteAddress, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return RobotCheckResult.Passed;
        }

        if (string.IsNullOrWhiteSpace(answer) || answer.Length > 2_048)
        {
            return RobotCheckResult.Failed;
        }

        List<KeyValuePair<string, string>> form = [new("secret", settings.SecretKey), new("response", answer)];
        if (remoteAddress is not null)
        {
            form.Add(new("remoteip", remoteAddress));
        }

        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await clients.CreateClient(HttpClientName).PostAsync(settings.VerifyUrl, content, cancellationToken);
            response.EnsureSuccessStatusCode();
            var verdict = await response.Content.ReadFromJsonAsync<Verdict>(cancellationToken);
            return verdict?.Success == true ? RobotCheckResult.Passed : RobotCheckResult.Failed;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !cancellationToken.IsCancellationRequested)
        {
            LogUnavailable(logger, exception);
            return RobotCheckResult.Unavailable;
        }
    }

    private sealed record Verdict([property: JsonPropertyName("success")] bool Success);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The robot check could not be asked; the sign-up is refused for now")]
    private static partial void LogUnavailable(ILogger logger, Exception exception);
}
