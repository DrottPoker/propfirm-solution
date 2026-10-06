using System.Globalization;

using Prop.Api.Configuration;

namespace Prop.Api.Billing;

/// <summary>
/// A paid charge's invoice as a PDF (ADR 0032): one A4 page with the standard Helvetica fonts, which every PDF reader
/// has, so no font is embedded. Text is Latin-1, which covers the languages of the EU's west; other letters are shown as ?.
/// </summary>
internal static class InvoicePdf
{
    private const float Left = 56;
    private const float Right = PdfPage.Width - 56;

    /// <summary>The invoice from <paramref name="seller"/> to the firm, made by the product <paramref name="producer"/>.</summary>
    public static byte[] Render(Charge charge, string firmName, SellerOptions seller, string producer)
    {
        var page = new PdfPage();
        var paidAt = charge.PaidAt ?? charge.CreatedAt;
        var y = PdfPage.Height - 72;

        page.Text(Left, y, "Invoice", 22, bold: true);
        page.Text(Right, y + 6, seller.Name, 11, bold: true, alignRight: true);
        y -= 40;

        // Who it is from, and the invoice's details beside it.
        var details = new (string Label, string Value)[]
        {
            ("Invoice number", charge.Invoice ?? "-"),
            ("Invoice date", Day(paidAt)),
            ("Charge", $"#{charge.Number.ToString(CultureInfo.InvariantCulture)}"),
            ("Status", $"Paid by card on {Day(paidAt)}"),
        };
        var top = y;
        foreach (var line in SellerLines(seller))
        {
            page.Text(Left, y, line, 9.5f);
            y -= 13;
        }

        var detailY = top;
        foreach (var (label, value) in details)
        {
            page.Text(330, detailY, label, 9.5f, muted: true);
            page.Text(Right, detailY, value, 9.5f, alignRight: true);
            detailY -= 13;
        }

        y = Math.Min(y, detailY) - 24;
        page.Text(Left, y, "Billed to", 9.5f, muted: true);
        y -= 14;
        foreach (var (line, i) in CustomerLines(charge.Customer, firmName, charge.FirmId).Select((l, i) => (l, i)))
        {
            page.Text(Left, y, line, i == 0 ? 10.5f : 9.5f, bold: i == 0);
            y -= 13;
        }

        // The lines, without VAT, and the totals.
        y -= 24;
        page.Text(Left, y, "Description", 9.5f, muted: true);
        page.Text(430, y, "Quantity", 9.5f, muted: true, alignRight: true);
        page.Text(Right, y, $"Amount ({charge.Currency})", 9.5f, muted: true, alignRight: true);
        y -= 8;
        page.Rule(Left, Right, y);
        foreach (var line in charge.Lines)
        {
            y -= 16;
            page.Text(Left, y, line.Description, 10);
            page.Text(430, y, line.Quantity.ToString(CultureInfo.InvariantCulture), 10, alignRight: true);
            page.Text(Right, y, Money(line.Amount), 10, alignRight: true);
        }

        y -= 10;
        page.Rule(Left, Right, y);
        y -= 18;
        page.Text(330, y, "Total without VAT", 10);
        page.Text(Right, y, Money(charge.NetAmount), 10, alignRight: true);
        y -= 16;
        page.Text(330, y, charge.Vat.Treatment == VatTreatment.NotRecorded ? "VAT (not recorded)" : Charge.VatLine(charge.Vat.Percent), 10);
        page.Text(Right, y, Money(charge.Vat.Amount), 10, alignRight: true);
        y -= 8;
        page.Rule(330, Right, y);
        y -= 18;
        page.Text(330, y, "Total paid", 11, bold: true);
        page.Text(Right, y, $"{Money(charge.Amount)} {charge.Currency}", 11, bold: true, alignRight: true);

        y -= 36;
        if (VatRules.NoteOf(charge.Vat.Treatment) is { } note)
        {
            page.Text(Left, y, note, 9.5f);
            y -= 14;
        }

        page.Text(Left, y, "Thank you. This invoice is paid, so there is nothing more to pay.", 9.5f, muted: true);

        var footer = string.Join("  |  ", new[] { seller.Name, Labelled("Org. no.", seller.OrganizationNumber), Labelled("VAT no.", seller.VatNumber), seller.Email }
            .Where(part => part.Length > 0));
        page.Text(Left, 48, footer, 8.5f, muted: true);
        return page.ToPdf($"Invoice {charge.Invoice}", seller.Name, producer);
    }

    private static IEnumerable<string> SellerLines(SellerOptions seller)
    {
        foreach (var line in Lines(seller.Address))
        {
            yield return line;
        }

        if (seller.OrganizationNumber.Length > 0)
        {
            yield return Labelled("Org. no.", seller.OrganizationNumber);
        }

        if (seller.VatNumber.Length > 0)
        {
            yield return Labelled("VAT no.", seller.VatNumber);
        }

        if (seller.Email.Length > 0)
        {
            yield return seller.Email;
        }
    }

    private static IEnumerable<string> CustomerLines(ChargeCustomer? customer, string firmName, string firmId)
    {
        yield return customer?.CompanyName ?? firmName;
        foreach (var line in Lines(customer?.Address))
        {
            yield return line;
        }

        if (customer?.Country is { } country)
        {
            yield return country;
        }

        if (customer?.RegistrationNumber is { } registration)
        {
            yield return Labelled("Reg. no.", registration);
        }

        if (customer?.VatNumber is { } vat)
        {
            yield return Labelled("VAT no.", vat);
        }

        yield return $"{firmName} ({firmId})";
    }

    private static string[] Lines(string? text) =>
        (text ?? "").Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Labelled(string label, string value) => value.Length > 0 ? $"{label} {value}" : "";

    private static string Day(DateTimeOffset time) => time.UtcDateTime.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    private static string Money(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);
}
