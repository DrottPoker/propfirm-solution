using System.Globalization;
using System.Text;

using Prop.Api.Configuration;

namespace Prop.Api.Billing;

/// <summary>
/// A paid charge's invoice as a PDF (ADR 0032): one A4 page with the standard Helvetica fonts, which every PDF reader
/// has, so no font is embedded. Text is Latin-1, which covers the languages of the EU's west; other letters are shown as ?.
/// </summary>
internal static class InvoicePdf
{
    private const float PageWidth = 595;
    private const float PageHeight = 842;
    private const float Left = 56;
    private const float Right = PageWidth - 56;

    /// <summary>The invoice from <paramref name="seller"/> to the firm, made by the product <paramref name="producer"/>.</summary>
    public static byte[] Render(Charge charge, string firmName, SellerOptions seller, string producer)
    {
        var page = new Page();
        var paidAt = charge.PaidAt ?? charge.CreatedAt;
        var y = PageHeight - 72;

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

    /// <summary>The drawing of one page, in PDF's own operators, and the document around it.</summary>
    private sealed class Page
    {
        private readonly StringBuilder _content = new();

        public void Text(float x, float y, string text, float size, bool bold = false, bool muted = false, bool alignRight = false)
        {
            if (text.Length == 0)
            {
                return;
            }

            var latin = ToLatin1(text);
            var left = alignRight ? x - (Width(latin, bold) * size / 1000) : x;
            _content.Append(muted ? "0.42 g\n" : "0 g\n");
            _content.Append(CultureInfo.InvariantCulture, $"BT /{(bold ? "F2" : "F1")} {size:0.##} Tf {left:0.##} {y:0.##} Td (");
            foreach (var c in latin)
            {
                _content.Append(c is '(' or ')' or '\\' ? $"\\{c}" : c.ToString());
            }

            _content.Append(") Tj ET\n");
        }

        public void Rule(float from, float to, float y) =>
            _content.Append(CultureInfo.InvariantCulture, $"0.8 G 0.6 w {from:0.##} {y:0.##} m {to:0.##} {y:0.##} l S\n");

        public byte[] ToPdf(string title, string author, string producer)
        {
            var stream = Encoding.Latin1.GetBytes(_content.ToString());
            string[] objects =
            [
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                FormattableString.Invariant($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] /Resources << /Font << /F1 4 0 R /F2 5 0 R >> >> /Contents 6 0 R >>"),
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
                "",
                $"<< /Title ({Escape(ToLatin1(title))}) /Author ({Escape(ToLatin1(author))}) /Producer ({Escape(ToLatin1(producer))}) >>",
            ];

            using var output = new MemoryStream();
            var offsets = new List<long>();
            Write(output, "%PDF-1.4\n%\u00e2\u00e3\u00cf\u00d3\n");
            for (var i = 0; i < objects.Length; i++)
            {
                offsets.Add(output.Position);
                if (i == 5)
                {
                    Write(output, FormattableString.Invariant($"6 0 obj\n<< /Length {stream.Length} >>\nstream\n"));
                    output.Write(stream);
                    Write(output, "\nendstream\nendobj\n");
                }
                else
                {
                    Write(output, FormattableString.Invariant($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"));
                }
            }

            var xref = output.Position;
            var table = new StringBuilder(FormattableString.Invariant($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n"));
            foreach (var offset in offsets)
            {
                table.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
            }

            table.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R /Info 7 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            Write(output, table.ToString());
            return output.ToArray();
        }

        private static void Write(Stream output, string text) => output.Write(Encoding.Latin1.GetBytes(text));

        private static string Escape(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal);

        // Letters outside Latin-1 have no glyph in the standard fonts' encoding.
        private static string ToLatin1(string text)
        {
            var builder = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                builder.Append(c switch
                {
                    '\u2013' or '\u2014' or '\u2212' => '-',
                    '\u2018' or '\u2019' => '\'',
                    '\u201c' or '\u201d' => '"',
                    _ when c is >= ' ' and <= '~' or >= '\u00a0' and <= '\u00ff' => c,
                    _ => '?',
                });
            }

            return builder.ToString();
        }

        // In thousandths of the font size, from the fonts' metrics. Letters without a width here are about as wide as n.
        private static float Width(string text, bool bold)
        {
            var widths = bold ? BoldWidths : RegularWidths;
            return text.Sum(c => c is >= ' ' and <= '~' ? widths[c - ' '] : 556);
        }

        private static readonly short[] RegularWidths =
        [
            278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
            1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
            333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
            556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
        ];

        private static readonly short[] BoldWidths =
        [
            278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
            975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
            333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
            611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
        ];
    }
}
