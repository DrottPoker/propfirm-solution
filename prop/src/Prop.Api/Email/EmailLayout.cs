using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Prop.Api.Email;

/// <summary>A part of an email, written the same in its plain text and its HTML (<see cref="EmailLayout"/>).</summary>
internal abstract record EmailBlock
{
    /// <summary>A paragraph. Lines of its own are kept, as in a message someone wrote.</summary>
    public sealed record Paragraph(string Text) : EmailBlock;

    /// <summary>A heading over the blocks after it.</summary>
    public sealed record Heading(string Text) : EmailBlock;

    /// <summary>The email's main link, as a button with the address under it for mail programs that hide buttons.</summary>
    public sealed record Button(string Label, Uri Url) : EmailBlock;

    /// <summary>Another link, on a line of its own.</summary>
    public sealed record Link(Uri Url) : EmailBlock;

    /// <summary>Numbered steps.</summary>
    public sealed record Steps(IReadOnlyList<string> Items) : EmailBlock;

    /// <summary>Labels with their values, such as a receipt's lines.</summary>
    public sealed record Table(IReadOnlyList<EmailRow> Rows) : EmailBlock;

    /// <summary>A message someone wrote, quoted.</summary>
    public sealed record Quote(string Text) : EmailBlock;

    /// <summary>Small print, such as how long a link works.</summary>
    public sealed record Note(string Text) : EmailBlock;
}

/// <summary>A line of a table. A <paramref name="Strong"/> one stands out, as a total does.</summary>
internal sealed record EmailRow(string Label, string Value, bool Strong = false);

/// <summary>
/// How an email looks: the page around it, its card, text, lines and button, the fonts, the corners and a colored line
/// over the card. <paramref name="HeadingStyle"/> is the inline style of headings.
/// </summary>
internal sealed record EmailLook(
    string Page,
    string Card,
    string Text,
    string Muted,
    string Border,
    string Accent,
    string OnAccent,
    string Font,
    string HeadingStyle,
    int Radius,
    string? TopRule = null);

/// <summary>
/// Writes an email's blocks as plain text and as HTML, for our emails (<see cref="PlatformEmails"/>) and the firms' to their
/// traders (<see cref="TraderEmails"/>). The HTML says the same as the text, in layout tables with inline styles, which mail
/// programs show the same, with no web fonts and no images but a firm's logo.
/// </summary>
internal static partial class EmailLayout
{
    /// <summary>The first line of every email.</summary>
    public const string Greeting = "Hi,";

    /// <summary>Fonts every computer and phone has.</summary>
    public const string SansSerif = "-apple-system,'Segoe UI',Helvetica,Arial,sans-serif";

    /// <summary>The blocks as plain text, with an empty line between them.</summary>
    public static string Text(IEnumerable<EmailBlock> blocks) => string.Join("\n\n", blocks.Select(TextOf));

    /// <summary>
    /// The blocks of an email written as plain text, separated by empty lines. The first block that is only a link becomes a
    /// button saying <paramref name="button"/>, and later ones links. Numbered lines become steps, lines that start with
    /// &gt; a quote, and lines of "label: value" a table.
    /// </summary>
    public static IReadOnlyList<EmailBlock> Parse(string text, string? button)
    {
        var blocks = new List<EmailBlock>();
        foreach (var lines in Paragraphs(text))
        {
            if (lines is [var only] && IsLink(only, out var url))
            {
                blocks.Add(blocks.Any(b => b is EmailBlock.Button) ? new EmailBlock.Link(url) : new EmailBlock.Button(button ?? "Open", url));
            }
            else if (lines.TrueForAll(l => StepLine().IsMatch(l)))
            {
                blocks.Add(new EmailBlock.Steps([.. lines.Select(l => StepLine().Replace(l, ""))]));
            }
            else if (lines.TrueForAll(l => l.StartsWith('>')))
            {
                blocks.Add(new EmailBlock.Quote(string.Join('\n', lines.Select(l => l.StartsWith("> ", StringComparison.Ordinal) ? l[2..] : l[1..]))));
            }
            else if (lines.Count > 1 && lines.TrueForAll(l => RowLine().IsMatch(l)))
            {
                blocks.Add(new EmailBlock.Table([.. lines.Select(l => RowLine().Match(l)).Select(m => new EmailRow(m.Groups[1].Value, m.Groups[2].Value))]));
            }
            else
            {
                blocks.Add(new EmailBlock.Paragraph(string.Join('\n', lines)));
            }
        }

        return blocks;
    }

    /// <summary>
    /// The email as HTML: <paramref name="header"/>, which is HTML, over the greeting and the blocks, and the
    /// <paramref name="footer"/> lines under a line, the first one stronger.
    /// </summary>
    public static string Html(EmailLook look, string header, IReadOnlyList<EmailBlock> blocks, IReadOnlyList<string> footer)
    {
        var html = new StringBuilder();
        var topRule = look.TopRule is { } rule ? $"border-top:3px solid {rule};" : "";
        html.Append(CultureInfo.InvariantCulture, $"""
            <!doctype html>
            <html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="color-scheme" content="light"></head>
            <body style="margin:0;padding:0;background:{look.Page}">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:{look.Page};padding:24px 12px">
            <tr><td align="center">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;background:{look.Card};border:1px solid {look.Border};{topRule}border-radius:{look.Radius}px;font-family:{look.Font};font-size:15px;line-height:1.5;color:{look.Text}">
            <tr><td style="padding:24px 28px 8px">{header}</td></tr>
            <tr><td style="padding:8px 28px 4px">
            <p style="margin:0 0 14px">{Greeting}</p>

            """);
        foreach (var block in blocks)
        {
            html.Append(HtmlOf(block, look)).Append('\n');
        }

        html.Append(CultureInfo.InvariantCulture, $"""
            </td></tr>
            <tr><td style="padding:16px 28px 24px;border-top:1px solid {look.Border};font-size:13px;line-height:1.5;color:{look.Muted}">

            """);
        foreach (var (line, i) in footer.Select((l, i) => (l, i)))
        {
            var strong = i == 0 ? $";color:{look.Text};font-weight:600" : "";
            html.Append(CultureInfo.InvariantCulture, $"""<p style="margin:0 0 4px{strong}">{Inline(line, look.Muted)}</p>""").Append('\n');
        }

        html.Append("""
            </td></tr>
            </table>
            </td></tr>
            </table>
            </body></html>
            """);
        return html.ToString();
    }

    public static string Encode(string text) => WebUtility.HtmlEncode(text);

    private static string TextOf(EmailBlock block) => block switch
    {
        EmailBlock.Paragraph paragraph => paragraph.Text,
        EmailBlock.Heading heading => heading.Text,
        EmailBlock.Button button => button.Url.ToString(),
        EmailBlock.Link link => link.Url.ToString(),
        EmailBlock.Steps steps => string.Join('\n', steps.Items.Select((item, i) => string.Create(CultureInfo.InvariantCulture, $"{i + 1}. {item}"))),
        EmailBlock.Table table => string.Join('\n', table.Rows.Select(r => $"{r.Label}: {r.Value}")),
        EmailBlock.Quote quote => string.Join('\n', quote.Text.Split('\n').Select(line => line.Length == 0 ? ">" : $"> {line}")),
        EmailBlock.Note note => note.Text,
        _ => throw new ArgumentOutOfRangeException(nameof(block), block, "Unknown email block."),
    };

    private static string HtmlOf(EmailBlock block, EmailLook look)
    {
        switch (block)
        {
            case EmailBlock.Paragraph paragraph:
                return $"""<p style="margin:0 0 14px">{Inline(paragraph.Text, look.Text)}</p>""";
            case EmailBlock.Heading heading:
                return $"""<h2 style="margin:26px 0 8px;{look.HeadingStyle};line-height:1.3;color:{look.Text}">{Encode(heading.Text)}</h2>""";
            case EmailBlock.Button button:
                var href = Encode(button.Url.AbsoluteUri);
                return $"""
                    <p style="margin:20px 0"><a href="{href}" style="display:inline-block;background:{look.Accent};color:{look.OnAccent};padding:11px 20px;border-radius:{Math.Min(look.Radius, 6)}px;text-decoration:none;font-weight:600">{Encode(button.Label)}</a></p>
                    <p style="margin:0 0 14px;font-size:13px;color:{look.Muted}">Or open this link: <a href="{href}" style="color:{look.Muted};word-break:break-all">{href}</a></p>
                    """;
            case EmailBlock.Link link:
                var url = Encode(link.Url.AbsoluteUri);
                return $"""<p style="margin:0 0 14px"><a href="{url}" style="color:{look.Text};word-break:break-all">{url}</a></p>""";
            case EmailBlock.Steps steps:
                var items = string.Concat(steps.Items.Select(item => $"""<li style="margin:0 0 6px;padding-left:4px">{Inline(item, look.Text)}</li>"""));
                return $"""<ol style="margin:0 0 14px;padding:0 0 0 22px">{items}</ol>""";
            case EmailBlock.Table table:
                var rows = string.Concat(table.Rows.Select(row =>
                {
                    var cell = $"padding:7px 0;border-bottom:1px solid {look.Border};vertical-align:top";
                    var strong = row.Strong ? ";font-weight:600" : "";
                    var label = row.Strong ? look.Text : look.Muted;
                    return $"""<tr><td style="{cell};padding-right:16px;color:{label}{strong}">{Encode(row.Label)}</td><td style="{cell};text-align:right{strong}">{Inline(row.Value, look.Text)}</td></tr>""";
                }));
                return $"""<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:0 0 14px;border-collapse:collapse;font-size:14px">{rows}</table>""";
            case EmailBlock.Quote quote:
                return $"""<blockquote style="margin:0 0 14px;padding:2px 0 2px 14px;border-left:3px solid {look.Border};color:{look.Muted}">{Inline(quote.Text, look.Muted)}</blockquote>""";
            case EmailBlock.Note note:
                return $"""<p style="margin:0 0 14px;font-size:13px;color:{look.Muted}">{Inline(note.Text, look.Muted)}</p>""";
            default:
                throw new ArgumentOutOfRangeException(nameof(block), block, "Unknown email block.");
        }
    }

    // Text as HTML, with its web addresses and email addresses as links in the color, and its own lines kept.
    private static string Inline(string text, string color)
    {
        var html = new StringBuilder();
        var at = 0;
        foreach (Match match in Linkable().Matches(text))
        {
            // Punctuation after an address ends the sentence, not the address.
            var value = match.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', '"', '\'');
            var href = value.StartsWith("http", StringComparison.Ordinal) ? value : $"mailto:{value}";
            html.Append(Encode(text[at..match.Index]));
            html.Append(CultureInfo.InvariantCulture, $"""<a href="{Encode(href)}" style="color:{color};word-break:break-all">{Encode(value)}</a>""");
            at = match.Index + value.Length;
        }

        html.Append(Encode(text[at..]));
        return html.ToString().Replace("\n", "<br>", StringComparison.Ordinal);
    }

    // Lines that are not empty, grouped where empty lines separate them.
    private static List<List<string>> Paragraphs(string text)
    {
        var paragraphs = new List<List<string>>();
        var current = new List<string>();
        foreach (var line in text.Split('\n').Select(l => l.TrimEnd()))
        {
            if (line.Length > 0)
            {
                current.Add(line);
            }
            else if (current.Count > 0)
            {
                paragraphs.Add(current);
                current = [];
            }
        }

        if (current.Count > 0)
        {
            paragraphs.Add(current);
        }

        return paragraphs;
    }

    private static bool IsLink(string line, [NotNullWhen(true)] out Uri? url) =>
        Uri.TryCreate(line, UriKind.Absolute, out url) && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp) && !line.Contains(' ', StringComparison.Ordinal);

    [GeneratedRegex(@"^\d+\. ")]
    private static partial Regex StepLine();

    [GeneratedRegex(@"^(.+?): (.+)$")]
    private static partial Regex RowLine();

    [GeneratedRegex(@"https?://[^\s<>""]+|[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+")]
    private static partial Regex Linkable();
}
