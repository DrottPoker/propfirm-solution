using Common.Postgres;

using Microsoft.Extensions.Options;

using Npgsql;

using Prop.Api.Configuration;
using Prop.Api.Firms;

namespace Prop.Api.Billing;

/// <summary>Where a firm's limit on open challenges comes from.</summary>
public enum SlotLimit
{
    /// <summary>The sandbox's limit, before the firm is live.</summary>
    Sandbox,

    /// <summary>The slots the firm has paid for this month.</summary>
    Paid,

    /// <summary>Slots without charges, for configured firms.</summary>
    Complimentary,

    /// <summary>No limit.</summary>
    Unlimited,
}

/// <summary>Why no challenge can start now.</summary>
public enum StartRefusal
{
    SandboxFull,
    NoFreeSlots,

    /// <summary>The firm's month is not paid.</summary>
    Unpaid,

    /// <summary>We have suspended the firm.</summary>
    Suspended,
}

/// <summary>
/// How a firm uses its slots. <paramref name="Used"/> counts challenges that have not ended, and
/// <paramref name="Reserved"/> orders in the portal that wait for payment, which hold a slot each so a buyer never
/// pays for a challenge that cannot start. <paramref name="Slots"/> is null when there is no limit. Nothing can
/// start while the firm is <paramref name="Suspended"/>.
/// </summary>
internal sealed record SlotUsage(SlotLimit Limit, int? Slots, int Used, int Reserved, bool Paid, bool Suspended)
{
    public int? Free => Slots is { } slots ? Math.Max(0, slots - Used - Reserved) : null;

    public StartRefusal? Refusal =>
        Suspended ? StartRefusal.Suspended
        : !Paid ? StartRefusal.Unpaid
        : Free == 0 ? Limit == SlotLimit.Sandbox ? StartRefusal.SandboxFull : StartRefusal.NoFreeSlots
        : null;

    public bool HasRoom => Refusal is null;

    /// <summary>Whether at least <paramref name="percent"/> of a paid or complimentary limit is used.</summary>
    public bool IsNearlyFull(int percent) =>
        Limit is SlotLimit.Paid or SlotLimit.Complimentary && Slots is > 0 and { } slots && (Used + Reserved) * 100L >= (long)slots * percent;
}

/// <summary>
/// The firms' slots: how many challenges each can have open at once, and how many are used. Starts and orders
/// lock a firm's slots in their transaction, so two of them can never take the last free slot together.
/// </summary>
internal sealed class SlotService(NpgsqlDataSource dataSource, DatabaseSchema schema, IOptions<SandboxOptions> sandbox, TimeProvider time)
{
    public static string RefusalMessage(StartRefusal refusal) => refusal switch
    {
        StartRefusal.SandboxFull => "The sandbox has room for no more open challenge accounts. Cancel one to start another, or go live.",
        StartRefusal.NoFreeSlots => "Every slot is taken. Buy more slots in the admin panel, or wait until a challenge ends.",
        StartRefusal.Suspended => "The firm is suspended, so no challenges can start.",
        _ => "This month is not paid, so no challenges can start. Pay it in the admin panel.",
    };

    /// <summary>Why a paid order started no account.</summary>
    public static string OrderProblem(StartRefusal refusal) => refusal switch
    {
        StartRefusal.SandboxFull => "The sandbox had no room for another open challenge account, so none was started.",
        StartRefusal.NoFreeSlots => "Every slot was taken when the payment came, so no account was started. Start it when a slot is free.",
        StartRefusal.Suspended => "The firm was suspended when the payment came, so no account was started.",
        _ => "The firm's month was not paid when the payment came, so no account was started. Start it once the month is paid.",
    };

    /// <summary>Holds the firm's slots until the caller's transaction ends.</summary>
    public static async Task LockAsync(NpgsqlConnection connection, string firmId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("select pg_advisory_xact_lock(hashtextextended('slots:' || $1, 0))", connection);
        command.Parameters.AddWithValue(firmId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SlotUsage> UsageAsync(Firm firm, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await UsageAsync(connection, firm, null, cancellationToken);
    }

    /// <summary>The firm's usage now. <paramref name="exceptOrder"/> is an order being paid, whose reservation becomes its account.</summary>
    public async Task<SlotUsage> UsageAsync(NpgsqlConnection connection, Firm firm, Guid? exceptOrder, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var used = await CountAsync(
            connection,
            "select count(*) from challenge_accounts where firm_id = $1 and status not in ('Failed', 'Cancelled')",
            [firm.Id],
            cancellationToken);
        var reserved = await CountAsync(
            connection,
            "select count(*) from orders where firm_id = $1 and status = 'Pending' and expires_at > $2 and challenge_account_id is null and id is distinct from $3",
            [firm.Id, now, new NpgsqlParameter { Value = (object?)exceptOrder ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid }],
            cancellationToken);

        // As saved, since the firm may have gone live or been suspended a moment ago.
        var state = await BillingStore.GoLiveStateAsync(connection, firm.Id, forUpdate: false, cancellationToken);
        if (state.Status != FirmStatus.Live)
        {
            return new SlotUsage(SlotLimit.Sandbox, sandbox.Value.MaxOpenAccounts, used, reserved, Paid: true, state.Suspended);
        }

        var billing = await BillingStore.GetBillingAsync(connection, firm.Id, forUpdate: false, cancellationToken);
        switch (billing)
        {
            case null or { Plan: BillingPlan.Complimentary, Slots: null }:
                return new SlotUsage(SlotLimit.Unlimited, null, used, reserved, Paid: true, state.Suspended);
            case { Plan: BillingPlan.Complimentary, Slots: { } slots }:
                return new SlotUsage(SlotLimit.Complimentary, slots, used, reserved, Paid: true, state.Suspended);
            default:
                var month = BillingRules.MonthOf(now);
                var latest = await BillingStore.LatestPeriodAsync(connection, firm.Id, month, cancellationToken);
                return new SlotUsage(SlotLimit.Paid, latest?.Slots ?? billing.Slots ?? 0, used, reserved, Paid: latest?.Month == month, state.Suspended);
        }
    }

    private static async Task<int> CountAsync(NpgsqlConnection connection, string sql, object[] parameters, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter as NpgsqlParameter ?? new NpgsqlParameter { Value = parameter });
        }

        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
