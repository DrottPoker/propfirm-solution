using System.Globalization;

using Prop.Api.Billing;
using Prop.Api.Firms;

namespace Prop.Api.Payments;

/// <summary>
/// A paid order's receipt as a PDF, in the firm's name, with the same lines as the receipt in the buyer's email. Drawn as
/// invoices are (<see cref="InvoicePdf"/>), on one A4 page.
/// </summary>
internal static class ReceiptPdf
{
    private const float Left = 56;
    private const float Right = PdfPage.Width - 56;
    private const float Details = 290;

    /// <summary>The receipt from the firm to the buyer, made by the product <paramref name="producer"/>.</summary>
    public static byte[] Render(OrderReceipt receipt, Firm firm, string producer)
    {
        var page = new PdfPage();
        var order = receipt.Order;
        var y = PdfPage.Height - 72;

        page.Text(Left, y, "Receipt", 22, bold: true);
        page.Text(Right, y + 6, PdfPage.Fit(firm.Name, 11, 260, bold: true), 11, bold: true, alignRight: true);
        y -= 40;

        // Who it is from, and the receipt's details beside it.
        var details = new List<(string Label, string Value)>
        {
            ("Receipt for", receipt.Title),
            ("Date paid", OrderReceipt.Day(receipt.PaidAt)),
            ("Paid with", receipt.PaidWith(firm)),
        };
        if (order.PaymentReference is { } reference)
        {
            details.Add(("Payment reference", reference));
        }

        var top = y;
        foreach (var (line, i) in FirmLines(firm).Select((l, i) => (l, i)))
        {
            page.Text(Left, y, PdfPage.Fit(line, i == 0 ? 10.5f : 9.5f, Details - Left - 16, bold: i == 0), i == 0 ? 10.5f : 9.5f, bold: i == 0);
            y -= 13;
        }

        var detailY = top;
        foreach (var (label, value) in details)
        {
            page.Text(Details, detailY, label, 9.5f, muted: true);
            page.Text(Right, detailY, PdfPage.Fit(value, 9.5f, Right - Details - 90), 9.5f, alignRight: true);
            detailY -= 13;
        }

        y = Math.Min(y, detailY) - 24;
        page.Text(Left, y, "Sold to", 9.5f, muted: true);
        y -= 14;
        foreach (var (line, i) in BuyerLines(order).Select((l, i) => (l, i)))
        {
            page.Text(Left, y, PdfPage.Fit(line, i == 0 ? 10.5f : 9.5f, Right - Left, bold: i == 0), i == 0 ? 10.5f : 9.5f, bold: i == 0);
            y -= 13;
        }

        // The challenge and the discount, and the total paid.
        y -= 24;
        page.Text(Left, y, "Description", 9.5f, muted: true);
        page.Text(Right, y, $"Amount ({order.Currency})", 9.5f, muted: true, alignRight: true);
        y -= 8;
        page.Rule(Left, Right, y);
        foreach (var line in receipt.Lines)
        {
            y -= 16;
            page.Text(Left, y, PdfPage.Fit(line.Description, 10, 380), 10);
            page.Text(Right, y, Amount(line.Amount), 10, alignRight: true);
        }

        y -= 10;
        page.Rule(Left, Right, y);
        y -= 18;
        page.Text(Details, y, "Total paid", 11, bold: true);
        page.Text(Right, y, receipt.Money(order.Amount), 11, bold: true, alignRight: true);

        y -= 36;
        page.Text(
            Left,
            y,
            order.RefundedAt is { } refundedAt
                ? $"The payment was refunded on {OrderReceipt.Day(refundedAt)}."
                : "Thank you. This order is paid, so there is nothing more to pay.",
            9.5f,
            muted: order.RefundedAt is null);

        var footer = string.Join("  |  ", new[] { firm.Name, firm.SupportEmail ?? "", firm.Portal.Url.Host }.Where(part => part.Length > 0));
        page.Text(Left, 48, PdfPage.Fit(footer, 8.5f, Right - Left), 8.5f, muted: true);
        return page.ToPdf($"Receipt for {receipt.Title}", firm.Name, producer);
    }

    private static IEnumerable<string> FirmLines(Firm firm)
    {
        yield return firm.Name;
        if (firm.SupportEmail is { } support)
        {
            yield return support;
        }

        yield return firm.Portal.Url.AbsoluteUri;
    }

    private static IEnumerable<string> BuyerLines(Order order)
    {
        if (order.BuyerName is { } name)
        {
            yield return name;
        }

        yield return order.Email;
        if (order.BuyerCountry is { } country)
        {
            yield return country;
        }
    }

    private static string Amount(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);
}
