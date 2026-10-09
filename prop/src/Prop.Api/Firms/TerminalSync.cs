using Common.Postgres;

using Npgsql;

using NpgsqlTypes;

using Prop.Api.Trading;

namespace Prop.Api.Firms;

/// <summary>
/// Keeps what the terminal shows the firms' traders up to date on the trading platform (ADR 0058): the terminal profile
/// of each firm that signed up, and every trader's name. What was last sent is kept, so only changes are sent, also when a
/// trader changes their name or a firm its terms. The firm's own choices in the profile, such as whether orders ask
/// first, are left as they are.
/// </summary>
internal sealed partial class TerminalSync(
    NpgsqlDataSource dataSource,
    DatabaseSchema schema,
    FirmCatalog firms,
    ITradingPlatform trading,
    TimeProvider time,
    ILogger<TerminalSync> logger) : BackgroundService
{
    /// <summary>How often changes are looked for.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    // Raised when the profile below changes, so every firm is sent it again.
    private const int ProfileVersion = 1;

    private const int NamesPerBatch = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await schema.EnsureAsync(stoppingToken);
        await firms.Ready.WaitAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await UpdateProfilesAsync(stoppingToken);
                await UpdateNamesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, PollInterval, exception);
            }

            try
            {
                await Task.Delay(PollInterval, time, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    /// <summary>
    /// A signed-up firm's terminal: a prop firm with every part shown, whose traders log in through its portal and so not
    /// with a password, with links to the portal's support page and the firm's terms. Whether orders ask first, how
    /// tickets start and a risk warning stay as they are.
    /// </summary>
    public static TerminalProfile ProfileOf(TerminalProfile current, Uri portalUrl, Uri? termsUrl) =>
        current with
        {
            Kind = TerminalKind.Prop,
            Modules = TerminalModules.All,
            PasswordLogin = false,
            Links = new TerminalLinks(null, new Uri(portalUrl, "support"), termsUrl, null, null),
        };

    // A configured firm's terminal comes from the trading platform's configuration, as its server does.
    private async Task UpdateProfilesAsync(CancellationToken cancellationToken)
    {
        const string Sent = "jsonb_build_object('version', $1::integer, 'portalUrl', f.portal_url, 'termsUrl', f.shop_terms_url)";
        var stale = new List<(string Id, string PortalUrl, string? TermsUrl)>();
        await using (var command = dataSource.CreateCommand(
            $"select f.id, f.portal_url, f.shop_terms_url from firms f where not f.configured and f.trading_server is not null and f.trading_terminal is distinct from {Sent}"))
        {
            command.Parameters.AddWithValue(ProfileVersion);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                stale.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
        }

        foreach (var (id, portalUrl, termsUrl) in stale)
        {
            if (firms.ById(id)?.Trading is not { } server)
            {
                continue;
            }

            try
            {
                var current = await trading.GetTerminalProfileAsync(server, cancellationToken);
                var profile = ProfileOf(current, new Uri(portalUrl), termsUrl is null ? null : new Uri(termsUrl));
                if (profile != current)
                {
                    await trading.SetTerminalProfileAsync(server, profile, cancellationToken);
                }
            }
            catch (TradingPlatformRejectedException exception)
            {
                // Tried again in the next round, so a profile the platform cannot use stays in the log.
                LogProfileRefused(logger, id, exception);
                continue;
            }

            // What was sent, not what the firm has now, so a change made meanwhile is sent in the next round.
            await using var update = dataSource.CreateCommand(
                "update firms set trading_terminal = jsonb_build_object('version', $2::integer, 'portalUrl', $3::text, 'termsUrl', $4::text) where id = $1");
            update.Parameters.AddWithValue(id);
            update.Parameters.AddWithValue(ProfileVersion);
            update.Parameters.AddWithValue(portalUrl);
            update.Parameters.Add(new NpgsqlParameter { Value = (object?)termsUrl ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Text });
            await update.ExecuteNonQueryAsync(cancellationToken);
            LogProfileSent(logger, id);
        }
    }

    // The traders whose name the terminal shows differently from the portal, a batch at a time until none is left.
    private async Task UpdateNamesAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var stale = new List<(Guid TraderId, string FirmId, Guid UserId, string? Name)>();
            await using (var command = dataSource.CreateCommand(
                "select id, firm_id, trading_user_id, name from traders where trading_user_id is not null and name is distinct from trading_name order by id limit $1"))
            {
                command.Parameters.AddWithValue(NamesPerBatch);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    stale.Add((reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
                }
            }

            var sent = 0;
            foreach (var (traderId, firmId, userId, name) in stale)
            {
                if (firms.ById(firmId)?.Trading is not { } server)
                {
                    continue;
                }

                try
                {
                    await trading.SetUserNameAsync(server, userId, name, cancellationToken);
                }
                catch (TradingPlatformRejectedException exception)
                {
                    // A name the platform cannot take is not offered again until the trader changes it.
                    LogNameRefused(logger, traderId, exception);
                }

                await using var update = dataSource.CreateCommand("update traders set trading_name = $2 where id = $1");
                update.Parameters.AddWithValue(traderId);
                update.Parameters.Add(new NpgsqlParameter { Value = (object?)name ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Text });
                await update.ExecuteNonQueryAsync(cancellationToken);
                sent++;
            }

            if (stale.Count < NamesPerBatch || sent == 0)
            {
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The trading platform has the terminal profile of firm {FirmId}")]
    private static partial void LogProfileSent(ILogger logger, string firmId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The trading platform refused the terminal profile of firm {FirmId}")]
    private static partial void LogProfileRefused(ILogger logger, string firmId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The trading platform refused the name of trader {TraderId}")]
    private static partial void LogNameRefused(ILogger logger, Guid traderId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Updating the terminals on the trading platform failed; trying again in {Delay}")]
    private static partial void LogFailed(ILogger logger, TimeSpan delay, Exception exception);
}
