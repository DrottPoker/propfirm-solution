using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Options;

using Prop.Api.Configuration;

namespace Prop.Api.Identity;

/// <summary>
/// A check to start: our own id for it, the trader, the extra checks, where the trader comes back to in the portal, and
/// the portal's address.
/// </summary>
internal sealed record IdentityCheckRequest(Guid SessionId, Guid TraderId, bool CheckAddress, bool CheckSanctions, Uri ReturnUrl, Uri PortalUrl);

/// <summary>A service that checks IDs: it starts a check on its own page and tells what it decided.</summary>
internal interface IIdentityChecker
{
    IdentityProvider Provider { get; }

    /// <summary>The provider's id for the check and the page where the trader does it. Throws <see cref="IdentityCheckNotStartedException"/>.</summary>
    Task<(string ProviderSessionId, Uri Url)> StartAsync(IdentityCheckRequest request, CancellationToken cancellationToken);

    /// <summary>What the provider has decided so far, or null when it cannot tell now.</summary>
    Task<IdentityDecision?> DecisionAsync(string providerSessionId, CancellationToken cancellationToken);
}

/// <summary>The check could not be started. The message is safe to show the trader.</summary>
internal sealed class IdentityCheckNotStartedException : Exception
{
    public IdentityCheckNotStartedException()
    {
    }

    public IdentityCheckNotStartedException(string message)
        : base(message)
    {
    }

    public IdentityCheckNotStartedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Didit's hosted check (ADR 0042): a session with the workflow for the checks the firm chose, on Didit's page, which
/// sends the trader back to the portal. The document and the pictures stay with Didit; we read only the decision.
/// </summary>
internal sealed class DiditChecker(IHttpClientFactory httpClients, IOptions<IdentityCheckOptions> options) : IIdentityChecker
{
    public const string HttpClientName = "Didit";

    public IdentityProvider Provider => IdentityProvider.Didit;

    public async Task<(string ProviderSessionId, Uri Url)> StartAsync(IdentityCheckRequest request, CancellationToken cancellationToken)
    {
        var didit = options.Value.Didit;
        var workflow = (request.CheckAddress, request.CheckSanctions) switch
        {
            (true, true) => didit.WorkflowWithAddressAndSanctions,
            (true, false) => didit.WorkflowWithAddress,
            (false, true) => didit.WorkflowWithSanctions,
            _ => didit.Workflow,
        };
        if (workflow.Length == 0 || didit.ApiKey.Length == 0)
        {
            throw new IdentityCheckNotStartedException("ID checks are not set up yet. Try again later.");
        }

        using var message = Request(HttpMethod.Post, "v3/session/");
        message.Content = JsonContent.Create(new
        {
            workflow_id = workflow,
            vendor_data = request.TraderId.ToString(),
            callback = request.ReturnUrl.ToString(),
            metadata = new { session = request.SessionId },
            language = "en",
        });
        try
        {
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(message, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new IdentityCheckNotStartedException("The ID check could not be started. Try again shortly.");
            }

            using var session = JsonDocument.Parse(body);
            var root = session.RootElement;
            return (root.GetProperty("session_id").GetString()!, new Uri(root.GetProperty("url").GetString()!));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or UriFormatException && !cancellationToken.IsCancellationRequested)
        {
            throw new IdentityCheckNotStartedException("The ID check could not be started. Try again shortly.", exception);
        }
    }

    public async Task<IdentityDecision?> DecisionAsync(string providerSessionId, CancellationToken cancellationToken)
    {
        using var message = Request(HttpMethod.Get, $"v3/session/{Uri.EscapeDataString(providerSessionId)}/decision/");
        try
        {
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var decision = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return DecisionOf(decision.RootElement);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>A session's decision as Didit sends it, in its own words.</summary>
    public static IdentityDecision DecisionOf(JsonElement decision)
    {
        var status = StatusOf(decision.TryGetProperty("status", out var s) ? s.GetString() : null);
        var document = First(decision, "id_verifications");
        var name = Text(document, "full_name")
            ?? (string.Join(' ', new[] { Text(document, "first_name"), Text(document, "last_name") }.OfType<string>()) is { Length: > 0 } joined ? joined : null);
        var born = Text(document, "date_of_birth") is { } dob && DateOnly.TryParseExact(dob, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : (DateOnly?)null;
        return new IdentityDecision(
            status,
            name,
            born,
            Text(document, "issuing_state_name") ?? Text(document, "issuing_state"),
            Text(First(decision, "poa_verifications"), "status") == "Approved",
            Text(First(decision, "aml_screenings"), "status") == "Approved",
            status == IdentityStatus.Declined ? ReasonOf(decision) : null);
    }

    /// <summary>Didit's session status as ours. Checks that were given up or ran out can be started again.</summary>
    public static IdentityStatus StatusOf(string? status) => status switch
    {
        "Approved" => IdentityStatus.Approved,
        "Declined" => IdentityStatus.Declined,
        "In Review" => IdentityStatus.InReview,
        "Abandoned" or "Expired" or "Kyc Expired" => IdentityStatus.Expired,
        _ => IdentityStatus.Pending,
    };

    // The short descriptions of the warnings on every check, in order and once each.
    private static string ReasonOf(JsonElement decision)
    {
        var reasons = new List<string>();
        foreach (var feature in new[] { "id_verifications", "liveness_checks", "face_matches", "poa_verifications", "aml_screenings" })
        {
            if (!decision.TryGetProperty(feature, out var checks) || checks.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var check in checks.EnumerateArray())
            {
                if (check.TryGetProperty("warnings", out var warnings) && warnings.ValueKind == JsonValueKind.Array)
                {
                    reasons.AddRange(warnings.EnumerateArray().Select(w => Text(w, "short_description")).OfType<string>());
                }
            }
        }

        var reason = string.Join(" ", reasons.Distinct(StringComparer.Ordinal).Select(r => r.TrimEnd('.') + "."));
        return reason.Length == 0 ? "The check did not pass." : reason.Length <= 500 ? reason : string.Concat(reason.AsSpan(0, 497), "...");
    }

    private static JsonElement First(JsonElement decision, string feature) =>
        decision.ValueKind == JsonValueKind.Object && decision.TryGetProperty(feature, out var checks) && checks.ValueKind == JsonValueKind.Array && checks.GetArrayLength() > 0
            ? checks[0]
            : default;

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text.Trim()
            : null;

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Add("x-api-key", options.Value.Didit.ApiKey);
        return request;
    }
}

/// <summary>Didit signs each webhook's body with the webhook secret, as HMAC-SHA256 in hex in the X-Signature header.</summary>
internal static class DiditSignature
{
    public const string Header = "X-Signature";

    public static string Of(string secret, ReadOnlySpan<byte> body) => Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));

    public static bool IsValid(string secret, string? signature, ReadOnlySpan<byte> body) =>
        secret.Length > 0
        && signature is { Length: 64 }
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Of(secret, body)), Encoding.ASCII.GetBytes(signature.ToLowerInvariant()));
}

/// <summary>
/// Checks without a real check, for firms in the sandbox and for development: the portal's test page approves or
/// declines, and the outcome is saved at once.
/// </summary>
internal sealed class TestChecker : IIdentityChecker
{
    public IdentityProvider Provider => IdentityProvider.Test;

    public Task<(string ProviderSessionId, Uri Url)> StartAsync(IdentityCheckRequest request, CancellationToken cancellationToken) =>
        Task.FromResult((request.SessionId.ToString(), new Uri(request.PortalUrl, $"identity/test?session={request.SessionId}")));

    public Task<IdentityDecision?> DecisionAsync(string providerSessionId, CancellationToken cancellationToken) => Task.FromResult<IdentityDecision?>(null);
}
