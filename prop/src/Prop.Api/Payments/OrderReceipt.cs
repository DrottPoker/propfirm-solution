using System.Globalization;

using Prop.Api.Firms;
using Prop.Rules;

namespace Prop.Api.Payments;

/// <summary>A line of a receipt: what was bought or taken off, and the amount in the order's currency.</summary>
internal sealed record ReceiptLine(string Description, decimal Amount);

/// <summary>
/// The receipt of a paid order, the same in the buyer's email and its PDF (<see cref="ReceiptPdf"/>): the challenge at its
/// price, the discount when a code was used, the total paid, and when and how it was paid. <paramref name="Challenge"/> is
/// the challenge as the order's account started with it. Orders carry no VAT, so a receipt has no VAT line.
/// </summary>
internal sealed record OrderReceipt(Order Order, ChallengeDefinition? Challenge)
{
    /// <summary>The order as the app names it, for example "Order 1002".</summary>
    public string Title => string.Create(CultureInfo.InvariantCulture, $"Order {Order.Number}");

    public string ChallengeName => Challenge?.Name ?? Order.ChallengeId;

    /// <summary>When the order was paid. Only paid orders have receipts.</summary>
    public DateTimeOffset PaidAt => Order.PaidAt ?? Order.CreatedAt;

    /// <summary>The challenge at its price, and the discount, which add up to the total paid.</summary>
    public IReadOnlyList<ReceiptLine> Lines =>
        Order is { DiscountCode: { } code, ListAmount: { } listAmount }
            ? [new(ChallengeName, listAmount), new($"Discount, code {code}", Order.Amount - listAmount)]
            : [new(ChallengeName, Order.Amount)];

    /// <summary>How the buyer paid, in words.</summary>
    public string PaidWith(Firm firm) => Order.Provider switch
    {
        PaymentProvider.Test => "Test payment, no money was taken",
        PaymentProvider.Stripe => "Stripe",
        _ => $"{firm.Name}'s checkout",
    };

    /// <summary>An amount in the order's currency, for example "99.00 USD".</summary>
    public string Money(decimal amount) => $"{amount.ToString("N2", CultureInfo.InvariantCulture)} {Order.Currency}";

    /// <summary>A day as receipts and invoices write it, in UTC, for example "5 Oct 2026".</summary>
    public static string Day(DateTimeOffset time) => time.UtcDateTime.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}
