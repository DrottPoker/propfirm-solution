using Common.Postgres;

using Npgsql;

using NpgsqlTypes;

using Prop.Api.Challenges;

namespace Prop.Api.Payments;

/// <summary>
/// A discount code in the firm's shop (ADR 0036): <paramref name="PercentOff"/> or <paramref name="AmountOff"/> in
/// <paramref name="Currency"/>, for the challenges in <paramref name="ChallengeIds"/> or every one when null, at most
/// <paramref name="MaxUses"/> times and until <paramref name="ExpiresAt"/>. A code <paramref name="ForRetries"/> is only
/// for a buyer with an account at the firm that failed. <paramref name="Uses"/> counts paid orders and orders still
/// waiting for payment, so a code cannot be used more often than the firm allows while buyers pay.
/// </summary>
internal sealed record DiscountCode(
    Guid Id,
    string FirmId,
    string Code,
    decimal? PercentOff,
    decimal? AmountOff,
    string? Currency,
    IReadOnlyList<string>? ChallengeIds,
    int? MaxUses,
    DateTimeOffset? ExpiresAt,
    bool ForRetries,
    bool Active,
    DateTimeOffset CreatedAt,
    int Uses);

/// <summary>A price with a code taken off. <paramref name="Amount"/> is what the buyer pays.</summary>
internal sealed record DiscountedPrice(decimal ListAmount, decimal Discount, decimal Amount, string Currency);

internal static class DiscountRules
{
    public const int MaxCodeLength = 32;
    public const decimal MinPercent = 1m;
    public const decimal MaxPercent = 99m;
    public const int MaxUsesLimit = 1_000_000;

    /// <summary>Codes are matched without regard to case or surrounding spaces.</summary>
    public static string Normalize(string code) => code.Trim().ToUpperInvariant();

    /// <summary>What is wrong with a new code. Null when it is valid.</summary>
    public static string? Problem(DiscountCodeRequest request, IReadOnlySet<string> challengeIds, DateTimeOffset now)
    {
        var code = request.Code?.Trim() ?? "";
        if (code.Length is 0 or > MaxCodeLength || !code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            return $"The code is 1 to {MaxCodeLength} letters, digits, - or _.";
        }

        if ((request.PercentOff is null) == (request.AmountOff is null))
        {
            return "Give either a percentage or an amount off.";
        }

        if (request.PercentOff is { } percent && (percent is < MinPercent or > MaxPercent || decimal.Round(percent, 2) != percent))
        {
            return FormattableString.Invariant($"The percentage off must be {MinPercent:0} to {MaxPercent:0}, with at most two decimals.");
        }

        if (request.AmountOff is { } amount)
        {
            if (amount <= 0 || amount > PriceRules.MaxAmount || decimal.Round(amount, 2) != amount)
            {
                return "The amount off must be more than 0, in whole cents.";
            }

            if (request.Currency is null || !PriceRules.Currencies.Contains(request.Currency))
            {
                return "Choose the currency of the amount off.";
            }
        }

        if (request.ChallengeIds is { } ids && (ids.Count == 0 || ids.Any(id => !challengeIds.Contains(id))))
        {
            return "Choose the challenges the code is for, or leave it for every challenge.";
        }

        if (request.MaxUses is < 1 or > MaxUsesLimit)
        {
            return FormattableString.Invariant($"The most uses must be 1 to {MaxUsesLimit:N0}, or empty for no limit.");
        }

        return request.ExpiresAt is { } expires && expires <= now ? "The code must end in the future." : null;
    }

    /// <summary>
    /// The price with the code taken off, or why the code cannot be used on it. A code never brings a price below
    /// the lowest price, so a payment provider can still take it.
    /// </summary>
    public static (DiscountedPrice? Price, string? Refusal) Apply(DiscountCode code, ChallengePrice price, DateTimeOffset now)
    {
        if (!code.Active || code.ExpiresAt is { } expires && expires <= now)
        {
            return (null, "That code has ended.");
        }

        if (code.ChallengeIds is { } ids && !ids.Contains(price.ChallengeId))
        {
            return (null, "That code is not for this challenge.");
        }

        if (code.AmountOff is not null && !string.Equals(code.Currency, price.Currency, StringComparison.Ordinal))
        {
            return (null, $"That code is only for prices in {code.Currency}.");
        }

        if (code.MaxUses is { } max && code.Uses >= max)
        {
            return (null, "That code has been used up.");
        }

        var discount = code.PercentOff is { } percent
            ? decimal.Round(price.Amount * percent / 100m, 2, MidpointRounding.AwayFromZero)
            : code.AmountOff!.Value;
        var amount = price.Amount - discount;
        return amount < PriceRules.MinAmount
            ? (null, FormattableString.Invariant($"That code would bring the price below {PriceRules.MinAmount:0.00} {price.Currency}."))
            : (new DiscountedPrice(price.Amount, discount, amount, price.Currency), null);
    }
}

/// <summary>The firms' discount codes, and how often each was used.</summary>
internal sealed class DiscountStore(NpgsqlDataSource dataSource, DatabaseSchema schema)
{
    // Paid orders, and orders still waiting for payment, count as uses.
    private const string SelectCode =
        """
        select d.id, d.firm_id, d.code, d.percent_off, d.amount_off, d.currency, d.challenge_ids, d.max_uses, d.expires_at, d.for_retries,
               d.active, d.created_at,
               (select count(*)::int from orders o
                where o.discount_code_id = d.id and (o.status = 'Paid' or (o.status = 'Pending' and o.expires_at > $2)))
        from discount_codes d
        """;

    public async Task<IReadOnlyList<DiscountCode>> ListAsync(string firmId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await ReadAsync(connection, "where d.firm_id = $1 order by d.created_at desc", [firmId, now], cancellationToken);
    }

    /// <summary>The firm's code as the buyer typed it, in any case.</summary>
    public async Task<DiscountCode?> FindAsync(string firmId, string code, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return (await ReadAsync(connection, "where d.firm_id = $1 and d.normalized_code = $3", [firmId, now, DiscountRules.Normalize(code)], cancellationToken))
            .SingleOrDefault();
    }

    /// <summary>The code locked until the caller's transaction ends, so two orders cannot both take its last use.</summary>
    public static async Task<DiscountCode?> LockAsync(NpgsqlConnection connection, string firmId, Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using (var command = new NpgsqlCommand("select 1 from discount_codes where firm_id = $1 and id = $2 for update", connection))
        {
            command.Parameters.AddWithValue(firmId);
            command.Parameters.AddWithValue(id);
            await command.ExecuteScalarAsync(cancellationToken);
        }

        return (await ReadAsync(connection, "where d.firm_id = $1 and d.id = $3", [firmId, now, id], cancellationToken)).SingleOrDefault();
    }

    /// <summary>Saves a new code. False when the firm already has a code with the same letters.</summary>
    public async Task<bool> CreateAsync(DiscountCode code, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            """
            insert into discount_codes (id, firm_id, code, normalized_code, percent_off, amount_off, currency, challenge_ids, max_uses, expires_at,
                                        for_retries, active, created_at)
            values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, true, $12)
            """);
        command.Parameters.AddWithValue(code.Id);
        command.Parameters.AddWithValue(code.FirmId);
        command.Parameters.AddWithValue(code.Code);
        command.Parameters.AddWithValue(DiscountRules.Normalize(code.Code));
        command.Parameters.Add(Number(code.PercentOff));
        command.Parameters.Add(Number(code.AmountOff));
        command.Parameters.Add(OrderStore.Text(code.Currency));
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)code.ChallengeIds?.ToArray() ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)code.MaxUses ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Integer });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)code.ExpiresAt ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.TimestampTz });
        command.Parameters.AddWithValue(code.ForRetries);
        command.Parameters.AddWithValue(code.CreatedAt);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }
    }

    /// <summary>Turns the code on or off. False when the firm has no such code.</summary>
    public async Task<bool> SetActiveAsync(string firmId, Guid id, bool active, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("update discount_codes set active = $3 where firm_id = $1 and id = $2");
        command.Parameters.AddWithValue(firmId);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(active);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>Removes a code no order has used. Null when the firm has no such code, false when an order used it.</summary>
    public async Task<bool?> DeleteAsync(string firmId, Guid id, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            """
            delete from discount_codes d
            where d.firm_id = $1 and d.id = $2 and not exists (select 1 from orders o where o.discount_code_id = d.id)
            returning 1
            """);
        command.Parameters.AddWithValue(firmId);
        command.Parameters.AddWithValue(id);
        if (await command.ExecuteScalarAsync(cancellationToken) is not null)
        {
            return true;
        }

        await using var exists = dataSource.CreateCommand("select 1 from discount_codes where firm_id = $1 and id = $2");
        exists.Parameters.AddWithValue(firmId);
        exists.Parameters.AddWithValue(id);
        return await exists.ExecuteScalarAsync(cancellationToken) is null ? null : false;
    }

    /// <summary>Whether the buyer has an account at the firm that failed, which a code for retries needs.</summary>
    public async Task<bool> HasFailedAccountAsync(string firmId, string email, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            """
            select exists (select 1 from challenge_accounts a join traders t on t.id = a.trader_id
                           where t.firm_id = $1 and t.normalized_email = $2 and a.status = 'Failed')
            """);
        command.Parameters.AddWithValue(firmId);
        command.Parameters.AddWithValue(Emails.Normalize(email));
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static NpgsqlParameter Number(decimal? value) => new() { Value = (object?)value ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Numeric };

    private static async Task<IReadOnlyList<DiscountCode>> ReadAsync(NpgsqlConnection connection, string where, object[] parameters, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"{SelectCode} {where}", connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var codes = new List<DiscountCode>();
        while (await reader.ReadAsync(cancellationToken))
        {
            codes.Add(new DiscountCode(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<string[]>(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                reader.GetBoolean(9),
                reader.GetBoolean(10),
                reader.GetFieldValue<DateTimeOffset>(11),
                reader.GetInt32(12)));
        }

        return codes;
    }
}
