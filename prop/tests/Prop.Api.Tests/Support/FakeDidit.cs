using System.Net;
using System.Text;
using System.Text.Json.Nodes;

using Prop.Api.Identity;

namespace Prop.Api.Tests.Support;

/// <summary>
/// Didit's API as far as the service uses it: sessions are created with a workflow and remembered, and their decisions
/// are what the test says. Can act as an API that is down. Also signs the webhooks Didit would send.
/// </summary>
internal sealed class FakeDidit : HttpMessageHandler
{
    public const string ApiKey = "didit-test-api-key";

    public const string WebhookSecret = "didit-test-webhook-secret";

    public const string Workflow = "workflow-id";

    public const string WorkflowWithAddress = "workflow-id-address";

    public const string WorkflowWithSanctions = "workflow-id-sanctions";

    public const string WorkflowWithAddressAndSanctions = "workflow-id-address-sanctions";

    private readonly Lock _lock = new();
    private readonly List<JsonObject> _created = [];
    private readonly Dictionary<string, string> _decisions = new(StringComparer.Ordinal);
    private bool _down;

    /// <summary>The sessions asked for, in order, as the service sent them.</summary>
    public IReadOnlyList<JsonObject> Created
    {
        get
        {
            lock (_lock)
            {
                return [.. _created];
            }
        }
    }

    /// <summary>The service's settings for Didit with this fake.</summary>
    public static Dictionary<string, string> Settings() => new()
    {
        ["Identity:Provider"] = "Didit",
        ["Identity:Didit:ApiUrl"] = "https://didit.test/",
        ["Identity:Didit:ApiKey"] = ApiKey,
        ["Identity:Didit:WebhookSecret"] = WebhookSecret,
        ["Identity:Didit:Workflow"] = Workflow,
        ["Identity:Didit:WorkflowWithAddress"] = WorkflowWithAddress,
        ["Identity:Didit:WorkflowWithSanctions"] = WorkflowWithSanctions,
        ["Identity:Didit:WorkflowWithAddressAndSanctions"] = WorkflowWithAddressAndSanctions,
    };

    /// <summary>What Didit says about the session from now on, as its decision JSON.</summary>
    public void Decide(string sessionId, JsonObject decision)
    {
        lock (_lock)
        {
            _decisions[sessionId] = decision.ToJsonString();
        }
    }

    /// <summary>Whether the API answers with a server error.</summary>
    public void SetDown(bool down)
    {
        lock (_lock)
        {
            _down = down;
        }
    }

    /// <summary>An approved decision, with the document's name, date of birth and country, and the screening when there was one.</summary>
    public static JsonObject Approved(string sessionId, string firstName, string lastName, string dateOfBirth, string country, bool screened = false) =>
        new()
        {
            ["session_id"] = sessionId,
            ["status"] = "Approved",
            ["id_verifications"] = new JsonArray(new JsonObject
            {
                ["status"] = "Approved",
                ["first_name"] = firstName,
                ["last_name"] = lastName,
                ["date_of_birth"] = dateOfBirth,
                ["issuing_state"] = "SWE",
                ["issuing_state_name"] = country,
                ["warnings"] = new JsonArray(),
            }),
            ["aml_screenings"] = screened ? new JsonArray(new JsonObject { ["status"] = "Approved", ["total_hits"] = 0 }) : new JsonArray(),
        };

    /// <summary>A declined decision, with Didit's warnings.</summary>
    public static JsonObject Declined(string sessionId, params string[] warnings) =>
        new()
        {
            ["session_id"] = sessionId,
            ["status"] = "Declined",
            ["id_verifications"] = new JsonArray(new JsonObject
            {
                ["status"] = "Declined",
                ["warnings"] = new JsonArray([.. warnings.Select(w => (JsonNode)new JsonObject { ["risk"] = "DOCUMENT", ["short_description"] = w })]),
            }),
        };

    /// <summary>A webhook as Didit sends it about the session, signed with the secret unless told otherwise.</summary>
    public static HttpRequestMessage Webhook(string sessionId, string status, string secret = WebhookSecret)
    {
        var body = new JsonObject
        {
            ["event_id"] = Guid.NewGuid(),
            ["webhook_type"] = "status.updated",
            ["session_id"] = sessionId,
            ["status"] = status,
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        }.ToJsonString();
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/identity/v1/didit", UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(DiditSignature.Header, DiditSignature.Of(secret, Encoding.UTF8.GetBytes(body)));
        return request;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!request.Headers.TryGetValues("x-api-key", out var keys) || keys.Single() != ApiKey)
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_lock)
        {
            if (_down)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            if (request.Method == HttpMethod.Post && path == "/v3/session/")
            {
                var created = JsonNode.Parse(body!)!.AsObject();
                _created.Add(created);
                var id = $"didit-session-{_created.Count}";
                return Json(HttpStatusCode.Created, new JsonObject
                {
                    ["session_id"] = id,
                    ["session_number"] = _created.Count,
                    ["url"] = $"https://verify.didit.test/session/{id}",
                    ["status"] = "Not Started",
                    ["vendor_data"] = created["vendor_data"]?.GetValue<string>(),
                    ["workflow_id"] = created["workflow_id"]?.GetValue<string>(),
                });
            }

            if (request.Method == HttpMethod.Get && path.StartsWith("/v3/session/", StringComparison.Ordinal) && path.EndsWith("/decision/", StringComparison.Ordinal))
            {
                var id = path["/v3/session/".Length..^"/decision/".Length];
                if (!_created.Select((_, i) => $"didit-session-{i + 1}").Contains(id))
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                return _decisions.TryGetValue(id, out var decision)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(decision, Encoding.UTF8, "application/json") }
                    : Json(HttpStatusCode.OK, new JsonObject { ["session_id"] = id, ["status"] = "In Progress" });
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, JsonObject body) =>
        new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
}
