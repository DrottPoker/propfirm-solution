using System.Text.Json;

using Common.Postgres;

using Npgsql;

using Prop.Api.Firms;
using Prop.Api.Json;

namespace Prop.Api.Challenges;

public enum PayoutMethodKind
{
    /// <summary>A bank transfer to the trader's account.</summary>
    Bank,

    /// <summary>A transfer of a crypto currency to the trader's wallet.</summary>
    Crypto,

    /// <summary>Anything else the firm pays with, described by the trader, for example a PayPal address.</summary>
    Other,
}

/// <summary>
/// How a trader wants to be paid (ADR 0026). For <see cref="PayoutMethodKind.Bank"/>: <paramref name="AccountHolder"/>,
/// <paramref name="AccountNumber"/> (an IBAN or a local account number) and optionally <paramref name="BankCode"/> (BIC, SWIFT or
/// routing number) and <paramref name="BankName"/>. For <see cref="PayoutMethodKind.Crypto"/>: <paramref name="Asset"/> (for example
/// USDT), <paramref name="Network"/> (for example TRC20) and <paramref name="Address"/>. For <see cref="PayoutMethodKind.Other"/>:
/// <paramref name="Details"/>. Fields of the other kinds are left out.
/// </summary>
public sealed record PayoutMethod(
    PayoutMethodKind Kind,
    string? AccountHolder = null,
    string? AccountNumber = null,
    string? BankCode = null,
    string? BankName = null,
    string? Asset = null,
    string? Network = null,
    string? Address = null,
    string? Details = null)
{
    private const int MaxLength = 200;

    /// <summary>The method with its fields trimmed, and those of other kinds removed.</summary>
    public PayoutMethod Normalized() => Kind switch
    {
        PayoutMethodKind.Bank => new(Kind, AccountHolder: Clean(AccountHolder), AccountNumber: Clean(AccountNumber), BankCode: Clean(BankCode), BankName: Clean(BankName)),
        PayoutMethodKind.Crypto => new(Kind, Asset: Clean(Asset), Network: Clean(Network), Address: Clean(Address)),
        _ => new(Kind, Details: Clean(Details)),
    };

    /// <summary>What is missing or wrong in a normalized method, or null.</summary>
    public string? Problem()
    {
        var required = Kind switch
        {
            PayoutMethodKind.Bank => new[] { ("the account holder", AccountHolder), ("the account number or IBAN", AccountNumber) },
            PayoutMethodKind.Crypto => [("the currency", Asset), ("the network", Network), ("the wallet address", Address)],
            _ => [("how you want to be paid", Details)],
        };
        if (required.FirstOrDefault(r => r.Item2 is null) is { Item1: { } missing })
        {
            return $"Fill in {missing}.";
        }

        return new[] { AccountHolder, AccountNumber, BankCode, BankName, Asset, Network, Address, Details }.Any(v => v?.Length > MaxLength)
            ? $"Each field can be at most {MaxLength} characters."
            : null;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// The traders' payout methods, encrypted like the firms' secrets, since they are personal and lead to money. A payout keeps
/// a copy of the method it was asked for with, so a later change does not move a payout that is on its way.
/// </summary>
internal sealed class PayoutMethods(NpgsqlDataSource dataSource, DatabaseSchema schema, SecretProtector secrets)
{
    public async Task<PayoutMethod?> GetAsync(Guid traderId, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("select details from trader_payout_methods where trader_id = $1");
        command.Parameters.AddWithValue(traderId);
        return await command.ExecuteScalarAsync(cancellationToken) is string details ? Open(traderId, details) : null;
    }

    /// <summary>Saves a normalized, valid method for the trader, replacing the one before.</summary>
    public async Task SaveAsync(Guid traderId, PayoutMethod method, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            """
            insert into trader_payout_methods (trader_id, details, updated_at) values ($1, $2, $3)
            on conflict (trader_id) do update set details = excluded.details, updated_at = excluded.updated_at
            """);
        command.Parameters.AddWithValue(traderId);
        command.Parameters.AddWithValue(secrets.Protect(JsonSerializer.Serialize(method, PropJson.Options), Purpose(traderId)));
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>A method as stored for the trader, here or as a payout's copy.</summary>
    public PayoutMethod Open(Guid traderId, string details) =>
        JsonSerializer.Deserialize<PayoutMethod>(secrets.Unprotect(details, Purpose(traderId)), PropJson.Options)
        ?? throw new InvalidOperationException($"The payout method of trader {traderId} is empty.");

    private static string Purpose(Guid traderId) => $"trader:{traderId}:payout-method";
}
