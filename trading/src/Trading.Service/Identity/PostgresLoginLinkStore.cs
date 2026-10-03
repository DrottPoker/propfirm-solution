using Common.Postgres;

using Npgsql;

namespace Trading.Service.Identity;

internal sealed class PostgresLoginLinkStore(NpgsqlDataSource dataSource, DatabaseSchema schema) : ILoginLinkStore
{
    public async Task CreateAsync(byte[] tokenHash, Guid userId, string? accountId, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var batch = dataSource.CreateBatch();

        // Expired links are kept a day for support, then removed as new links are made.
        var cleanUp = new NpgsqlBatchCommand("delete from login_links where expires_at < $1");
        cleanUp.Parameters.AddWithValue(expiresAt - TimeSpan.FromDays(1));
        batch.BatchCommands.Add(cleanUp);

        var insert = new NpgsqlBatchCommand("insert into login_links (token_hash, user_id, account_id, expires_at) values ($1, $2, $3, $4)");
        insert.Parameters.AddWithValue(tokenHash);
        insert.Parameters.AddWithValue(userId);
        insert.Parameters.AddWithValue((object?)accountId ?? DBNull.Value);
        insert.Parameters.AddWithValue(expiresAt);
        batch.BatchCommands.Add(insert);

        await batch.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<LoginLink?> UseAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);

        // One statement, so two requests with the same link cannot both succeed.
        await using var command = dataSource.CreateCommand(
            "update login_links set used_at = $2 where token_hash = $1 and used_at is null and expires_at > $2 returning user_id, account_id");
        command.Parameters.AddWithValue(tokenHash);
        command.Parameters.AddWithValue(now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new LoginLink(reader.GetGuid(0), await reader.IsDBNullAsync(1, cancellationToken) ? null : reader.GetString(1))
            : null;
    }
}
