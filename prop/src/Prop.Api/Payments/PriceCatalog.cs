using Common.Postgres;

using Npgsql;

namespace Prop.Api.Payments;

/// <summary>What a challenge sells for in the firm's portal, and whether it is for sale there.</summary>
public sealed record ChallengePrice(string ChallengeId, decimal Amount, string Currency, bool ForSale);

internal static class PriceRules
{
    public const decimal MinAmount = 1m;
    public const decimal MaxAmount = 100_000m;

    /// <summary>Currencies with two decimals, so an amount is the same in cents at every provider.</summary>
    public static readonly IReadOnlySet<string> Currencies = new HashSet<string>(StringComparer.Ordinal)
    {
        "AED", "AUD", "CAD", "CHF", "CZK", "DKK", "EUR", "GBP", "HKD", "NOK", "NZD", "PLN", "SEK", "SGD", "USD",
    };

    /// <summary>What is wrong with the price. Null when it is valid.</summary>
    public static string? Problem(decimal amount, string? currency)
    {
        if (currency is null || !Currencies.Contains(currency))
        {
            return $"The currency must be one of {string.Join(", ", Currencies.Order(StringComparer.Ordinal))}.";
        }

        return amount is < MinAmount or > MaxAmount || decimal.Round(amount, 2) != amount
            ? FormattableString.Invariant($"The price must be {MinAmount:0} to {MaxAmount:0}, in whole cents.")
            : null;
    }
}

/// <summary>The firms' prices for their challenges. Kept apart from the challenges, which are only rules.</summary>
internal sealed class PriceCatalog(NpgsqlDataSource dataSource, DatabaseSchema schema, TimeProvider time)
{
    public async Task<IReadOnlyList<ChallengePrice>> ListAsync(string firmId, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            "select challenge_id, amount, currency, for_sale from challenge_prices where firm_id = $1 order by challenge_id");
        command.Parameters.AddWithValue(firmId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var prices = new List<ChallengePrice>();
        while (await reader.ReadAsync(cancellationToken))
        {
            prices.Add(new ChallengePrice(reader.GetString(0), reader.GetDecimal(1), reader.GetString(2), reader.GetBoolean(3)));
        }

        return prices;
    }

    public async Task<ChallengePrice?> GetAsync(string firmId, string challengeId, CancellationToken cancellationToken) =>
        (await ListAsync(firmId, cancellationToken)).FirstOrDefault(p => p.ChallengeId == challengeId);

    /// <summary>Sets the challenge's price. False when the firm has no such challenge. Validate the price first.</summary>
    public async Task<bool> SaveAsync(string firmId, ChallengePrice price, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            """
            insert into challenge_prices (firm_id, challenge_id, amount, currency, for_sale, updated_at) values ($1, $2, $3, $4, $5, $6)
            on conflict (firm_id, challenge_id) do update set
                amount = excluded.amount, currency = excluded.currency, for_sale = excluded.for_sale, updated_at = excluded.updated_at
            """);
        command.Parameters.AddWithValue(firmId);
        command.Parameters.AddWithValue(price.ChallengeId);
        command.Parameters.AddWithValue(price.Amount);
        command.Parameters.AddWithValue(price.Currency);
        command.Parameters.AddWithValue(price.ForSale);
        command.Parameters.AddWithValue(time.GetUtcNow());
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return false;
        }
    }

    /// <summary>The challenge is no longer sold in the portal and has no price.</summary>
    public async Task RemoveAsync(string firmId, string challengeId, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("delete from challenge_prices where firm_id = $1 and challenge_id = $2");
        command.Parameters.AddWithValue(firmId);
        command.Parameters.AddWithValue(challengeId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
