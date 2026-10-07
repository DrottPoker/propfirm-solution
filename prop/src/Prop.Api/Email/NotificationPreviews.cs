using Prop.Api.Firms;
using Prop.Api.Payments;
using Prop.Api.Support;
using Prop.Rules;

namespace Prop.Api.Email;

/// <summary>Who a notification email goes to: each of the firm's administrators, or the trader it is about.</summary>
public enum EmailAudience
{
    Team,
    Trader,
}

/// <summary>A notification email with sample data, as <see cref="Notifications.Preview"/> builds it. It is never queued or sent.</summary>
internal sealed record NotificationPreview(string Kind, EmailAudience Audience, EmailMessage Message);

/// <summary>
/// Previews of the notification emails (ADR 0025): each kind as it would go out for the firm now, in its name and look and
/// about its own challenge, with a sample trader, account, payout and support ticket. Built by the same code as the real
/// emails, and never queued, sent or saved.
/// </summary>
internal sealed partial class Notifications
{
    /// <summary>The trader in the previews.</summary>
    public const string SampleTraderName = "Alex Example";

    public const string SampleTraderEmail = "alex@example.com";

    /// <summary>What the sample payout pays the trader, in the challenge's currency.</summary>
    public const decimal SamplePayout = 1_600m;

    /// <summary>The sample account's number, and the sample order's.</summary>
    public const long SampleNumber = 1001;

    /// <summary>What the sample sale cost, when the firm has no price for the challenge.</summary>
    private const decimal SamplePrice = 549m;

    private const long SampleTicketNumber = 12;

    private const string SampleQuestion = "Hi, how many trading days do I need on my funded account before I can ask for my first payout?";

    private const string SampleAnswer =
        "Hi Alex, you can ask for a payout from your account's page in the portal once the funded account has enough trading days. The page shows how many are left.";

    private static readonly Guid SampleAccountId = new("0199b6a0-4c2e-7f31-9a5d-3e8b1c7d2f40");
    private static readonly Guid SampleTicketId = new("0199b6a0-4c2e-7f31-9a5d-3e8b1c7d2f41");
    private static readonly Guid SampleTraderId = new("0199b6a0-4c2e-7f31-9a5d-3e8b1c7d2f42");

    /// <summary>
    /// The email of the kind for the firm, about its first challenge that has the phases the email is about, or the two-step
    /// template in the firm's currency when it has none, and a sale at the firm's price for the challenge when it has one.
    /// The team's emails go to <paramref name="admin"/>. Throws for a kind that is not in <see cref="NotificationKinds.All"/>.
    /// </summary>
    public NotificationPreview Preview(
        Firm firm,
        IReadOnlyList<ChallengeDefinition> challenges,
        IReadOnlyList<ChallengePrice> prices,
        string kind,
        string admin,
        DateTimeOffset now)
    {
        var trader = new TraderName(SampleTraderName, SampleTraderEmail);
        var account = Account(0);
        var passed = Account(1);
        var accountUrl = AccountUrl(firm, account);
        return kind switch
        {
            NotificationKinds.FirmSale => ForTeam(SaleSample(firm, account, prices, admin)),
            NotificationKinds.FirmFundingAwaited => ForTeam(FundingAwaitedEmail(firm, passed, trader, admin)),
            NotificationKinds.FirmPayoutRequested => ForTeam(PayoutRequestedEmail(firm, account, PayoutSample(account, now), trader, admin)),
            NotificationKinds.FirmSupport => ForTeam(SupportTicketEmail(firm, TicketSample(firm, account, now), SampleQuestion, 0, opened: true, admin)),
            NotificationKinds.TraderStagePassed => ForTrader(StagePassedSample(firm, Account(2), now)),
            NotificationKinds.TraderPassed => ForTrader(PassedEmail(firm, passed, AccountUrl(firm, passed))),
            NotificationKinds.TraderFunded => ForTrader(FundedEmail(firm, account, accountUrl)),
            NotificationKinds.TraderEnded => ForTrader(FailedEmail(firm, account, FailureSample(account, now), accountUrl)),
            NotificationKinds.TraderReinstated => ForTrader(ReinstatedEmail(firm, account, new StageReinstated(now, 0, "sample-1001-1", account.Definition.InitialBalance - 1_787.50m, true, 1), accountUrl)),
            NotificationKinds.TraderPayouts => ForTrader(PayoutApprovedEmail(firm, account, PayoutSample(account, now), accountUrl)),
            NotificationKinds.TraderInactivity => ForTrader(InactivityReminderEmail(firm, account, DateOnly.FromDateTime(now.UtcDateTime).AddDays(InactivityReminderDays))),
            NotificationKinds.TraderSupportAnswers => ForTrader(SupportToTraderEmail(firm, TicketSample(firm, account, now), SampleAnswer, 0, opened: false, closed: false)),
            NotificationKinds.TraderIdentity => ForTrader(IdentityEmail(firm, SampleTraderEmail, approved: true, reason: null)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "There is no such notification email."),
        };

        NotifiedAccount Account(int phases) => new(SampleAccountId, SampleNumber, SampleTraderEmail, SampleChallenge(firm, challenges, phases));

        NotificationPreview ForTeam(EmailMessage message) => new(kind, EmailAudience.Team, message);

        NotificationPreview ForTrader(EmailMessage message) => new(kind, EmailAudience.Trader, message);
    }

    // The firm's first challenge with at least the evaluation phases, or the two-step template in the firm's currency.
    private static ChallengeDefinition SampleChallenge(Firm firm, IReadOnlyList<ChallengeDefinition> challenges, int phases) =>
        challenges.FirstOrDefault(c => c.Evaluation.Count >= phases)
        ?? ChallengeTemplates.TwoStep("two-step-100k", 100_000m, challenges.Count > 0 ? challenges[0].Currency : firm.Trading?.Currency ?? firm.AccountCurrency);

    private EmailMessage SaleSample(Firm firm, NotifiedAccount account, IReadOnlyList<ChallengePrice> prices, string admin)
    {
        var definition = account.Definition;
        var price = prices.FirstOrDefault(p => p.ChallengeId == definition.Id);
        return SaleEmail(
            firm, Person(SampleTraderName, SampleTraderEmail), definition.Name, SampleNumber, price?.Amount ?? SamplePrice, price?.Currency ?? definition.Currency, account.Id, admin);
    }

    // The first phase passed a little above its profit target.
    private static EmailMessage StagePassedSample(Firm firm, NotifiedAccount account, DateTimeOffset now)
    {
        var definition = account.Definition;
        var target = definition.Evaluation[0].ProfitTargetPercent ?? 0m;
        var balance = definition.InitialBalance + definition.PercentOfInitialBalance(target + 0.5m);
        return StagePassedEmail(firm, account, new StagePassed(now, 0, account.Id.ToString(), balance, TradingDays: 5), AccountUrl(firm, account));
    }

    // The daily loss limit of the first phase broken by a little.
    private static ChallengeFailed FailureSample(NotifiedAccount account, DateTimeOffset now)
    {
        var definition = account.Definition;
        var level = definition.InitialBalance - definition.PercentOfInitialBalance(definition.Stage(0).DailyLoss.Percent);
        return new ChallengeFailed(now, 0, account.Id.ToString(), FailureReason.DailyLoss, "daily", level, level - 37.5m);
    }

    // The trader's share of a profit, at the challenge's profit split, is the sample payout.
    private static Payout PayoutSample(NotifiedAccount account, DateTimeOffset now)
    {
        var split = account.Definition.Funded.ProfitSplitPercent ?? 80m;
        return new Payout("sample", account.Id.ToString(), decimal.Round(SamplePayout * 100m / split, 2), split, SamplePayout, PayoutStatus.Approved, now);
    }

    private static SupportTicket TicketSample(Firm firm, NotifiedAccount account, DateTimeOffset now) =>
        new(
            SampleTicketId,
            firm.Id,
            SampleTicketNumber,
            SampleTraderId,
            SampleTraderEmail,
            SampleTraderName,
            new SupportTicketAccount(account.Id, account.Number, account.Definition.Name, ChallengeStatus.Active, account.Definition.Funded.Name, Funded: true, Paused: false),
            "When can I ask for a payout?",
            SupportTicketStatus.Open,
            CreatedAt: now,
            UpdatedAt: now,
            WaitingSince: now,
            AnsweredAt: null,
            TraderReadAt: null,
            ClosedAt: null,
            ClosedBy: null,
            Messages: 1,
            OpenedBy: SupportAuthor.Trader);
}
