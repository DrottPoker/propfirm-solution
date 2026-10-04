using System.Globalization;

using Microsoft.Extensions.Options;

using Npgsql;

using Prop.Api.Configuration;
using Prop.Api.Firms;
using Prop.Rules;

namespace Prop.Api.Email;

/// <summary>
/// The emails about what happens to a firm's traders (ADR 0025): to the firm's administrators when something waits for them,
/// and to the trader when the challenge moves on. Each kind can be turned off by the firm; a kind it never set is on.
/// </summary>
internal static class NotificationKinds
{
    /// <summary>To the administrators: a challenge was bought in the portal.</summary>
    public const string FirmSale = "firmSale";

    /// <summary>To the administrators: a trader passed and waits for the funded account.</summary>
    public const string FirmFundingAwaited = "firmFundingAwaited";

    /// <summary>To the administrators: a funded trader asked for a payout.</summary>
    public const string FirmPayoutRequested = "firmPayoutRequested";

    /// <summary>To the trader: an evaluation stage is passed and the next one starts.</summary>
    public const string TraderStagePassed = "traderStagePassed";

    /// <summary>To the trader: every evaluation stage is passed, and the firm reviews the funded account.</summary>
    public const string TraderPassed = "traderPassed";

    /// <summary>To the trader: the funded account is open.</summary>
    public const string TraderFunded = "traderFunded";

    /// <summary>To the trader: the challenge ended, by a broken loss limit, a time limit or inactivity.</summary>
    public const string TraderEnded = "traderEnded";

    /// <summary>To the trader: a payout was approved, paid or rejected.</summary>
    public const string TraderPayouts = "traderPayouts";

    /// <summary>To the trader: the challenge ends soon unless a trade is opened.</summary>
    public const string TraderInactivity = "traderInactivity";

    public static readonly IReadOnlyList<string> All =
    [
        FirmSale, FirmFundingAwaited, FirmPayoutRequested, TraderStagePassed, TraderPassed, TraderFunded, TraderEnded, TraderPayouts, TraderInactivity,
    ];

    /// <summary>Whether the firm sends the kind. Kinds the firm never set are on.</summary>
    public static bool IsOn(Firm firm, string kind) => firm.EmailSettings is null || !firm.EmailSettings.TryGetValue(kind, out var on) || on;
}

/// <summary>A challenge account as the notification emails need it.</summary>
internal sealed record NotifiedAccount(Guid Id, long Number, string Email, ChallengeDefinition Definition);

/// <summary>
/// Queues the notification emails for a decision of the rule engine or a paid order, in the caller's transaction, so an
/// email goes out if and only if the decision is saved. The caller wakes the email worker after committing.
/// </summary>
internal sealed class Notifications(IOptions<PlatformOptions> platform)
{
    /// <summary>The trader is reminded when the last day to open a trade is at most this many days away.</summary>
    public const int InactivityReminderDays = 3;

    public async Task QueueAsync(NpgsqlConnection connection, Firm firm, NotifiedAccount account, ChallengeOutput output, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var definition = account.Definition;
        var accountUrl = new Uri(firm.Portal.Url, $"accounts/{account.Id}");
        switch (output)
        {
            case StagePassed passed when passed.Stage < definition.FundedStage - 1:
                await ToTraderAsync(connection, firm, account, NotificationKinds.TraderStagePassed, StagePassedEmail(firm, account, passed, accountUrl), now, cancellationToken);
                break;
            case FundingAwaited:
                await ToTraderAsync(connection, firm, account, NotificationKinds.TraderPassed, PassedEmail(firm, account, accountUrl), now, cancellationToken);
                await ToAdminsAsync(connection, firm, NotificationKinds.FirmFundingAwaited, to => FundingAwaitedEmail(firm, account, to), now, cancellationToken);
                break;
            case StageStarted started when started.Stage == definition.FundedStage:
                await ToTraderAsync(connection, firm, account, NotificationKinds.TraderFunded, FundedEmail(firm, account, accountUrl), now, cancellationToken);
                break;
            case ChallengeFailed failed:
                await ToTraderAsync(connection, firm, account, NotificationKinds.TraderEnded, FailedEmail(firm, account, failed, accountUrl), now, cancellationToken);
                break;
            case ChallengeExpired expired:
                await ToTraderAsync(connection, firm, account, NotificationKinds.TraderEnded, ExpiredEmail(firm, account, expired, accountUrl), now, cancellationToken);
                break;
            case PayoutWithdrawn withdrawn:
                await ToAdminsAsync(connection, firm, NotificationKinds.FirmPayoutRequested, to => PayoutRequestedEmail(firm, account, withdrawn.Payout, to), now, cancellationToken);
                break;
            case PayoutApproved approved:
                await ToTraderAsync(connection, firm, account, NotificationKinds.TraderPayouts, PayoutApprovedEmail(firm, account, approved.Payout, accountUrl), now, cancellationToken);
                break;
            case PayoutPaid paid:
                await ToTraderAsync(connection, firm, account, NotificationKinds.TraderPayouts, PayoutPaidEmail(firm, account, paid, accountUrl), now, cancellationToken);
                break;
            case PayoutRejected rejected:
                await ToTraderAsync(connection, firm, account, NotificationKinds.TraderPayouts, PayoutRejectedEmail(firm, account, rejected, accountUrl), now, cancellationToken);
                break;
            default:
                break;
        }
    }

    /// <summary>A challenge was bought in the firm's portal and paid for.</summary>
    public Task QueueSaleAsync(
        NpgsqlConnection connection,
        Firm firm,
        string buyer,
        string challengeName,
        long orderNumber,
        decimal amount,
        string currency,
        Guid? accountId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ToAdminsAsync(
            connection,
            firm,
            NotificationKinds.FirmSale,
            to => SaleEmail(firm, buyer, challengeName, orderNumber, amount, currency, accountId, to),
            now,
            cancellationToken);

    /// <summary>
    /// Reminds the trader that the challenge ends on <paramref name="endsOn"/> unless a trade is opened before. Queued once per
    /// deadline, however often it is asked for.
    /// </summary>
    public static Task QueueInactivityReminderAsync(
        NpgsqlConnection connection,
        Firm firm,
        NotifiedAccount account,
        DateOnly endsOn,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var lastDay = endsOn.AddDays(-1);
        var message = TraderEmails.Create(
            firm,
            account.Email,
            $"Open a trade by {Day(lastDay)} to keep your {account.Definition.Name}",
            [$"No new trade has been opened on your {account.Definition.Name} (account #{account.Number}) for a while. Open a new trade by {Day(lastDay)}, or the challenge ends when the trading day of {Day(endsOn)} starts."],
            "Open your account",
            new Uri(firm.Portal.Url, $"accounts/{account.Id}"));
        return NotificationKinds.IsOn(firm, NotificationKinds.TraderInactivity)
            ? EmailOutbox.AddAsync(connection, message, NotificationKinds.TraderInactivity, firm.Id, now, cancellationToken, $"inactivity:{account.Id}:{endsOn:yyyy-MM-dd}")
            : Task.CompletedTask;
    }

    private static Task ToTraderAsync(
        NpgsqlConnection connection,
        Firm firm,
        NotifiedAccount account,
        string kind,
        EmailMessage message,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        NotificationKinds.IsOn(firm, kind) ? EmailOutbox.AddAsync(connection, message, kind, firm.Id, now, cancellationToken) : Task.CompletedTask;

    private static async Task ToAdminsAsync(
        NpgsqlConnection connection,
        Firm firm,
        string kind,
        Func<string, EmailMessage> message,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!NotificationKinds.IsOn(firm, kind))
        {
            return;
        }

        var admins = new List<string>();
        await using (var command = new NpgsqlCommand("select email from firm_admins where firm_id = $1 order by created_at", connection))
        {
            command.Parameters.AddWithValue(firm.Id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                admins.Add(reader.GetString(0));
            }
        }

        foreach (var admin in admins)
        {
            await EmailOutbox.AddAsync(connection, message(admin), kind, firm.Id, now, cancellationToken);
        }
    }

    private static EmailMessage StagePassedEmail(Firm firm, NotifiedAccount account, StagePassed passed, Uri accountUrl)
    {
        var definition = account.Definition;
        var stage = definition.Stage(passed.Stage).Name;
        var next = definition.Stage(passed.Stage + 1).Name;
        return ToTrader(
            firm,
            account,
            $"You passed {stage} of your {definition.Name}",
            $"Well done! You passed {stage} of your {definition.Name} (account #{account.Number}) with a balance of {Money(passed.Balance, definition.Currency)}. {next} starts now, on a new trading account with {Money(definition.InitialBalance, definition.Currency)}. Open it from your account in the portal:",
            accountUrl);
    }

    private static EmailMessage PassedEmail(Firm firm, NotifiedAccount account, Uri accountUrl) =>
        ToTrader(
            firm,
            account,
            $"You passed your {account.Definition.Name}",
            $"Well done! You passed every phase of your {account.Definition.Name} (account #{account.Number}). {firm.Name} now reviews your account, and you get an email when your funded account is ready.",
            accountUrl);

    private static EmailMessage FundedEmail(Firm firm, NotifiedAccount account, Uri accountUrl)
    {
        var definition = account.Definition;
        var split = definition.Funded.ProfitSplitPercent is { } percent ? $" You keep {percent.ToString("0.##", CultureInfo.InvariantCulture)}% of the profit you make." : "";
        return ToTrader(
            firm,
            account,
            $"Your funded account with {firm.Name} is ready",
            $"Your funded account for {definition.Name} (account #{account.Number}) is open, with {Money(definition.InitialBalance, definition.Currency)}.{split} Open the terminal from your account in the portal:",
            accountUrl);
    }

    private static EmailMessage FailedEmail(Firm firm, NotifiedAccount account, ChallengeFailed failed, Uri accountUrl)
    {
        var limit = failed.Reason switch
        {
            FailureReason.DailyLoss => "the daily loss limit",
            FailureReason.MaxLoss => "the max loss limit",
            _ => "a loss limit",
        };
        var currency = account.Definition.Currency;
        return ToTrader(
            firm,
            account,
            $"Your {account.Definition.Name} has ended",
            $"Your {account.Definition.Name} (account #{account.Number}) ended on {Day(DateOnly.FromDateTime(failed.Time.UtcDateTime))}: equity {Money(failed.Equity, currency)} fell below {limit} at {Money(failed.Level, currency)}, so the open trades were closed. See what happened, and start a new challenge, in the portal:",
            accountUrl);
    }

    private static EmailMessage ExpiredEmail(Firm firm, NotifiedAccount account, ChallengeExpired expired, Uri accountUrl)
    {
        var definition = account.Definition;
        var why = expired.Reason == ExpiryReason.Inactivity
            ? $"no new trade was opened for {definition.InactivityDays} days"
            : $"{definition.Stage(expired.Stage).Name} was not passed within its time limit";
        return ToTrader(
            firm,
            account,
            $"Your {definition.Name} has ended",
            $"Your {definition.Name} (account #{account.Number}) ended when the trading day of {Day(expired.Day)} started, since {why}. See your account, and start a new challenge, in the portal:",
            accountUrl);
    }

    private static EmailMessage PayoutApprovedEmail(Firm firm, NotifiedAccount account, Payout payout, Uri accountUrl) =>
        ToTrader(
            firm,
            account,
            $"Your payout of {Money(payout.Amount, account.Definition.Currency)} is approved",
            $"{firm.Name} has approved your payout of {Money(payout.Amount, account.Definition.Currency)} from account #{account.Number}, and is sending the money. You get an email when it is paid.",
            accountUrl);

    private static EmailMessage PayoutPaidEmail(Firm firm, NotifiedAccount account, PayoutPaid paid, Uri accountUrl) =>
        ToTrader(
            firm,
            account,
            $"Your payout of {Money(paid.Payout.Amount, account.Definition.Currency)} is paid",
            $"{firm.Name} has paid your payout of {Money(paid.Payout.Amount, account.Definition.Currency)} from account #{account.Number}.{(paid.Reference is { } reference ? $" The payment's reference is {reference}." : "")}",
            accountUrl);

    private static EmailMessage PayoutRejectedEmail(Firm firm, NotifiedAccount account, PayoutRejected rejected, Uri accountUrl) =>
        ToTrader(
            firm,
            account,
            $"Your payout of {Money(rejected.Payout.Amount, account.Definition.Currency)} was rejected",
            [
                $"{firm.Name} has rejected your payout of {Money(rejected.Payout.Amount, account.Definition.Currency)} from account #{account.Number}:",
                rejected.Reason,
                rejected.ProfitReturned
                    ? $"The profit of {Money(rejected.Payout.Profit, account.Definition.Currency)} is back on your account, so you can ask for the payout again later."
                    : "The profit that was taken off the account for the payout is not returned.",
                $"Ask {firm.Name} if you have questions.",
            ],
            accountUrl);

    private static EmailMessage ToTrader(Firm firm, NotifiedAccount account, string subject, string text, Uri accountUrl) =>
        ToTrader(firm, account, subject, [text], accountUrl);

    private static EmailMessage ToTrader(Firm firm, NotifiedAccount account, string subject, IReadOnlyList<string> paragraphs, Uri accountUrl) =>
        TraderEmails.Create(firm, account.Email, subject, paragraphs, "Open your account", accountUrl);

    private EmailMessage FundingAwaitedEmail(Firm firm, NotifiedAccount account, string to) =>
        ToAdmin(
            to,
            $"{account.Email} passed {account.Definition.Name}",
            $"{account.Email} passed every phase of {account.Definition.Name}, and account #{account.Number} waits for your approval of the funded account. Approve it when your checks, such as KYC, are done:",
            new Uri(firm.Portal.Url, $"admin/accounts/{account.Id}"));

    private EmailMessage PayoutRequestedEmail(Firm firm, NotifiedAccount account, Payout payout, string to) =>
        ToAdmin(
            to,
            $"Payout request of {Money(payout.Amount, account.Definition.Currency)} from {account.Email}",
            $"{account.Email} asked for a payout of {Money(payout.Amount, account.Definition.Currency)} from account #{account.Number}, {payout.ProfitSplitPercent.ToString("0.##", CultureInfo.InvariantCulture)}% of a profit of {Money(payout.Profit, account.Definition.Currency)}. Approve it after your checks, send the money and mark it as paid:",
            new Uri(firm.Portal.Url, "admin/payouts"));

    private EmailMessage SaleEmail(Firm firm, string buyer, string challengeName, long orderNumber, decimal amount, string currency, Guid? accountId, string to) =>
        ToAdmin(
            to,
            $"New sale: {challengeName} for {Money(amount, currency)}",
            $"{buyer} bought {challengeName} for {Money(amount, currency)} in your portal (order #{orderNumber}).{(accountId is null ? " No account could be started for it, so see the order in your admin panel." : " The challenge has started.")}",
            new Uri(firm.Portal.Url, accountId is { } id ? $"admin/accounts/{id}" : "admin/orders"));

    private EmailMessage ToAdmin(string to, string subject, string text, Uri link) =>
        new(
            to,
            subject,
            $"""
            Hi,

            {text}

            {link}

            You get this email as an administrator of the firm. Choose which emails the firm sends under Notifications in the admin panel.

            {platform.Value.Name}
            """);

    private static string Money(decimal amount, string currency) => $"{amount.ToString("N2", CultureInfo.InvariantCulture)} {currency}";

    private static string Day(DateOnly day) => day.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}
