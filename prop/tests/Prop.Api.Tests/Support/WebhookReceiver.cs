using System.Net;
using System.Text.Json;

namespace Prop.Api.Tests.Support;

/// <summary>The firm's end of the webhooks. Answers with the queued status codes, then 200.</summary>
internal sealed class WebhookReceiver : HttpMessageHandler
{
    private readonly Lock _lock = new();
    private readonly Queue<HttpStatusCode> _answers = new();
    private readonly List<Received> _received = [];

    public IReadOnlyList<Received> Received
    {
        get
        {
            lock (_lock)
            {
                return [.. _received];
            }
        }
    }

    /// <summary>The next deliveries are answered with these status codes.</summary>
    public void AnswerWith(params HttpStatusCode[] statuses)
    {
        lock (_lock)
        {
            foreach (var status in statuses)
            {
                _answers.Enqueue(status);
            }
        }
    }

    /// <summary>The successfully delivered webhooks of a type.</summary>
    public List<Received> Delivered(string eventType) =>
        [.. Received.Where(r => r.Status == HttpStatusCode.OK && r.EventType == eventType)];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        lock (_lock)
        {
            var status = _answers.TryDequeue(out var queued) ? queued : HttpStatusCode.OK;
            _received.Add(new Received(
                request.Headers.GetValues("Prop-Webhook-Event").Single(),
                request.Headers.GetValues("Prop-Signature").Single(),
                body,
                status));
            return new HttpResponseMessage(status);
        }
    }
}

internal sealed record Received(string EventType, string Signature, string Body, HttpStatusCode Status)
{
    public JsonElement Json
    {
        get
        {
            using var document = JsonDocument.Parse(Body);
            return document.RootElement.Clone();
        }
    }
}
