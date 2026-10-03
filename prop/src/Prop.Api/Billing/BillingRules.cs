using System.Globalization;

using Prop.Api.Configuration;

namespace Prop.Api.Billing;

/// <summary>The monthly price of each slot from slot number <paramref name="From"/> on.</summary>
internal sealed record SlotPrice(int From, decimal Price);

/// <summary>One line of a charge: what is paid for, how many, and the amount for all of them.</summary>
public sealed record ChargeLine(string Description, int Quantity, decimal Amount);

/// <summary>What a firm pays us, from the configuration: the currency, the startup fee and the slot prices.</summary>
internal sealed record BillingTerms(string Currency, decimal StartupFee, IReadOnlyList<SlotPrice> SlotPrices, int MinSlots, int MaxSlots, int ChargeDaysBeforeMonth)
{
    public static BillingTerms From(BillingOptions options) =>
        new(
            options.Currency,
            options.StartupFee,
            [.. options.SlotPrices.Select(p => new SlotPrice(p.From, p.Price))],
            options.MinSlots,
            options.MaxSlots,
            options.ChargeDaysBeforeMonth);

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

        if (SlotPrices.Count == 0 || SlotPrices[0].From != 1
            || SlotPrices.Zip(SlotPrices.Skip(1)).Any(p => p.Second.From <= p.First.From)
            || SlotPrices.Any(p => p.Price <= 0 || decimal.Round(p.Price, 2) != p.Price))
        {
            problems.Add("Billing:SlotPrices must start from slot 1, rise from tier to tier, and have prices above 0 in whole cents.");
        }

        if (MinSlots < 1 || MaxSlots < MinSlots)
        {
            problems.Add("Billing:MinSlots must be at least 1, and Billing:MaxSlots at least MinSlots.");
        }

        if (ChargeDaysBeforeMonth is < 0 or > 27)
        {
            problems.Add("Billing:ChargeDaysBeforeMonth must be 0 to 27.");
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

    /// <summary>A month's price of the slots. Each slot costs the price of the tier it falls in.</summary>
    public static decimal MonthlyPrice(int slots, IReadOnlyList<SlotPrice> prices)
    {
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

    /// <summary>
    /// The first payment, which takes the firm live: the startup fee and the slots for the rest of the month, and
    /// for the next month too when it is already due.
    /// </summary>
    public static IReadOnlyList<ChargeLine> Activation(DateTimeOffset now, int slots, BillingTerms terms)
    {
        var month = MonthOf(now);
        var monthly = MonthlyPrice(slots, terms.SlotPrices);
        var (left, inMonth) = DaysLeft(now);
        List<ChargeLine> lines = [];
        if (terms.StartupFee > 0)
        {
            lines.Add(new ChargeLine("Startup fee", 1, terms.StartupFee));
        }

        lines.Add(new ChargeLine(Invariant($"{slots} slots, {NameOf(month)} ({left} of {inMonth} days)"), slots, ForRestOfMonth(monthly, now)));
        if (IsNextMonthDue(now, terms))
        {
            lines.Add(new ChargeLine(Invariant($"{slots} slots, {NameOf(month.AddMonths(1))}"), slots, monthly));
        }

        return lines;
    }

    /// <summary>A month's slots, charged before the month starts.</summary>
    public static IReadOnlyList<ChargeLine> Renewal(DateOnly month, int slots, BillingTerms terms) =>
        [new ChargeLine(Invariant($"{slots} slots, {NameOf(month)}"), slots, MonthlyPrice(slots, terms.SlotPrices))];

    /// <summary>
    /// More slots now: the difference in price for the rest of the month, and for the next month too when it is
    /// already paid with fewer slots.
    /// </summary>
    public static IReadOnlyList<ChargeLine> MoreSlots(DateTimeOffset now, int currentSlots, int newSlots, int? nextMonthPaidSlots, BillingTerms terms)
    {
        var month = MonthOf(now);
        var (left, inMonth) = DaysLeft(now);
        var newPrice = MonthlyPrice(newSlots, terms.SlotPrices);
        List<ChargeLine> lines =
        [
            new(
                Invariant($"{newSlots - currentSlots} more slots, {NameOf(month)} ({left} of {inMonth} days)"),
                newSlots - currentSlots,
                ForRestOfMonth(newPrice - MonthlyPrice(currentSlots, terms.SlotPrices), now)),
        ];
        if (nextMonthPaidSlots is { } next && next < newSlots)
        {
            lines.Add(new ChargeLine(
                Invariant($"{newSlots - next} more slots, {NameOf(month.AddMonths(1))}"),
                newSlots - next,
                newPrice - MonthlyPrice(next, terms.SlotPrices)));
        }

        return lines;
    }

    /// <summary>More slots in a whole month that is already paid with fewer.</summary>
    public static IReadOnlyList<ChargeLine> MoreSlotsInMonth(DateOnly month, int paidSlots, int newSlots, BillingTerms terms) =>
    [
        new(
            Invariant($"{newSlots - paidSlots} more slots, {NameOf(month)}"),
            newSlots - paidSlots,
            MonthlyPrice(newSlots, terms.SlotPrices) - MonthlyPrice(paidSlots, terms.SlotPrices)),
    ];

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
