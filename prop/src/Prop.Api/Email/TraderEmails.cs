using System.Globalization;

using Prop.Api.Firms;
using Prop.Api.Payments;
using Prop.Rules;

namespace Prop.Api.Email;

/// <summary>
/// The emails to a firm's traders (ADR 0033). They come in the firm's name and look, with its logo and accent color,
/// and replies go to the firm's support address when it has one. The footer has the firm's name, its support address and
/// why the trader gets the email. Each has the same text in plain text too, for mail programs that show no HTML.
/// </summary>
internal static class TraderEmails
{
    private const string DefaultAccent = "#2563eb";

    /// <summary>
    /// An email to a trader: paragraphs of text and, with <paramref name="link"/>, a button to it. <paramref name="note"/>
    /// is a line in small print under the button, for example how long the link works, and <paramref name="more"/> what
    /// comes after it, such as a receipt. Without <paramref name="askForReplies"/> the email does not ask the trader to reply
    /// to it, for example when the answer belongs in the portal, though a reply still goes to the firm's support address.
    /// <paramref name="reason"/> says why the trader gets the email, by default that the trader trades with the firm.
    /// </summary>
    public static EmailMessage Create(
        Firm firm,
        string to,
        string subject,
        IReadOnlyList<string> paragraphs,
        string? button,
        Uri? link,
        string? note = null,
        bool askForReplies = true,
        IReadOnlyList<EmailBlock>? more = null,
        string? reason = null)
    {
        var blocks = new List<EmailBlock>(paragraphs.Select(p => new EmailBlock.Paragraph(p)));
        if (link is not null)
        {
            blocks.Add(new EmailBlock.Button(button ?? "Open", link));
        }

        if (note is not null)
        {
            blocks.Add(new EmailBlock.Note(note));
        }

        blocks.AddRange(more ?? []);
        if (firm.SupportEmail is not null && askForReplies)
        {
            blocks.Add(new EmailBlock.Paragraph("Questions? Reply to this email."));
        }

        reason ??= $"You get this email because you trade with {firm.Name}.";
        string[] signature = firm.SupportEmail is { } support ? [firm.Name, support] : [firm.Name];
        var text = $"{EmailLayout.Greeting}\n\n{EmailLayout.Text(blocks)}\n\n{reason}\n\n{string.Join('\n', signature)}";
        var look = LookOf(firm);
        return new EmailMessage(to, subject, text, firm.Name, EmailLayout.Html(look, Header(firm, look), blocks, [.. signature, reason]), firm.SupportEmail);
    }

    /// <summary>
    /// After paying for a challenge in the portal: a link that confirms the email and lets the buyer choose a password, how to
    /// get started, the rules the account started with and the receipt.
    /// </summary>
    public static EmailMessage InviteBuyer(Firm firm, OrderReceipt receipt, Uri link, TimeSpan lifetime) =>
        Create(
            firm,
            receipt.Order.Email,
            $"Your {receipt.ChallengeName} with {firm.Name} is starting",
            [
                $"Thank you for buying {receipt.ChallengeName} from {firm.Name}. Your challenge is being set up now.",
                $"Open this link to confirm your email and get into {firm.Name}'s portal, where you follow your challenge and open the trading terminal. If you have not chosen a password yet, you choose it there.",
            ],
            "Confirm your email",
            link,
            $"The link works once, within {PlatformEmails.Lifetime(lifetime)}. If you did not buy this, you can ignore this email.",
            more: Purchase(firm, receipt, "Choose a password with the link above, or log in if you have chosen one already."),
            reason: Bought(firm));

    /// <summary>
    /// After paying for a challenge in the portal, to a buyer who has a password for it already: a link to the new account,
    /// how to get started, the rules the account started with and the receipt.
    /// </summary>
    public static EmailMessage ChallengeBought(Firm firm, OrderReceipt receipt, Uri accountUrl) =>
        Create(
            firm,
            receipt.Order.Email,
            $"Your {receipt.ChallengeName} with {firm.Name} has started",
            [$"Thank you for buying {receipt.ChallengeName} from {firm.Name}. Your challenge is being set up now. Follow it in {firm.Name}'s portal, where you also open the trading terminal."],
            "Open your account",
            accountUrl,
            more: Purchase(firm, receipt, $"Log in to {firm.Name}'s portal with your password."),
            reason: Bought(firm));

    /// <summary>The firm started a challenge for a trader who has no password for its portal yet, or has not confirmed the email.</summary>
    public static EmailMessage InviteTrader(Firm firm, string challengeName, string to, Uri link, TimeSpan lifetime) =>
        Create(
            firm,
            to,
            $"Your {challengeName} with {firm.Name} has started",
            [$"{firm.Name} has started {challengeName} for you. Open this link to get into {firm.Name}'s portal, where you follow your challenge and open the trading terminal. If you have not chosen a password yet, you choose it there."],
            "Open the portal",
            link,
            $"The link works once, within {PlatformEmails.Lifetime(lifetime)}.",
            reason: StartedByFirm(firm));

    /// <summary>The firm started another challenge for a trader who already has a password for its portal.</summary>
    public static EmailMessage ChallengeStarted(Firm firm, string challengeName, string to, Uri accountUrl) =>
        Create(
            firm,
            to,
            $"Your {challengeName} with {firm.Name} has started",
            [$"{firm.Name} has started {challengeName} for you. Log in to {firm.Name}'s portal to follow it and open the trading terminal."],
            "Open your account",
            accountUrl,
            reason: StartedByFirm(firm));

    /// <summary>A trader forgot the password for the firm's portal.</summary>
    public static EmailMessage ResetPassword(Firm firm, string to, Uri link, TimeSpan lifetime) =>
        Create(
            firm,
            to,
            $"Choose a new password for {firm.Name}",
            [$"Open this link to choose a new password for {firm.Name}'s portal."],
            "Choose a new password",
            link,
            $"The link works once, within {PlatformEmails.Lifetime(lifetime)}. If you did not ask for a new password, you can ignore this email, and your password stays as it is.");

    /// <summary>A trader who chose a password on the order's page asks for the link that confirms the email again.</summary>
    public static EmailMessage ConfirmEmail(Firm firm, string to, Uri link, TimeSpan lifetime) =>
        Create(
            firm,
            to,
            $"Confirm your email for {firm.Name}",
            [$"Open this link to confirm that this is your email for {firm.Name}'s portal. Payouts are paid only to traders who have."],
            "Confirm your email",
            link,
            $"The link works once, within {PlatformEmails.Lifetime(lifetime)}. If you did not ask for this, you can ignore this email.");

    /// <summary>The firm went live, so a trader's account from its sandbox ended. A firm that sells in its portal points to its shop.</summary>
    public static EmailMessage SandboxAccountEnded(Firm firm, string to, string challengeName, long accountNumber) =>
        Create(
            firm,
            to,
            $"Your test account with {firm.Name} has ended",
            [
                $"{firm.Name} has finished testing and is now open for real. Your {challengeName} (account #{accountNumber.ToString(CultureInfo.InvariantCulture)}) was a test account, so it has ended, with any trades on it closed.",
                firm.Payments.Provider is null ? $"Ask {firm.Name} if you want a new challenge." : $"New challenges are bought in {firm.Name}'s shop.",
            ],
            firm.Payments.Provider is null ? "Go to the portal" : "Go to the shop",
            new Uri(firm.Portal.Url, firm.Payments.Provider is null ? "" : "buy"));

    /// <summary>
    /// The firm answered the trader's support ticket (ADR 0041), with the answer. The email asks the trader to answer in the
    /// portal, so the conversation stays in one place.
    /// </summary>
    public static EmailMessage SupportAnswer(Firm firm, string to, long ticketNumber, string subject, string answer, int files, bool closed, Uri ticketUrl) =>
        Create(
            firm,
            to,
            $"{firm.Name} answered your ticket #{ticketNumber.ToString(CultureInfo.InvariantCulture)}",
            [
                $"{firm.Name} answered your support ticket \"{subject}\":",
                answer,
                .. files > 0 ? [FilesNote(files)] : Array.Empty<string>(),
                closed
                    ? $"{firm.Name} has closed the ticket. If you need more help, write in it again in the portal."
                    : "Answer in the portal, so the whole conversation stays in one place.",
            ],
            "Open the ticket",
            ticketUrl,
            askForReplies: false,
            reason: Ticket(firm));

    /// <summary>The firm wrote to the trader first, in a new support ticket (ADR 0041), with its message.</summary>
    public static EmailMessage SupportOpened(Firm firm, string to, long ticketNumber, string subject, string text, int files, Uri ticketUrl) =>
        Create(
            firm,
            to,
            $"Message from {firm.Name}: {subject}",
            [
                $"{firm.Name} has written to you in support ticket #{ticketNumber.ToString(CultureInfo.InvariantCulture)}, \"{subject}\":",
                text,
                .. files > 0 ? [FilesNote(files)] : Array.Empty<string>(),
                "Answer in the portal, so the whole conversation stays in one place.",
            ],
            "Open the ticket",
            ticketUrl,
            askForReplies: false,
            reason: Ticket(firm));

    /// <summary>Our built-in ID check approved the trader (ADR 0042).</summary>
    public static EmailMessage IdentityVerified(Firm firm, string to, Uri page) =>
        Create(
            firm,
            to,
            $"Your identity is verified with {firm.Name}",
            [$"Thank you. Your identity is verified, so nothing about it stands in the way of your funded account or your payouts with {firm.Name}."],
            "Go to the portal",
            page);

    /// <summary>Our built-in ID check declined the trader, with why. The trader can try again.</summary>
    public static EmailMessage IdentityDeclined(Firm firm, string to, string reason, Uri page) =>
        Create(
            firm,
            to,
            $"Your ID check with {firm.Name} did not pass",
            [
                "Your ID check did not pass:",
                reason,
                "You can try again in the portal. Use a valid ID document, in good light, and make sure every corner of it is in the picture.",
            ],
            "Try again",
            page);

    /// <summary>That a message has files, which are only in the portal, for example "1 file is attached in the ticket."</summary>
    public static string FilesNote(int files) => files == 1 ? "1 file is attached in the ticket." : $"{files.ToString(CultureInfo.InvariantCulture)} files are attached in the ticket.";

    private static string Bought(Firm firm) => $"You get this email because you bought a challenge from {firm.Name}.";

    private static string StartedByFirm(Firm firm) => $"You get this email because {firm.Name} started a challenge for you.";

    private static string Ticket(Firm firm) => $"You get this email about your support ticket with {firm.Name}.";

    // How to get started, the rules the account started with and the receipt, after the purchase email's link.
    private static List<EmailBlock> Purchase(Firm firm, OrderReceipt receipt, string firstStep)
    {
        List<EmailBlock> blocks =
        [
            new EmailBlock.Heading("How to get started"),
            new EmailBlock.Steps([firstStep, "Open the trading terminal from your account in the portal.", "Place your first trade."]),
        ];
        if (receipt.Challenge is { } challenge)
        {
            blocks.Add(new EmailBlock.Heading("Your rules"));
            blocks.Add(new EmailBlock.Table(RulesOf(challenge)));
            blocks.Add(new EmailBlock.Note("Every rule, such as the minimum trading days, is on your account's page in the portal."));
        }

        var order = receipt.Order;
        List<EmailRow> lines =
        [
            .. receipt.Lines.Select(l => new EmailRow(l.Description, receipt.Money(l.Amount))),
            new("Total paid", receipt.Money(order.Amount), Strong: true),
            new("Paid with", receipt.PaidWith(firm)),
        ];
        if (order.PaymentReference is { } reference)
        {
            lines.Add(new EmailRow("Payment reference", reference));
        }

        blocks.Add(new EmailBlock.Heading("Receipt"));
        blocks.Add(new EmailBlock.Paragraph($"{receipt.Title}, paid {OrderReceipt.Day(receipt.PaidAt)}."));
        blocks.Add(new EmailBlock.Table(lines));
        return blocks;
    }

    // What a trader most needs to know of the challenge: its size, the profit targets, the loss limits and the profit split.
    private static List<EmailRow> RulesOf(ChallengeDefinition challenge)
    {
        var rows = new List<EmailRow> { new("Account size", Money(challenge.InitialBalance, challenge.Currency)) };
        rows.AddRange(challenge.Evaluation.Select(stage => new EmailRow($"{stage.Name} profit target", Share(challenge, stage.ProfitTargetPercent ?? 0))));
        rows.Add(new EmailRow("Daily loss limit", PerStage(challenge, stage => Share(challenge, stage.DailyLoss.Percent))));
        rows.Add(new EmailRow(
            "Max loss limit", PerStage(challenge, stage => Share(challenge, stage.MaxLoss.Percent) + (stage.MaxLoss.Kind == MaxLossKind.Trailing ? ", trailing" : ""))));
        if (challenge.Funded.ProfitSplitPercent is { } split)
        {
            rows.Add(new EmailRow("Profit split", $"{Percent(split)} to you"));
        }

        return rows;
    }

    // One value when every stage has the same, or each stage's.
    private static string PerStage(ChallengeDefinition challenge, Func<StageRules, string> value)
    {
        var stages = challenge.Evaluation.Append(challenge.Funded).Select(stage => (stage.Name, Value: value(stage))).ToList();
        return stages.Select(s => s.Value).Distinct().Count() == 1 ? stages[0].Value : string.Join(", ", stages.Select(s => $"{s.Name} {s.Value}"));
    }

    // A percent of the account size, with the amount, for example "10% (10,000.00 USD)".
    private static string Share(ChallengeDefinition challenge, decimal percent) =>
        $"{Percent(percent)} ({Money(challenge.PercentOfInitialBalance(percent), challenge.Currency)})";

    private static string Percent(decimal percent) => $"{percent.ToString("0.##", CultureInfo.InvariantCulture)}%";

    private static string Money(decimal amount, string currency) => $"{amount.ToString("N2", CultureInfo.InvariantCulture)} {currency}";

    private static EmailLook LookOf(Firm firm)
    {
        var colors = firm.Portal.Branding.Colors;
        var accent = colors.TryGetValue("accent", out var a) ? a : DefaultAccent;
        var onAccent = colors.TryGetValue("accent-foreground", out var f) ? f : ForegroundOn(accent);
        return new EmailLook(
            Page: "#f4f4f5",
            Card: "#ffffff",
            Text: "#18181b",
            Muted: "#71717a",
            Border: "#e4e4e7",
            Accent: accent,
            OnAccent: onAccent,
            Font: EmailLayout.SansSerif,
            HeadingStyle: "font-size:16px;font-weight:600",
            Radius: 8);
    }

    // The firm's logo, or its name in its accent color.
    private static string Header(Firm firm, EmailLook look)
    {
        var name = EmailLayout.Encode(firm.Name);
        return firm.Portal.Branding.LogoUrl is { } logoUrl && Uri.TryCreate(firm.Portal.Url, logoUrl, out var logoUri)
            ? $"""<img src="{EmailLayout.Encode(logoUri.AbsoluteUri)}" alt="{name}" height="40" style="display:block;height:40px;max-width:240px;border:0">"""
            : $"""<span style="font-size:20px;font-weight:700;color:{look.Accent}">{name}</span>""";
    }

    // Black or white, whichever reads better on the color, as the portal does for a color the firm did not pair.
    private static string ForegroundOn(string hex)
    {
        if (hex.Length != 7 || !int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return "#ffffff";
        }

        static double Channel(int value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        var luminance = (0.2126 * Channel((rgb >> 16) & 0xff)) + (0.7152 * Channel((rgb >> 8) & 0xff)) + (0.0722 * Channel(rgb & 0xff));
        return luminance > 0.179 ? "#000000" : "#ffffff";
    }
}
