using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

using Common.Postgres;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using Npgsql;

using Prop.Api.Challenges;
using Prop.Api.Configuration;
using Prop.Api.Email;
using Prop.Api.Firms;
using Prop.Api.Portal;
using Prop.Api.Trading;

namespace Prop.Api.Signup;

/// <summary>Whether a short name can be chosen, and why not, with free names like it when it is taken.</summary>
internal sealed record Availability(bool Available, string? Reason, IReadOnlyList<string>? Suggestions = null)
{
    public static readonly Availability Yes = new(true, null);
}

internal abstract record SignupOutcome
{
    /// <summary>A field is wrong. <paramref name="Field"/> names it, as in the request.</summary>
    public sealed record Invalid(string Field, string Problem) : SignupOutcome;

    public sealed record Taken(string Problem) : SignupOutcome;

    /// <summary>The email with the confirmation link is sent.</summary>
    public sealed record VerificationSent : SignupOutcome;

    /// <summary>The firm is created. The link logs its administrator in on the firm's portal.</summary>
    public sealed record Completed(string FirmId, Uri AdminUrl) : SignupOutcome;

    public sealed record EmailNotSent : SignupOutcome;

    /// <summary>The address got <see cref="SignupOptions.MaxEmailsPerDay"/> confirmation emails in the last day (ADR 0045).</summary>
    public sealed record TooManyEmails : SignupOutcome;
}

/// <summary>
/// Firms that sign up themselves (ADR 0017). A sign-up waits for the email address to be confirmed, unless that is
/// turned off. Confirming creates the firm, its portal address, its administrator and a one-time login link in one
/// transaction, and wakes the job that creates its server on the trading platform.
/// </summary>
internal sealed partial class SignupService(
    NpgsqlDataSource dataSource,
    DatabaseSchema schema,
    FirmCatalog firms,
    FirmStore store,
    ITradingPartner partner,
    IPasswordHasher<PortalUser> hasher,
    IEmailSender email,
    WorkSignals signals,
    IOptions<SignupOptions> signup,
    IOptions<PlatformOptions> platform,
    IOptions<LoginOptions> login,
    TimeProvider time,
    ILogger<SignupService> logger)
{
    public static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(24);

    public const int MaxEmailLength = 254;

    /// <summary>How many free names are suggested for one that is taken or reserved.</summary>
    public const int MaxSuggestions = 3;

    // Endings that keep the firm's name recognisable, tried in this order.
    private static readonly string[] SuggestionEndings = ["capital", "fx", "trading", "funded", "prop", "hq", "markets", "group"];

    /// <summary>Whether the short name can be chosen now by the person with the email. Asks the trading platform too when told to.</summary>
    public async Task<Availability> CheckAsync(string? firmId, string? emailAddress, bool askTradingPlatform, CancellationToken cancellationToken)
    {
        if (!FirmRules.IsValidId(firmId))
        {
            return new Availability(false, "Use 2 to 40 lowercase letters, digits and dashes, not first or last.");
        }

        if (IsReserved(firmId!))
        {
            return new Availability(false, "That name is reserved.", await SuggestAsync(firmId!, emailAddress, cancellationToken));
        }

        if (!await IsFreeAsync(firmId!, emailAddress, cancellationToken))
        {
            return new Availability(false, "That name is taken.", await SuggestAsync(firmId!, emailAddress, cancellationToken));
        }

        if (askTradingPlatform)
        {
            try
            {
                if (!await partner.IsServerAvailableAsync(firmId!, cancellationToken))
                {
                    return new Availability(false, "That name is taken.");
                }
            }
            catch (TradingPlatformUnavailableException exception)
            {
                // The server is created later and tried again, so a sign-up need not wait for the trading platform.
                LogTradingPlatformUnavailable(logger, exception);
            }
        }

        return Availability.Yes;
    }

    public async Task<SignupOutcome> SignUpAsync(
        string? firmName,
        string? firmId,
        string? emailAddress,
        string? password,
        bool acceptTerms,
        string? currency,
        CancellationToken cancellationToken)
    {
        if (!FirmRules.IsValidName(firmName) || firmName!.Trim().Length < 2)
        {
            return new SignupOutcome.Invalid("firmName", $"The firm's name needs 2 to {FirmRules.MaxNameLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(emailAddress) || !emailAddress.Contains('@', StringComparison.Ordinal) || emailAddress.Trim().Length > MaxEmailLength)
        {
            return new SignupOutcome.Invalid("email", "A valid email address is required.");
        }

        var minimumLength = login.Value.MinimumPasswordLength;
        if (password is null || password.Length < minimumLength)
        {
            return new SignupOutcome.Invalid("password", minimumLength == 1 ? "Choose a password." : $"The password needs at least {minimumLength} characters.");
        }

        if (!acceptTerms)
        {
            return new SignupOutcome.Invalid("acceptTerms", "Accept the terms and the data processing agreement to sign up.");
        }

        var accountCurrency = string.IsNullOrWhiteSpace(currency) ? signup.Value.DefaultCurrency : currency.Trim().ToUpperInvariant();
        if (!signup.Value.Currencies.Contains(accountCurrency, StringComparer.Ordinal) && accountCurrency != signup.Value.DefaultCurrency)
        {
            return new SignupOutcome.Invalid("currency", $"Choose the accounts' currency: {string.Join(", ", signup.Value.Currencies)}.");
        }

        var availability = await CheckAsync(firmId, emailAddress, askTradingPlatform: true, cancellationToken);
        if (!availability.Available)
        {
            return FirmRules.IsValidId(firmId) ? new SignupOutcome.Taken(availability.Reason!) : new SignupOutcome.Invalid("firmId", availability.Reason!);
        }

        var now = time.GetUtcNow();
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        // Each sign-up with confirmation emails the address, so an address gets a few in a day at most.
        var emailsLeft = signup.Value.RequireEmailVerification ? signup.Value.MaxEmailsPerDay : (int?)null;
        if (!await SaveAsync(token, firmId!, firmName.Trim(), emailAddress.Trim(), hasher.HashPassword(null!, password), accountCurrency, emailsLeft, now, cancellationToken))
        {
            return new SignupOutcome.TooManyEmails();
        }

        if (!signup.Value.RequireEmailVerification)
        {
            return await VerifyAsync(token, cancellationToken) switch
            {
                SignupOutcome.Completed completed => completed,
                _ => new SignupOutcome.Taken("That name is taken."),
            };
        }

        try
        {
            var link = new Uri(platform.Value.Url!, $"verify?token={token}");
            await email.SendAsync(PlatformEmails.ConfirmSignup(platform.Value.Name, emailAddress.Trim(), link, VerificationLifetime), cancellationToken);
            return new SignupOutcome.VerificationSent();
        }
        catch (EmailNotSentException exception)
        {
            LogEmailNotSent(logger, exception);
            await DeleteAsync(token, cancellationToken);
            return new SignupOutcome.EmailNotSent();
        }
    }

    /// <summary>
    /// Creates the firm from a confirmed sign-up. Invalid when the link is unknown, used or expired, Taken when
    /// someone else got the name first.
    /// </summary>
    public async Task<SignupOutcome> VerifyAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(token))
        {
            return new SignupOutcome.Invalid("token", "The link has expired or was already used.");
        }

        await schema.EnsureAsync(cancellationToken);
        var now = time.GetUtcNow();
        string firmId;
        Uri portalUrl;
        string loginToken;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            await using var use = new NpgsqlCommand(
                """
                update firm_signups set used_at = $2
                where token_hash = $1 and used_at is null and expires_at > $2
                returning firm_id, firm_name, email, password_hash, terms_version, currency
                """,
                connection);
            use.Parameters.AddWithValue(Hash(token));
            use.Parameters.AddWithValue(now);
            string firmName, adminEmail, passwordHash, termsVersion, currency;
            await using (var reader = await use.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    return new SignupOutcome.Invalid("token", "The link has expired or was already used.");
                }

                (firmId, firmName, adminEmail, passwordHash, termsVersion, currency) =
                    (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5));
            }

            portalUrl = platform.Value.PortalUrlOf(firmId);
            if (!await FirmStore.InsertSignedUpAsync(connection, firmId, firmName, portalUrl, termsVersion, currency, now, cancellationToken))
            {
                return new SignupOutcome.Taken("Someone else took that name first. Sign up again with another one.");
            }

            var adminId = await FirmAdmins.InsertAsync(connection, firmId, adminEmail, passwordHash, now, cancellationToken);
            loginToken = await FirmAdmins.CreateLoginLinkAsync(connection, adminId, now, cancellationToken);

            // Sent with the firm, so the administrator can find the admin panel again.
            await EmailOutbox.AddAsync(
                connection, PlatformEmails.Welcome(platform.Value.Name, firmName, adminEmail, new Uri(portalUrl, "admin/login")), "welcome", firmId, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        firms.Put(await store.GetAsync(firmId, cancellationToken) ?? throw new InvalidOperationException($"Firm {firmId} disappeared."));
        signals.Provisioning.Set();
        signals.Emails.Set();
        LogSignedUp(logger, firmId);
        return new SignupOutcome.Completed(firmId, new Uri(portalUrl, $"admin/welcome?token={loginToken}&next=%2Fadmin%2Fget-started"));
    }

    // Free means no firm has it, and nobody else signed up with it and may still confirm it.
    private async Task<bool> IsFreeAsync(string firmId, string? emailAddress, CancellationToken cancellationToken) =>
        firms.ById(firmId) is null && !await IsPendingForOtherAsync(firmId, emailAddress, cancellationToken);

    /// <summary>Free names like the one that is taken, with a common ending added. Does not ask the trading platform.</summary>
    private async Task<IReadOnlyList<string>> SuggestAsync(string firmId, string? emailAddress, CancellationToken cancellationToken)
    {
        var suggestions = new List<string>();
        foreach (var ending in SuggestionEndings)
        {
            var candidate = $"{firmId}-{ending}";
            if (FirmRules.IsValidId(candidate) && !IsReserved(candidate) && await IsFreeAsync(candidate, emailAddress, cancellationToken))
            {
                suggestions.Add(candidate);
                if (suggestions.Count == MaxSuggestions)
                {
                    break;
                }
            }
        }

        return suggestions;
    }

    private bool IsReserved(string firmId) =>
        FirmRules.ReservedIds.Contains(firmId)
        || signup.Value.ReservedFirmIds.Contains(firmId, StringComparer.Ordinal)
        || platform.Value.Url?.Host.Split('.')[0] == firmId;

    // Someone else signed up with the name and may still confirm it.
    private async Task<bool> IsPendingForOtherAsync(string firmId, string? emailAddress, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            "select exists (select 1 from firm_signups where firm_id = $1 and normalized_email <> $2 and used_at is null and expires_at > $3)");
        command.Parameters.AddWithValue(firmId);
        command.Parameters.AddWithValue(Emails.Normalize(emailAddress ?? ""));
        command.Parameters.AddWithValue(time.GetUtcNow());
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    // A new sign-up replaces the person's earlier unconfirmed ones, which expire, so they still count toward
    // emailsPerDay. False when the address had that many in the last day. Sign-ups that expired long ago are removed.
    private async Task<bool> SaveAsync(
        string token,
        string firmId,
        string firmName,
        string emailAddress,
        string passwordHash,
        string currency,
        int? emailsPerDay,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var normalized = Emails.Normalize(emailAddress);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (emailsPerDay is { } most)
        {
            // Sign-ups with the same address wait for each other, so the limit holds.
            await ExecuteAsync(connection, "select pg_advisory_xact_lock(hashtextextended('firm_signups:' || $1, 0))", [normalized], cancellationToken);
            await using var count = new NpgsqlCommand("select count(*) from firm_signups where normalized_email = $1 and created_at > $2", connection);
            count.Parameters.AddWithValue(normalized);
            count.Parameters.AddWithValue(now - TimeSpan.FromDays(1));
            if ((long)(await count.ExecuteScalarAsync(cancellationToken))! >= most)
            {
                return false;
            }
        }

        await ExecuteAsync(
            connection,
            "update firm_signups set expires_at = $2 where normalized_email = $1 and used_at is null and expires_at > $2",
            [normalized, now],
            cancellationToken);
        await ExecuteAsync(connection, "delete from firm_signups where expires_at < $1", [now - TimeSpan.FromDays(30)], cancellationToken);
        await ExecuteAsync(
            connection,
            """
            insert into firm_signups (token_hash, firm_id, firm_name, email, normalized_email, password_hash, terms_version, created_at, expires_at, currency)
            values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
            """,
            [Hash(token), firmId, firmName, emailAddress, normalized, passwordHash, signup.Value.TermsVersion, now, now + VerificationLifetime, currency],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, object[] parameters, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task DeleteAsync(string token, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("delete from firm_signups where token_hash = $1");
        command.Parameters.AddWithValue(Hash(token));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    [LoggerMessage(Level = LogLevel.Information, Message = "Firm {FirmId} signed up")]
    private static partial void LogSignedUp(ILogger logger, string firmId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The trading platform could not say whether a server name is free; the sign-up goes on")]
    private static partial void LogTradingPlatformUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "The confirmation email for a sign-up could not be sent")]
    private static partial void LogEmailNotSent(ILogger logger, Exception exception);
}
