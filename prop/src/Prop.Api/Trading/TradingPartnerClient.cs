using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Options;

using Prop.Api.Configuration;

namespace Prop.Api.Trading;

/// <summary>
/// Our trading platform's partner API v1 over HTTP, with the prop platform's partner key. The contract is
/// <c>contracts/trading/trading-service.json</c>, and a test checks that every path and field used here is in it.
/// </summary>
internal sealed class TradingPartnerClient(IHttpClientFactory httpClients, IOptions<TradingPlatformOptions> options) : ITradingPartner
{
    private const string Partner = "api/partner/v1/";
    private const string ApiKeyHeader = "X-Api-Key";

    public async Task<bool> IsServerAvailableAsync(string server, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"{Partner}server-names/{Uri.EscapeDataString(server)}", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await ReadJsonAsync(response, cancellationToken)).GetProperty("available").GetBoolean();
    }

    public async Task<PartnerTenant?> CreateTenantAsync(string server, string name, string currency, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, $"{Partner}tenants", new { id = server, name, currency }, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var tenant = await ReadJsonAsync(response, cancellationToken);
        return ToTenant(tenant, tenant.GetProperty("adminApiKey").GetString());
    }

    public async Task<PartnerTenant?> GetTenantAsync(string server, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"{Partner}tenants/{Uri.EscapeDataString(server)}", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return ToTenant(await ReadJsonAsync(response, cancellationToken), null);
    }

    public async Task<string> ReplaceAdminKeyAsync(string server, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, $"{Partner}tenants/{Uri.EscapeDataString(server)}/admin-key", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await ReadJsonAsync(response, cancellationToken)).GetProperty("adminApiKey").GetString()!;
    }

    public async Task SetListingAsync(string server, bool listed, Uri loginUrl, Uri? logoUrl, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Patch,
            $"{Partner}tenants/{Uri.EscapeDataString(server)}",
            new { listed, loginUrl = loginUrl.ToString(), logoUrl = logoUrl?.ToString() ?? "" },
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static PartnerTenant ToTenant(JsonElement tenant, string? adminApiKey) =>
        new(
            tenant.GetProperty("id").GetString()!,
            [.. tenant.GetProperty("groups").EnumerateArray().Select(g => new PartnerGroup(g.GetProperty("id").GetString()!, g.GetProperty("currency").GetString()!))],
            adminApiKey);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Add(ApiKeyHeader, options.Value.PartnerApiKey);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        try
        {
            return await httpClients.CreateClient(TradingPlatformClient.HttpClientName).SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new TradingPlatformUnavailableException($"The trading platform could not be reached: {exception.Message}", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TradingPlatformUnavailableException("The trading platform did not answer in time.", exception);
        }
    }

    // Server errors are worth trying again; anything else the platform refused is final.
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new TradingPlatformUnavailableException($"The trading platform answered {(int)response.StatusCode}: {body}");
        }

        throw new TradingPlatformRejectedException(
            $"The trading platform refused {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: {(int)response.StatusCode} {body}");
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }
}
