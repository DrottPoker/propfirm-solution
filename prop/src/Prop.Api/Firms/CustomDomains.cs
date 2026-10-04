using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

using Common.Postgres;

using Microsoft.Extensions.Options;

using Npgsql;

using Prop.Api.Challenges;
using Prop.Api.Configuration;

namespace Prop.Api.Firms;

/// <summary>Where firms point their own domains, and how often we look them up (ADR 0039).</summary>
public sealed class DomainOptions
{
    public const string SectionName = "Domains";

    /// <summary>The host a firm's domain points to with a CNAME record, for example portals.example.app. Empty turns own domains off.</summary>
    public string CnameTarget { get; init; } = "";

    /// <summary>How often domains that wait for their DNS records are looked up.</summary>
    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>A resolver with the JSON API for DNS over HTTPS, which the lookups go through.</summary>
    public Uri DnsOverHttpsUrl { get; init; } = new("https://cloudflare-dns.com/dns-query");

    public bool Enabled => CnameTarget.Length > 0;
}

/// <summary>Where a firm's own domain is: waiting for its DNS records, or the portal's address.</summary>
public enum DomainStatus
{
    Pending,
    Active,
}

/// <summary>
/// A firm's own domain for its portal. <paramref name="Token"/> is the value of the TXT record that proves the domain
/// is the firm's; <paramref name="Problem"/> says what the last lookup missed.
/// </summary>
internal sealed record CustomDomain(
    string FirmId,
    string Domain,
    string Token,
    DomainStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CheckedAt,
    DateTimeOffset? ActiveAt,
    string? Problem)
{
    /// <summary>The TXT record with the token, beside the domain.</summary>
    public string TxtName => $"{DomainRules.RecordPrefix}.{Domain}";
}

internal static class DomainRules
{
    /// <summary>The TXT record's name in front of the domain.</summary>
    public const string RecordPrefix = "_prop-platform";

    private static readonly IdnMapping Idn = new();

    /// <summary>The domain as DNS has it: in lower case, without a trailing dot and with international names in ASCII. Null when it cannot be one.</summary>
    public static string? Normalize(string? domain)
    {
        var trimmed = domain?.Trim().TrimEnd('.').ToLowerInvariant() ?? "";
        if (trimmed.StartsWith("https://", StringComparison.Ordinal) || trimmed.StartsWith("http://", StringComparison.Ordinal))
        {
            trimmed = Uri.TryCreate(trimmed, UriKind.Absolute, out var url) ? url.Host : "";
        }

        try
        {
            return trimmed.Length == 0 ? null : Idn.GetAscii(trimmed);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>What is wrong with the domain for a firm's portal. Null when it can be one.</summary>
    public static string? Problem(string? domain, PlatformOptions platform, DomainOptions options)
    {
        if (domain is null || domain.Length > 253 || IPAddress.TryParse(domain, out _))
        {
            return "Write a domain such as portal.yourfirm.com.";
        }

        var labels = domain.Split('.');
        if (labels.Length < 3 || labels.Any(l => l.Length is 0 or > 63 || l[0] == '-' || l[^1] == '-' || !l.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')))
        {
            return "Use a subdomain of your own domain, such as portal.yourfirm.com. A domain without one cannot point to us with a CNAME record.";
        }

        return OurOwn(platform, options).Any(own => domain == own || domain.EndsWith($".{own}", StringComparison.Ordinal))
            ? "That domain is ours. Use one of your own."
            : null;
    }

    // Our own domains, and the one firms' portals are on.
    private static IEnumerable<string> OurOwn(PlatformOptions platform, DomainOptions options)
    {
        string?[] hosts =
        [
            platform.Url?.Host,
            platform.OpsUrl?.Host,
            platform.ApiUrl?.Host,
            Uri.TryCreate(platform.FirmPortalUrl.Replace("{firm}", "x", StringComparison.Ordinal), UriKind.Absolute, out var portal) ? portal.Host[2..] : null,
            options.CnameTarget,
        ];
        return hosts.Where(h => !string.IsNullOrEmpty(h) && h.Contains('.', StringComparison.Ordinal)).Select(h => h!.ToLowerInvariant());
    }

    /// <summary>A new token for the TXT record.</summary>
    public static string NewToken() => $"prop-verify-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))}";
}

/// <summary>How saving a firm's own domain went.</summary>
internal enum DomainSaveOutcome
{
    Saved,
    Taken,
    Configured,
}

/// <summary>The firms' own domains. A firm has at most one, and a domain belongs to one firm.</summary>
internal sealed class CustomDomainStore(NpgsqlDataSource dataSource, DatabaseSchema schema)
{
    private const string SelectDomain = "select firm_id, domain, token, status, created_at, checked_at, active_at, problem from firm_domains";

    public async Task<CustomDomain?> GetAsync(string firmId, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return (await ReadAsync(connection, "where firm_id = $1", [firmId], cancellationToken)).SingleOrDefault();
    }

    public async Task<IReadOnlyList<CustomDomain>> PendingAsync(CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await ReadAsync(connection, "where status = 'Pending' order by created_at", [], cancellationToken);
    }

    /// <summary>
    /// Saves the firm's new domain, waiting for its DNS records. A domain the firm had before stops being its portal's
    /// address, which goes back to <paramref name="defaultPortalUrl"/>. Firms from the configuration keep the hosts it gives them.
    /// </summary>
    public async Task<DomainSaveOutcome> SaveAsync(string firmId, string domain, Uri defaultPortalUrl, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (await ScalarAsync(connection, "select configured from firms where id = $1 for update", [firmId], cancellationToken) is true)
        {
            return DomainSaveOutcome.Configured;
        }

        if (await ScalarAsync(connection, "select 1 from firm_hosts where host = $1 and firm_id <> $2", [domain, firmId], cancellationToken) is not null)
        {
            return DomainSaveOutcome.Taken;
        }

        await RemoveAsync(connection, firmId, defaultPortalUrl, cancellationToken);
        try
        {
            await ExecuteAsync(
                connection,
                "insert into firm_domains (firm_id, domain, token, status, created_at) values ($1, $2, $3, 'Pending', $4)",
                [firmId, domain, DomainRules.NewToken(), now],
                cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return DomainSaveOutcome.Taken;
        }

        await transaction.CommitAsync(cancellationToken);
        return DomainSaveOutcome.Saved;
    }

    /// <summary>The firm no longer has its own domain, and its portal is at <paramref name="defaultPortalUrl"/> again.</summary>
    public async Task RemoveAsync(string firmId, Uri defaultPortalUrl, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await RemoveAsync(connection, firmId, defaultPortalUrl, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task MarkCheckedAsync(string firmId, string? problem, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await ExecuteAsync(connection, "update firm_domains set checked_at = $2, problem = $3 where firm_id = $1", [firmId, now, (object?)problem ?? DBNull.Value], cancellationToken);
    }

    /// <summary>
    /// The domain's records are right: it becomes one of the firm's hosts and its portal's address, in one transaction.
    /// False when another firm got the host meanwhile.
    /// </summary>
    public async Task<bool> ActivateAsync(CustomDomain domain, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (await ScalarAsync(connection, "select firm_id from firm_hosts where host = $1", [domain.Domain], cancellationToken) is string owner && owner != domain.FirmId)
        {
            return false;
        }

        await ExecuteAsync(connection, "insert into firm_hosts (host, firm_id) values ($1, $2) on conflict (host) do nothing", [domain.Domain, domain.FirmId], cancellationToken);
        await ExecuteAsync(connection, "update firms set portal_url = $2, updated_at = $3 where id = $1", [domain.FirmId, $"https://{domain.Domain}/", now], cancellationToken);
        await ExecuteAsync(
            connection,
            "update firm_domains set status = 'Active', checked_at = $2, active_at = $2, problem = null where firm_id = $1",
            [domain.FirmId, now],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>Whether a firm's portal is on the host, so a certificate may be made for it.</summary>
    public async Task<bool> IsFirmHostAsync(string host, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await ScalarAsync(connection, "select 1 from firm_hosts where host = $1", [host], cancellationToken) is not null;
    }

    private static async Task RemoveAsync(NpgsqlConnection connection, string firmId, Uri defaultPortalUrl, CancellationToken cancellationToken)
    {
        if (await ScalarAsync(connection, "delete from firm_domains where firm_id = $1 returning domain", [firmId], cancellationToken) is not string old)
        {
            return;
        }

        await ExecuteAsync(connection, "delete from firm_hosts where host = $1 and firm_id = $2", [old, firmId], cancellationToken);
        await ExecuteAsync(connection, "update firms set portal_url = $2 where id = $1 and portal_url = $3", [firmId, defaultPortalUrl.ToString(), $"https://{old}/"], cancellationToken);
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql, object[] parameters, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter);
        }

        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, object[] parameters, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<List<CustomDomain>> ReadAsync(NpgsqlConnection connection, string where, object[] parameters, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"{SelectDomain} {where}", connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var domains = new List<CustomDomain>();
        while (await reader.ReadAsync(cancellationToken))
        {
            domains.Add(new CustomDomain(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                Enum.Parse<DomainStatus>(reader.GetString(3)),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return domains;
    }
}

/// <summary>DNS record types the lookups use.</summary>
internal enum DnsRecordType
{
    A = 1,
    Cname = 5,
    Txt = 16,
}

/// <summary>DNS could not be asked, so whether a record is there is not known.</summary>
internal sealed class DnsLookupException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Looks up DNS records, so a firm's domain can be checked. Throws <see cref="DnsLookupException"/> when DNS cannot be asked.</summary>
internal interface IDnsLookup
{
    /// <summary>The record's values, a TXT record's without its quotes. Empty when there is no such record.</summary>
    Task<IReadOnlyList<string>> LookupAsync(string name, DnsRecordType type, CancellationToken cancellationToken);
}

/// <summary>DNS over HTTPS with the JSON API that Cloudflare and Google answer, so no DNS library is needed.</summary>
internal sealed class DnsOverHttpsLookup(IHttpClientFactory clients, IOptions<DomainOptions> options) : IDnsLookup
{
    public const string HttpClientName = "dns";

    public async Task<IReadOnlyList<string>> LookupAsync(string name, DnsRecordType type, CancellationToken cancellationToken)
    {
        var url = new Uri($"{options.Value.DnsOverHttpsUrl}?name={Uri.EscapeDataString(name)}&type={(int)type}");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/dns-json");
            using var response = await clients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var answer = await response.Content.ReadFromJsonAsync<DnsAnswer>(cancellationToken) ?? throw new DnsLookupException("DNS gave no answer.");

            // 0 is an answer and 3 a name that does not exist. Anything else means DNS could not say.
            return answer.Status switch
            {
                0 => [.. (answer.Answer ?? []).Where(a => a.Type == (int)type).Select(a => type == DnsRecordType.Txt ? TxtValue(a.Data) : a.Data.TrimEnd('.'))],
                3 => [],
                _ => throw new DnsLookupException($"DNS answered with status {answer.Status}."),
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !cancellationToken.IsCancellationRequested)
        {
            throw new DnsLookupException("DNS could not be asked.", exception);
        }
    }

    // A TXT record comes as one or more quoted strings, which together are its value.
    private static string TxtValue(string data) => string.Concat(data.Split('"', StringSplitOptions.RemoveEmptyEntries).Where(part => part.Trim().Length > 0));

    private sealed record DnsAnswer([property: JsonPropertyName("Status")] int Status, [property: JsonPropertyName("Answer")] List<DnsRecord>? Answer);

    private sealed record DnsRecord([property: JsonPropertyName("type")] int Type, [property: JsonPropertyName("data")] string Data);
}

/// <summary>
/// Checks a firm's own domain: the TXT record with its token proves it is the firm's, and a CNAME record, or the same
/// addresses, shows that it points to us. Once both are there, the domain becomes the portal's address.
/// </summary>
internal sealed class DomainVerifier(
    CustomDomainStore store,
    IDnsLookup dns,
    FirmStore firms,
    FirmCatalog catalog,
    WorkSignals signals,
    IOptions<DomainOptions> options,
    TimeProvider time)
{
    public async Task<CustomDomain?> CheckAsync(string firmId, CancellationToken cancellationToken)
    {
        if (await store.GetAsync(firmId, cancellationToken) is not { Status: DomainStatus.Pending } domain)
        {
            return await store.GetAsync(firmId, cancellationToken);
        }

        var target = options.Value.CnameTarget;
        string? problem;
        try
        {
            problem = !(await dns.LookupAsync(domain.TxtName, DnsRecordType.Txt, cancellationToken)).Contains(domain.Token, StringComparer.Ordinal)
                ? $"The TXT record {domain.TxtName} with the value {domain.Token} is not there yet."
                : !await PointsToUsAsync(domain.Domain, target, cancellationToken)
                    ? $"{domain.Domain} does not point to {target} yet. Add a CNAME record for it."
                    : null;
        }
        catch (DnsLookupException)
        {
            problem = "We could not look the domain up right now, and try again shortly.";
        }

        var now = time.GetUtcNow();
        if (problem is null && await store.ActivateAsync(domain, now, cancellationToken))
        {
            if (await firms.GetAsync(firmId, cancellationToken) is { } reloaded)
            {
                catalog.Put(reloaded);
            }

            // The terminal's link back to the portal moves to the new address.
            signals.Provisioning.Set();
        }
        else
        {
            await store.MarkCheckedAsync(firmId, problem ?? "Another firm has that domain.", now, cancellationToken);
        }

        return await store.GetAsync(firmId, cancellationToken);
    }

    // A CNAME to us, or, for a domain that cannot have one, the same addresses as ours.
    private async Task<bool> PointsToUsAsync(string domain, string target, CancellationToken cancellationToken)
    {
        var names = await dns.LookupAsync(domain, DnsRecordType.Cname, cancellationToken);
        if (names.Any(n => string.Equals(n, target, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var ours = await dns.LookupAsync(target, DnsRecordType.A, cancellationToken);
        var theirs = await dns.LookupAsync(domain, DnsRecordType.A, cancellationToken);
        return ours.Count > 0 && ours.Order(StringComparer.Ordinal).SequenceEqual(theirs.Order(StringComparer.Ordinal));
    }
}

/// <summary>Looks up the domains that wait for their DNS records, every few minutes, while own domains are on.</summary>
internal sealed partial class DomainVerificationWorker(
    CustomDomainStore store,
    DomainVerifier verifier,
    FirmCatalog firms,
    DatabaseSchema schema,
    IOptions<DomainOptions> options,
    TimeProvider time,
    ILogger<DomainVerificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        await schema.EnsureAsync(stoppingToken);
        await firms.Ready.WaitAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var domain in await store.PendingAsync(stoppingToken))
                {
                    await verifier.CheckAsync(domain.FirmId, stoppingToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception);
            }

            await Task.Delay(options.Value.CheckInterval, time, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Checking the firms' own domains failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
