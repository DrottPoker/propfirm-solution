using System.Globalization;

using Prop.Api.Configuration;

namespace Prop.Api.Billing;

/// <summary>The monthly price of each slot from slot number <paramref name="From"/> on.</summary>
internal sealed record SlotPrice(int From, decimal Price);

/// <summary>One line of a charge: what is paid for, how many, and the amount for all of them.</summary>
public sealed record ChargeLine(string Description, int Quantity, decimal Amount);

/// <summary>
/// What a firm pays us, from the configuration: the currency, the startup fee, the monthly package and the slots
/// it includes, the prices of slots beyond it, and the deposit paid for our review, which is taken off the startup
/// fee. The prices are without VAT, which is <paramref name="VatPercent"/> for firms that pay it, by
/// <paramref name="SellerCountry"/>, where we are.
/// </summary>
internal sealed record BillingTerms(
    string Currency,
    decimal StartupFee,
    decimal PackagePrice,
    int PackageSlots,
    IReadOnlyList<SlotPrice> SlotPrices,
    int MaxSlots,
    int ChargeDaysBeforeMonth,
    decimal ReviewDeposit,
    decimal VatPercent = 25,
    string SellerCountry = "SE")
{
    public static BillingTerms From(BillingOptions options) =>
        new(
            options.Currency,
            options.StartupFee,
            options.PackagePrice,
            options.PackageSlots,
            [.. options.SlotPrices.Select(p => new SlotPrice(p.From, p.Price))],
            options.MaxSlots,
            options.ChargeDaysBeforeMonth,
            options.ReviewDeposit,
            options.VatPercent,
            options.Seller.Country);

    /// <summary>
    /// The slots a month is charged for: the firm's choice, but never fewer than the package includes or than its
    /// open challenges and orders take.
    /// </summary>
    public int SlotsToCharge(int? chosen, int taken) => Math.Max(Math.Max(chosen ?? PackageSlots, PackageSlots), taken);

    /// <summary>What is wrong with the terms. Empty when they work.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (Currency.Length != 3 || !Currency.All(char.IsAsciiLetterUpper))
        {
            problems.Add("Billing:Currency must be three capital letters, for example USD.");
        }

        if (StartupFee < 0 || decimal.Round(StartupFee, 2) != StartupFee)
        {
            problems.Add("Billing:StartupFee must be 0 or more, in whole cents.");
        }

        if (ReviewDeposit < 0 || ReviewDeposit > StartupFee || decimal.Round(ReviewDeposit, 2) != ReviewDeposit)
        {
            problems.Add("Billing:ReviewDeposit must be 0 to Billing:StartupFee, in whole cents.");
        }

        if (PackagePrice <= 0 || decimal.Round(PackagePrice, 2) != PackagePrice)
        {
            problems.Add("Billing:PackagePrice must be above 0, in whole cents.");
        }

        if (PackageSlots < 1 || MaxSlots < PackageSlots)
        {
            problems.Add("Billing:PackageSlots must be at least 1, and Billing:MaxSlots at least PackageSlots.");
        }

        if (SlotPrices.Count == 0 || SlotPrices[0].From != PackageSlots + 1
            || SlotPrices.Zip(SlotPrices.Skip(1)).Any(p => p.Second.From <= p.First.From)
            || SlotPrices.Any(p => p.Price <= 0 || decimal.Round(p.Price, 2) != p.Price))
        {
            problems.Add("Billing:SlotPrices must start from the slot after the package's, rise from tier to tier, and have prices above 0 in whole cents.");
        }

        if (ChargeDaysBeforeMonth is < 0 or > 27)
        {
            problems.Add("Billing:ChargeDaysBeforeMonth must be 0 to 27.");
        }

        if (VatPercent is < 0 or >= 100 || decimal.Round(VatPercent, 2) != VatPercent)
        {
            problems.Add("Billing:VatPercent must be 0 to under 100, with at most two decimals.");
        }

        if (SellerCountry.Length != 2 || !SellerCountry.All(char.IsAsciiLetterUpper))
        {
            problems.Add("Billing:Seller:Country must be a two-letter country code, for example SE.");
        }

        return problems;
    }
}

/// <summary>
/// The arithmetic of billing, the same for every firm: months, prices and the lines of each charge. Months are
/// calendar months in UTC. A part of a month is paid for the days left of it, counting the day of the payment.
/// </summary>
internal static class BillingRules
{
    /// <summary>The first day of the month the time is in, in UTC.</summary>
    public static DateOnly MonthOf(DateTimeOffset time)
    {
        var utc = time.UtcDateTime;
        return new DateOnly(utc.Year, utc.Month, 1);
    }

    public static DateTimeOffset StartOf(DateOnly month) => new(month.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    /// <summary>When the month is charged, some days before it starts.</summary>
    public static DateTimeOffset ChargeTimeOf(DateOnly month, int daysBefore) => StartOf(month).AddDays(-daysBefore);

    public static string NameOf(DateOnly month) => month.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>A paid charge's invoice number, in the firm's own series, for example ACME-0003.</summary>
    public static string InvoiceOf(string firmId, long number) => string.Create(CultureInfo.InvariantCulture, $"{firmId.ToUpperInvariant()}-{number:D4}");

    /// <summary>
    /// What one more round of automatic expansion would cost: the monthly price of <paramref name="step"/> more slots, at
    /// most up to the most a firm can have, and the part of it for the rest of the month when it happens at the time.
    /// </summary>
    public static (int Added, decimal Monthly, decimal RestOfMonth) Expansion(DateTimeOffset time, int slots, int step, BillingTerms terms)
    {
        var added = Math.Max(0, Math.Min(slots + step, terms.MaxSlots) - slots);
        var monthly = MonthlyPrice(slots + added, terms) - MonthlyPrice(slots, terms);
        return (added, monthly, ForRestOfMonth(monthly, time));
    }

    /// <summary>A month's price of the slots: the package, and each slot beyond it at the price of the tier it falls in.</summary>
    public static decimal MonthlyPrice(int slots, BillingTerms terms) => terms.PackagePrice + ExtraSlotsPrice(slots, terms);

    /// <summary>A month's price of the slots beyond the package. Each slot costs the price of the tier it falls in.</summary>
    private static decimal ExtraSlotsPrice(int slots, BillingTerms terms)
    {
        var prices = terms.SlotPrices;
        var total = 0m;
        for (var i = 0; i < prices.Count && slots >= prices[i].From; i++)
        {
            var last = i + 1 < prices.Count ? prices[i + 1].From - 1 : int.MaxValue;
            total += (Math.Min(slots, last) - prices[i].From + 1) * prices[i].Price;
        }

        return total;
    }

    /// <summary>The days of the month left from the time, counting its own day, and the days in the month.</summary>
    public static (int Left, int InMonth) DaysLeft(DateTimeOffset time)
    {
        var utc = time.UtcDateTime;
        var inMonth = DateTime.DaysInMonth(utc.Year, utc.Month);
        return (inMonth - utc.Day + 1, inMonth);
    }

    /// <summary>The part of a month's price for the days left of it, rounded to whole cents.</summary>
    public static decimal ForRestOfMonth(decimal monthly, DateTimeOffset time)
    {
        var (left, inMonth) = DaysLeft(time);
        return decimal.Round(monthly * left / inMonth, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Whether the next month is already due to be charged at the time.</summary>
    public static bool IsNextMonthDue(DateTimeOffset time, BillingTerms terms) =>
        time >= ChargeTimeOf(MonthOf(time).AddMonths(1), terms.ChargeDaysBeforeMonth);

    /// <summary>The deposit for our review, paid when the firm sends its application.</summary>
    public static IReadOnlyList<ChargeLine> Deposit(BillingTerms terms) =>
        [new ChargeLine("Review deposit, taken off the startup fee", 1, terms.ReviewDeposit)];

    /// <summary>
    /// The first payment, which takes the firm live: the startup fee less the deposit the firm paid for our review,
    /// and the package and the slots beyond it for the rest of the month, and for the next month too when it is
    /// already due.
    /// </summary>
    public static IReadOnlyList<ChargeLine> Activation(DateTimeOffset now, int slots, BillingTerms terms, decimal depositPaid = 0)
    {
        var month = MonthOf(now);
        var (left, inMonth) = DaysLeft(now);
        var deposit = Math.Min(depositPaid, terms.StartupFee);
        List<ChargeLine> lines = [];
        if (terms.StartupFee - deposit > 0)
        {
            lines.Add(deposit > 0
                ? new ChargeLine(Invariant($"Startup fee, less the deposit of {deposit:N2} {terms.Currency}"), 1, terms.StartupFee - deposit)
                : new ChargeLine("Startup fee", 1, terms.StartupFee));
        }

        lines.AddRange(MonthLines(slots, terms, Invariant($"{NameOf(month)} ({left} of {inMonth} days)"), price => ForRestOfMonth(price, now)));
        if (IsNextMonthDue(now, terms))
        {
            lines.AddRange(MonthLines(slots, terms, NameOf(month.AddMonths(1)), price => price));
        }

        return lines;
    }

    /// <summary>A month's package and slots beyond it, charged before the month starts.</summary>
    public static IReadOnlyList<ChargeLine> Renewal(DateOnly month, int slots, BillingTerms terms) =>
        [.. MonthLines(slots, terms, NameOf(month), price => price)];

    /// <summary>
    /// More slots now: the difference in price for the rest of the month, and for the next month too when it is
    /// already paid with fewer slots.
    /// </summary>
    public static IReadOnlyList<ChargeLine> MoreSlots(DateTimeOffset now, int currentSlots, int newSlots, int? nextMonthPaidSlots, BillingTerms terms)
    {
        var month = MonthOf(now);
        var (left, inMonth) = DaysLeft(now);
        var newPrice = MonthlyPrice(newSlots, terms);
        List<ChargeLine> lines =
        [
            new(
                Invariant($"{newSlots - currentSlots} more {Slot(newSlots - currentSlots)}, {NameOf(month)} ({left} of {inMonth} days)"),
                newSlots - currentSlots,
                ForRestOfMonth(newPrice - MonthlyPrice(currentSlots, terms), now)),
        ];
        if (nextMonthPaidSlots is { } next && next < newSlots)
        {
            lines.Add(new ChargeLine(
                Invariant($"{newSlots - next} more {Slot(newSlots - next)}, {NameOf(month.AddMonths(1))}"),
                newSlots - next,
                newPrice - MonthlyPrice(next, terms)));
        }

        return lines;
    }

    /// <summary>More slots in a whole month that is already paid with fewer.</summary>
    public static IReadOnlyList<ChargeLine> MoreSlotsInMonth(DateOnly month, int paidSlots, int newSlots, BillingTerms terms) =>
    [
        new(
            Invariant($"{newSlots - paidSlots} more {Slot(newSlots - paidSlots)}, {NameOf(month)}"),
            newSlots - paidSlots,
            MonthlyPrice(newSlots, terms) - MonthlyPrice(paidSlots, terms)),
    ];

    /// <summary>
    /// A month's lines, or part of a month's at <paramref name="part"/> of the price: the package, and the slots
    /// beyond it when there are any.
    /// </summary>
    private static IEnumerable<ChargeLine> MonthLines(int slots, BillingTerms terms, string month, Func<decimal, decimal> part)
    {
        yield return new ChargeLine(Invariant($"Package with {terms.PackageSlots} {Slot(terms.PackageSlots)}, {month}"), terms.PackageSlots, part(terms.PackagePrice));
        if (slots > terms.PackageSlots)
        {
            yield return new ChargeLine(
                Invariant($"{slots - terms.PackageSlots} extra {Slot(slots - terms.PackageSlots)}, {month}"),
                slots - terms.PackageSlots,
                part(ExtraSlotsPrice(slots, terms)));
        }
    }

    private static string Slot(int count) => count == 1 ? "slot" : "slots";

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
