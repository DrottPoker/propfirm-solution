using System.Security.Cryptography;
using System.Text;

using Common.Postgres;

using Npgsql;

using NpgsqlTypes;

namespace Prop.Api.Payments;

/// <summary>Where a purchase in the portal is. A pending order past its time counts as expired, but a payment that arrives later still counts.</summary>
public enum OrderStatus
{
    Pending,
    Paid,
    Expired,
}

/// <summary>
/// A purchase of a challenge in the firm's portal. <paramref name="AccountId"/> is the account the payment
/// started, and <paramref name="Problem"/> says why a paid order has none. <paramref name="Amount"/> is what the buyer
/// pays, and <paramref name="ListAmount"/> the price before the <paramref name="DiscountCode"/>.
/// </summary>
internal sealed record Order(
    Guid Id,
    string FirmId,
    long Number,
    string Email,
    string ChallengeId,
    decimal Amount,
    string Currency,
    PaymentProvider Provider,
    OrderStatus Status,
    string? CheckoutId,
    Uri CheckoutUrl,
    string? PaymentReference,
    Guid? AccountId,
    string? Problem,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? RefundedAt,
    DateTimeOffset? DisputedAt,
    DateTimeOffset? InviteSentAt,
    byte[] AccessTokenHash,
    string? BuyerName = null,
    string? BuyerCountry = null,
    string? DiscountCode = null,
    decimal? ListAmount = null)
{
    /// <summary>Whether the token from the buyer's link is this order's. Compared in constant time.</summary>
    public bool HasAccessToken(string? token) =>
        !string.IsNullOrEmpty(token) && CryptographicOperations.FixedTimeEquals(OrderStore.HashToken(token), AccessTokenHash);
}

/// <summary>The discount code an order uses, as the buyer typed it, and the price with it.</summary>
internal sealed record OrderDiscount(Guid CodeId, string Code, DiscountedPrice Price);

/// <summary>Something that happened to an order, and who said so.</summary>
internal sealed record OrderEvent(string Type, DateTimeOffset RecordedAt, string Source, string? Detail);

/// <summary>Who changed an order.</summary>
internal static class OrderSources
{
    public const string Buyer = "buyer";
    public const string Stripe = "stripe";
    public const string FirmApi = "firm-api";
    public const string Admin = "admin";
}

/// <summary>The orders in the database. Changes that must happen together run in the caller's transaction.</summary>
internal sealed class OrderStore(NpgsqlDataSource dataSource, DatabaseSchema schema)
{
    public const int MaxOrdersPerRequest = 500;

    private const string SelectOrder =
        """
        select id, firm_id, number, email, challenge_id, amount, currency, provider, status, checkout_id, checkout_url, payment_reference,
               challenge_account_id, problem, created_at, expires_at, paid_at, refunded_at, disputed_at, invite_sent_at, access_token_sha256,
               buyer_name, buyer_country, discount_code, list_amount
        from orders
        """;

    public static byte[] HashToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    /// <summary>
    /// Saves a new order with the next number of the firm, and records that it was made. <paramref name="reserve"/>
    /// runs first in the same transaction and says why the order may not be made, for example that it cannot hold a slot
    /// or its discount code was just used up. The order or that reason.
    /// </summary>
    public async Task<(Order? Order, string? Refusal)> InsertAsync(
        Guid id,
        string firmId,
        string email,
        string? buyerName,
        string? buyerCountry,
        string challengeId,
        ChallengePrice price,
        OrderDiscount? discount,
        PaymentProvider provider,
        string accessToken,
        string? checkoutId,
        Uri checkoutUrl,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        Func<NpgsqlConnection, CancellationToken, Task<string?>> reserve,
        CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (await reserve(connection, cancellationToken) is { } refusal)
        {
            return (null, refusal);
        }

        long number;
        await using (var counter = new NpgsqlCommand(
            """
            insert into order_counters (firm_id, next_number) values ($1, 1002)
            on conflict (firm_id) do update set next_number = order_counters.next_number + 1
            returning next_number - 1
            """,
            connection))
        {
            counter.Parameters.AddWithValue(firmId);
            number = (long)(await counter.ExecuteScalarAsync(cancellationToken))!;
        }

        await ExecuteAsync(
            connection,
            """
            insert into orders (id, firm_id, number, email, challenge_id, amount, currency, provider, status, access_token_sha256,
                                checkout_id, checkout_url, created_at, expires_at, sandbox, buyer_name, buyer_country,
                                discount_code_id, discount_code, list_amount)
            values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, (select f.status <> 'Live' from firms f where f.id = $2), $15, $16,
                    $17, $18, $19)
            """,
            [
                id, firmId, number, email.Trim(), challengeId, discount?.Price.Amount ?? price.Amount, price.Currency, provider.ToString(),
                OrderStatus.Pending.ToString(), HashToken(accessToken), Text(checkoutId), checkoutUrl.AbsoluteUri, now, expiresAt, Text(buyerName),
                Text(buyerCountry), new NpgsqlParameter { Value = (object?)discount?.CodeId ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Uuid },
                Text(discount?.Code), new NpgsqlParameter { Value = (object?)discount?.Price.ListAmount ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Numeric },
            ],
            cancellationToken);
        await AddEventAsync(connection, id, "created", OrderSources.Buyer, null, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ((await GetAsync(firmId, id, now, cancellationToken))!, null);
    }

    public async Task<Order?> GetAsync(string firmId, Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await FindAsync(connection, "firm_id = $1 and id = $2", [firmId, id], forUpdate: false, now, cancellationToken);
    }

    /// <summary>The firm's newest orders, optionally only those with a status.</summary>
    public async Task<IReadOnlyList<Order>> ListAsync(string firmId, OrderStatus? status, int limit, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        var where = status switch
        {
            OrderStatus.Pending => "and status = 'Pending' and expires_at > $3",
            OrderStatus.Expired => "and (status = 'Expired' or (status = 'Pending' and expires_at <= $3))",
            OrderStatus.Paid => "and status = 'Paid'",
            _ => "",
        };
        await using var command = dataSource.CreateCommand($"{SelectOrder} where firm_id = $1 {where} order by created_at desc limit $2");
        command.Parameters.AddWithValue(firmId);
        command.Parameters.AddWithValue(limit);
        if (where.Contains("$3", StringComparison.Ordinal))
        {
            command.Parameters.AddWithValue(now);
        }

        return await ReadAllAsync(command, now, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderEvent>> EventsAsync(Guid orderId, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("select type, recorded_at, source, detail::text from order_events where order_id = $1 order by id");
        command.Parameters.AddWithValue(orderId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var events = new List<OrderEvent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new OrderEvent(reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return events;
    }

    /// <summary>The firm's order with the provider's payment id, for example the Stripe PaymentIntent of a refund.</summary>
    public async Task<Order?> FindByPaymentAsync(string firmId, PaymentProvider provider, string paymentReference, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await FindAsync(
            connection,
            "firm_id = $1 and provider = $2 and payment_reference = $3",
            [firmId, provider.ToString(), paymentReference],
            forUpdate: false,
            now,
            cancellationToken);
    }

    /// <summary>The platform emailed the buyer an invitation to the portal.</summary>
    public async Task SetInviteSentAsync(Guid orderId, string source, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ExecuteAsync(connection, "update orders set invite_sent_at = $2 where id = $1", [orderId, now], cancellationToken);
        await AddEventAsync(connection, orderId, "invite_sent", source, null, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>The order locked until the caller's transaction ends, so only one change to it happens at a time.</summary>
    public static Task<Order?> LockAsync(NpgsqlConnection connection, string firmId, Guid id, DateTimeOffset now, CancellationToken cancellationToken) =>
        FindAsync(connection, "firm_id = $1 and id = $2", [firmId, id], forUpdate: true, now, cancellationToken);

    public static Task UpdateAsync(NpgsqlConnection connection, Guid id, string set, object[] values, CancellationToken cancellationToken) =>
        ExecuteAsync(connection, $"update orders set {set} where id = $1", [id, .. values], cancellationToken);

    public static Task AddEventAsync(
        NpgsqlConnection connection,
        Guid orderId,
        string type,
        string source,
        string? detail,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            "insert into order_events (order_id, type, recorded_at, source, detail) values ($1, $2, $3, $4, $5)",
            [orderId, type, now, source, new NpgsqlParameter { Value = (object?)detail ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Jsonb }],
            cancellationToken);

    public static NpgsqlParameter Text(string? value) => new() { Value = (object?)value ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Text };

    private static async Task<Order?> FindAsync(
        NpgsqlConnection connection,
        string where,
        object[] parameters,
        bool forUpdate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"{SelectOrder} where {where}{(forUpdate ? " for update" : "")}", connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter);
        }

        return (await ReadAllAsync(command, now, cancellationToken)).SingleOrDefault();
    }

    private static async Task<IReadOnlyList<Order>> ReadAllAsync(NpgsqlCommand command, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var orders = new List<Order>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var status = Enum.Parse<OrderStatus>(reader.GetString(8));
            var expiresAt = reader.GetFieldValue<DateTimeOffset>(15);
            orders.Add(new Order(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetDecimal(5),
                reader.GetString(6),
                Enum.Parse<PaymentProvider>(reader.GetString(7)),
                status == OrderStatus.Pending && expiresAt <= now ? OrderStatus.Expired : status,
                reader.IsDBNull(9) ? null : reader.GetString(9),
                new Uri(reader.GetString(10)),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetGuid(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.GetFieldValue<DateTimeOffset>(14),
                expiresAt,
                NullableTime(reader, 16),
                NullableTime(reader, 17),
                NullableTime(reader, 18),
                NullableTime(reader, 19),
                reader.GetFieldValue<byte[]>(20),
                reader.IsDBNull(21) ? null : reader.GetString(21),
                reader.IsDBNull(22) ? null : reader.GetString(22),
                reader.IsDBNull(23) ? null : reader.GetString(23),
                reader.IsDBNull(24) ? null : reader.GetDecimal(24)));
        }

        return orders;
    }

    private static DateTimeOffset? NullableTime(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, object[] parameters, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter as NpgsqlParameter ?? new NpgsqlParameter { Value = parameter });
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
