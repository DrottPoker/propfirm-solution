using Common.Postgres;

using Npgsql;

namespace Prop.Api.Portal;

/// <summary>
/// The firm's own checks of a trader before it funds them or pays them out (ADR 0037): that it has seen their ID and
/// their address. The firm ticks them itself until a verification provider does them.
/// </summary>
internal static class TraderCheckItems
{
    public const string Identity = "identity";
    public const string Address = "address";

    /// <summary>Every check, in the order the admin panel shows them, with what it means.</summary>
    public static readonly IReadOnlyList<(string Item, string Label)> All =
    [
        (Identity, "ID checked"),
        (Address, "Address checked"),
    ];

    public static bool IsKnown(string item) => All.Any(c => c.Item == item);
}

/// <summary>A check the firm ticked: when, and which administrator.</summary>
internal sealed record TraderCheck(Guid TraderId, string Item, DateTimeOffset CheckedAt, string CheckedBy);

internal sealed class TraderChecks(NpgsqlDataSource dataSource, DatabaseSchema schema)
{
    /// <summary>The ticked checks of the traders.</summary>
    public async Task<ILookup<Guid, TraderCheck>> ListAsync(Guid[] traderIds, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("select trader_id, item, checked_at, checked_by from trader_checks where trader_id = any($1)");
        command.Parameters.AddWithValue(traderIds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var checks = new List<TraderCheck>();
        while (await reader.ReadAsync(cancellationToken))
        {
            checks.Add(new TraderCheck(reader.GetGuid(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetString(3)));
        }

        return checks.ToLookup(c => c.TraderId);
    }

    /// <summary>Ticks the check, or takes the tick away. A check ticked again keeps who ticked it first.</summary>
    public async Task SetAsync(Guid traderId, string item, bool isChecked, string checkedBy, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            isChecked
                ? "insert into trader_checks (trader_id, item, checked_at, checked_by) values ($1, $2, $3, $4) on conflict (trader_id, item) do nothing"
                : "delete from trader_checks where trader_id = $1 and item = $2");
        command.Parameters.AddWithValue(traderId);
        command.Parameters.AddWithValue(item);
        if (isChecked)
        {
            command.Parameters.AddWithValue(now);
            command.Parameters.AddWithValue(checkedBy);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
