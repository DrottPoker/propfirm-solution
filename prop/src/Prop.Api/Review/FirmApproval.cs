using Common.Postgres;

using Npgsql;

using Prop.Api.Challenges;
using Prop.Api.Firms;

namespace Prop.Api.Review;

/// <summary>
/// What a firm may not do before we have approved it (ADR 0043): it reaches only its own administrators, who alone get its
/// emails, buy in its shop and log in as its traders; it sends a few invitations to its team at most; and its portal cannot
/// move to its own domain. A live firm counts as approved.
/// </summary>
internal sealed class FirmApproval(NpgsqlDataSource dataSource, DatabaseSchema schema, FirmCatalog firms)
{
    /// <summary>How long back the invitations to the team are counted.</summary>
    public static readonly TimeSpan InvitePeriod = TimeSpan.FromDays(30);

    /// <summary>Why the firm cannot email, or let log in, someone who is not one of its administrators. For the firm's administrators.</summary>
    public const string TeamOnlyProblem =
        "Until we have approved your firm, it reaches only its administrators: only they get its emails and can log in as its traders. Try the trader's side with your own email address.";

    /// <summary>Why the firm cannot add its own domain yet.</summary>
    public const string DomainProblem = "You can add your own domain once we have approved your firm.";

    /// <summary>Why someone who is not on the firm's team cannot buy in its shop. For the visitor.</summary>
    public static string ShopClosedProblem(Firm firm) => $"{firm.Name} does not sell here yet, so only its own team can buy, to try the shop.";

    /// <summary>Why someone who is not on the firm's team cannot log in to its portal. For the visitor.</summary>
    public static string LoginClosedProblem(Firm firm) => $"{firm.Name} is not open yet, so only its own team can log in here.";

    /// <summary>SQL that is true when we have approved the firm whose id is <paramref name="firmId"/>, or it is live.</summary>
    public static string ApprovedSql(string firmId) =>
        $"""
        exists (select 1 from firms af where af.id = {firmId}
                and (af.status = 'Live' or exists (select 1 from firm_reviews ar where ar.firm_id = af.id and ar.status = 'Approved')))
        """;

    /// <summary>
    /// SQL that is true when the firm may reach the address, given as normalized by <see cref="Emails.Normalize"/>: the firm
    /// is approved, or the address is one of its administrators'.
    /// </summary>
    public static string MayReachSql(string firmId, string normalizedEmail) =>
        $"({ApprovedSql(firmId)} or exists (select 1 from firm_admins aa where aa.firm_id = {firmId} and aa.normalized_email = {normalizedEmail}))";

    public async Task<bool> IsApprovedAsync(string firmId, CancellationToken cancellationToken)
    {
        if (firms.ById(firmId)?.Status == FirmStatus.Live)
        {
            return true;
        }

        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand($"select {ApprovedSql("$1")}");
        command.Parameters.AddWithValue(firmId);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    /// <summary>Whether the firm may reach the address: email it, sell to it, and let it log in as a trader.</summary>
    public async Task<bool> MayReachAsync(string firmId, string email, CancellationToken cancellationToken)
    {
        if (firms.ById(firmId)?.Status == FirmStatus.Live)
        {
            return true;
        }

        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand($"select {MayReachSql("$1", "$2")}");
        command.Parameters.AddWithValue(firmId);
        command.Parameters.AddWithValue(Emails.Normalize(email));
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    /// <summary>Whether the address is one of the firm's administrators'.</summary>
    public async Task<bool> IsAdministratorAsync(string firmId, string email, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("select exists (select 1 from firm_admins where firm_id = $1 and normalized_email = $2)");
        command.Parameters.AddWithValue(firmId);
        command.Parameters.AddWithValue(Emails.Normalize(email));
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }
}
