using System.Globalization;
using System.Text.RegularExpressions;

namespace Prop.Api.Review;

/// <summary>One of the firm's owners, with the share of the company in percent.</summary>
public sealed record FirmOwner(string? Name, decimal? SharePercent);

/// <summary>
/// The firm's application for our review (ADR 0021): its company, owners and links we can check. Any field may be
/// empty in a draft. What is required is checked when the application is sent.
/// </summary>
public sealed record FirmApplication(
    string? CompanyName,
    string? RegistrationNumber,
    string? Country,
    string? Address,
    string? Website,
    string? ContactName,
    string? ContactPhone,
    IReadOnlyList<FirmOwner>? Owners,
    string? TermsUrl,
    IReadOnlyList<string>? Links,
    string? Description)
{
    public static readonly FirmApplication Empty = new(null, null, null, null, null, null, null, [], null, [], null);
}

/// <summary>What is wrong with a field of the application.</summary>
internal sealed record FieldProblem(string Field, string Problem);

/// <summary>The rules for an application, the same for a draft and one that is sent.</summary>
internal static partial class ApplicationRules
{
    public const int MaxOwners = 10;
    public const int MaxLinks = 5;
    public const int MaxUrlLength = 500;
    public const int MaxDescriptionLength = 2_000;

    /// <summary>The application with text trimmed, empty text as none and the country in capitals.</summary>
    public static FirmApplication Normalize(FirmApplication application) =>
        new(
            Clean(application.CompanyName),
            Clean(application.RegistrationNumber),
            Clean(application.Country)?.ToUpperInvariant(),
            Clean(application.Address),
            Clean(application.Website),
            Clean(application.ContactName),
            Clean(application.ContactPhone),
            [.. (application.Owners ?? []).Select(o => new FirmOwner(Clean(o.Name), o.SharePercent)).Where(o => o.Name is not null || o.SharePercent is not null)],
            Clean(application.TermsUrl),
            [.. (application.Links ?? []).Select(Clean).OfType<string>()],
            Clean(application.Description));

    /// <summary>
    /// The first problem with the fields that are filled in, and with <paramref name="complete"/> also with what is
    /// missing. Null when there is none. Expects a normalized application.
    /// </summary>
    public static FieldProblem? Problem(FirmApplication application, bool complete)
    {
        var owners = application.Owners ?? [];
        var links = application.Links ?? [];
        return Text("companyName", "the company's legal name", application.CompanyName, 200, complete)
            ?? Text("registrationNumber", "the company's registration number", application.RegistrationNumber, 100, complete)
            ?? CountryProblem(application.Country, complete)
            ?? Text("address", "the company's registered address", application.Address, 500, complete)
            ?? Url("website", "The website", application.Website, required: false)
            ?? Text("contactName", "the name of the person we talk to", application.ContactName, 200, complete)
            ?? Text("contactPhone", "a phone number", application.ContactPhone, 50, required: false)
            ?? OwnersProblem(owners, complete)
            ?? Url("termsUrl", "Your terms for traders", application.TermsUrl, complete)
            ?? (links.Count > MaxLinks ? new FieldProblem("links", $"Add at most {MaxLinks} links.") : null)
            ?? links.Select(link => Url("links", "Each link", link, required: true)).FirstOrDefault(p => p is not null)
            ?? Text("description", "a description", application.Description, MaxDescriptionLength, required: false);
    }

    private static FieldProblem? Text(string field, string what, string? value, int maxLength, bool required) =>
        value is null
            ? required ? new FieldProblem(field, $"Fill in {what}.") : null
            : value.Length > maxLength
                ? new FieldProblem(field, FormattableString.Invariant($"Write {what} in at most {maxLength:N0} characters."))
                : null;

    private static FieldProblem? CountryProblem(string? country, bool required) =>
        country is null
            ? required ? new FieldProblem("country", "Choose the country where the company is registered.") : null
            : CountryCode().IsMatch(country) ? null : new FieldProblem("country", "Choose the country as its two-letter code, for example SE.");

    private static FieldProblem? Url(string field, string what, string? value, bool required) =>
        value is null
            ? required ? new FieldProblem(field, $"{what} must be filled in.") : null
            : value.Length <= MaxUrlLength && Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps
                ? null
                : new FieldProblem(field, $"{what} must be an https address.");

    private static FieldProblem? OwnersProblem(IReadOnlyList<FirmOwner> owners, bool complete)
    {
        if (owners.Count == 0)
        {
            return complete ? new FieldProblem("owners", "Add the company's owners.") : null;
        }

        if (owners.Count > MaxOwners)
        {
            return new FieldProblem("owners", $"Add at most {MaxOwners} owners.");
        }

        for (var i = 0; i < owners.Count; i++)
        {
            var number = (i + 1).ToString(CultureInfo.InvariantCulture);
            if (owners[i].Name is not { } name ? complete : name.Length > 200)
            {
                return new FieldProblem("owners", $"Write the name of owner {number} in at most 200 characters.");
            }

            if (owners[i].SharePercent is not { } share ? complete : share is <= 0 or > 100 || decimal.Round(share, 2) != share)
            {
                return new FieldProblem("owners", $"Give owner {number} a share above 0 and at most 100 percent, with at most two decimals.");
            }
        }

        return owners.Sum(o => o.SharePercent ?? 0) > 100 ? new FieldProblem("owners", "The owners' shares add up to more than 100 percent.") : null;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    [GeneratedRegex("^[A-Z]{2}$")]
    private static partial Regex CountryCode();
}
