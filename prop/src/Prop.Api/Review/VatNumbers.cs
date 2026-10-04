using System.Text.RegularExpressions;

namespace Prop.Api.Review;

/// <summary>
/// The firm's VAT number in its application. We are a Swedish company, and a firm in another EU country pays no
/// Swedish VAT only with a VAT number (reverse charge). No country makes every company register for VAT, so a firm
/// in the EU gives its VAT number or says it has none, and then pays Swedish VAT. A firm outside the EU pays no
/// Swedish VAT, so for it the number is optional.
/// </summary>
internal static partial class VatNumbers
{
    public const int MaxLength = 30;

    /// <summary>The EU countries, where a firm gives its VAT number or says it has none.</summary>
    public static IReadOnlyList<string> EuCountries { get; } =
    [
        "AT", "BE", "BG", "CY", "CZ", "DE", "DK", "EE", "ES", "FI", "FR", "GR", "HR", "HU",
        "IE", "IT", "LT", "LU", "LV", "MT", "NL", "PL", "PT", "RO", "SE", "SI", "SK",
    ];

    /// <summary>The VAT number in capitals without spaces, dots or dashes, as the EU's VAT register writes it. Null when nothing is left.</summary>
    public static string? Normalize(string? vatNumber) =>
        vatNumber is null ? null : Separators().Replace(vatNumber, "").ToUpperInvariant() is { Length: > 0 } normalized ? normalized : null;

    /// <summary>
    /// What is wrong with a normalized VAT number for a company in <paramref name="country"/>, or with
    /// <paramref name="complete"/> that a company in the EU has neither given one nor said it has none. Null when
    /// there is nothing wrong.
    /// </summary>
    public static FieldProblem? Problem(string? vatNumber, bool noVatNumber, string? country, bool complete)
    {
        if (vatNumber is null)
        {
            return complete && !noVatNumber && country is not null && EuCountries.Contains(country)
                ? new FieldProblem("vatNumber", "Fill in the company's VAT number, or tick that it has none.")
                : null;
        }

        if (country is not null && EuCountries.Contains(country))
        {
            // An EU VAT number starts with the country code, except in Greece.
            var prefix = country == "GR" ? "EL" : country;
            return vatNumber.StartsWith(prefix, StringComparison.Ordinal) && EuNumber().IsMatch(vatNumber[prefix.Length..])
                ? null
                : new FieldProblem("vatNumber", $"Write the VAT number with {prefix} first, as in the EU's VAT register.");
        }

        return OtherNumber().IsMatch(vatNumber)
            ? null
            : new FieldProblem("vatNumber", FormattableString.Invariant($"Write the VAT or tax number in at most {MaxLength} letters and digits."));
    }

    // People write spaces, dots and dashes in the number, but the EU's VAT register leaves them out.
    [GeneratedRegex(@"[\s.\-]")]
    private static partial Regex Separators();

    // What follows the prefix of any EU VAT number: 2 to 12 digits or letters, and + or * in Ireland's.
    [GeneratedRegex(@"^[0-9A-Z+*]{2,12}$")]
    private static partial Regex EuNumber();

    [GeneratedRegex("^[0-9A-Z]{1,30}$")]
    private static partial Regex OtherNumber();
}
