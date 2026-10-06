using System.Globalization;
using System.Text;

namespace Prop.Api.Billing;

/// <summary>
/// One A4 page of a PDF, drawn in PDF's own operators, and the document around it. It has the standard Helvetica fonts,
/// which every PDF reader has, so no font is embedded. Text is Latin-1, which covers the languages of the EU's west; other
/// letters are shown as ?. Invoices (<see cref="InvoicePdf"/>) and receipts (<see cref="Payments.ReceiptPdf"/>) are drawn on it.
/// </summary>
internal sealed class PdfPage
{
    public const float Width = 595;
    public const float Height = 842;

    private readonly StringBuilder _content = new();

    /// <summary>The text, shortened with "..." when it is wider than <paramref name="maxWidth"/> at the size.</summary>
    public static string Fit(string text, float size, float maxWidth, bool bold = false)
    {
        var latin = ToLatin1(text);
        if (TextWidth(latin, bold) * size / 1000 <= maxWidth)
        {
            return text;
        }

        var end = latin.Length;
        while (end > 0 && TextWidth(string.Concat(latin.AsSpan(0, end).TrimEnd(), "..."), bold) * size / 1000 > maxWidth)
        {
            end--;
        }

        return string.Concat(latin.AsSpan(0, end).TrimEnd(), "...");
    }

    public void Text(float x, float y, string text, float size, bool bold = false, bool muted = false, bool alignRight = false)
    {
        if (text.Length == 0)
        {
            return;
        }

        var latin = ToLatin1(text);
        var left = alignRight ? x - (TextWidth(latin, bold) * size / 1000) : x;
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
            FormattableString.Invariant($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Width} {Height}] /Resources << /Font << /F1 4 0 R /F2 5 0 R >> >> /Contents 6 0 R >>"),
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
    private static float TextWidth(string text, bool bold)
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
