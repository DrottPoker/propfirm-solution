using System.Collections.Concurrent;

using Prop.Api.Firms;

namespace Prop.Api.Tests.Support;

/// <summary>DNS records the tests add, instead of asking real DNS.</summary>
internal sealed class FakeDns : IDnsLookup
{
    private readonly ConcurrentDictionary<(string Name, DnsRecordType Type), string[]> _records = new();
    private int _failures;

    public void Add(string name, DnsRecordType type, params string[] values) => _records[(name.ToLowerInvariant(), type)] = values;

    /// <summary>The next lookups cannot reach DNS.</summary>
    public void FailNext(int lookups) => Interlocked.Exchange(ref _failures, lookups);

    public Task<IReadOnlyList<string>> LookupAsync(string name, DnsRecordType type, CancellationToken cancellationToken)
    {
        if (Interlocked.Decrement(ref _failures) >= 0)
        {
            throw new DnsLookupException("DNS is down in the test.");
        }

        return Task.FromResult<IReadOnlyList<string>>(_records.TryGetValue((name.ToLowerInvariant(), type), out var values) ? values : []);
    }
}
