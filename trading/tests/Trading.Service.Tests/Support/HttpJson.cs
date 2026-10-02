using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Trading.Service.Tests.Support;

/// <summary>Calls the API and returns the raw JSON, so tests check exactly what a client receives.</summary>
internal static class HttpJson
{
    public static Task<JsonElement> GetJsonAsync(this HttpClient client, string url) =>
        client.SendJsonAsync(HttpMethod.Get, url, null, HttpStatusCode.OK);

    public static Task<JsonElement> PostJsonAsync(this HttpClient client, string url, object? body = null, HttpStatusCode expected = HttpStatusCode.OK) =>
        client.SendJsonAsync(HttpMethod.Post, url, body, expected);

    public static Task<JsonElement> PutJsonAsync(this HttpClient client, string url, object body) =>
        client.SendJsonAsync(HttpMethod.Put, url, body, HttpStatusCode.OK);

    public static Task<JsonElement> DeleteJsonAsync(this HttpClient client, string url) =>
        client.SendJsonAsync(HttpMethod.Delete, url, null, HttpStatusCode.OK);

    public static Task<JsonElement> CreateAccountAsync(this HttpClient client, string accountId, decimal balance = 100_000m) =>
        client.PostJsonAsync("/api/admin/accounts", new { accountId, groupId = "standard", initialBalance = balance });

    public static async Task<JsonElement> PriceAsync(this HttpClient client, string accountId, string symbol)
    {
        var prices = await client.GetJsonAsync($"/api/accounts/{accountId}/prices");
        return prices.EnumerateArray().Single(p => p.GetProperty("symbol").GetString() == symbol);
    }

    /// <summary>The "kind" of each event in a command response or event list.</summary>
    public static List<string?> EventKinds(this JsonElement envelopes) =>
        envelopes.EnumerateArray().Select(e => e.GetProperty("event").GetProperty("kind").GetString()).ToList();

    private static async Task<JsonElement> SendJsonAsync(this HttpClient client, HttpMethod method, string url, object? body, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(method, new Uri(url, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {expected} but got {response.StatusCode}: {content}");
        if (content.Length == 0)
        {
            return default;
        }

        using var document = JsonDocument.Parse(content);
        return document.RootElement.Clone();
    }
}
