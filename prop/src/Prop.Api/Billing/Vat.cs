using System.Text.Json;

using Prop.Api.Json;
using Prop.Api.Review;

namespace Prop.Api.Billing;

/// <summary>How VAT applies to a charge (ADR 0032). Prices are without VAT, and we are a company in Billing:Seller:Country.</summary>
public enum VatTreatment
{
    /// <summary>Our VAT is added: for a company in our country, one in another EU country without a VAT number, and one we know no country of.</summary>
    Charged,

    /// <summary>A company in another EU country with a VAT number accounts for the VAT itself (reverse charge).</summary>
    ReverseCharge,

    /// <summary>A company outside the EU pays no VAT to us.</summary>
    OutsideEu,

    /// <summary>A charge from before VAT was recorded.</summary>
    NotRecorded,
}

/// <summary>Who a charge is to, from the firm's application when the charge was made. Kept with the charge for its invoice.</summary>
public sealed record ChargeCustomer(string? CompanyName, string? RegistrationNumber, string? Address, string? Country, string? VatNumber)
{
    internal static ChargeCustomer From(FirmApplication application) =>
        new(application.CompanyName, application.RegistrationNumber, application.Address, application.Country, application.VatNumber);

    internal string ToJson() => JsonSerializer.Serialize(this, PropJson.Options);

    internal static ChargeCustomer? FromJson(string? json) => json is null ? null : JsonSerializer.Deserialize<ChargeCustomer>(json, PropJson.Options);
}

/// <summary>How VAT applies to the firm now, and the percent added to its charges.</summary>
public sealed record VatResponse(VatTreatment Treatment, decimal Percent);

/// <summary>The VAT of a charge: how it applied, the percent and the amount.</summary>
internal sealed record ChargeVat(VatTreatment Treatment, decimal Percent, decimal Amount);

/// <summary>The rules for VAT on what firms pay us, the same for every charge.</summary>
internal static class VatRules
{
    /// <summary>
    /// How VAT applies to the customer: by its country, and in the EU by whether it has a VAT number. A customer we know no
    /// country of pays our VAT, which is never too little.
    /// </summary>
    public static VatResponse Of(ChargeCustomer? customer, BillingTerms terms)
    {
        var country = customer?.Country;
        if (country is null || country == terms.SellerCountry)
        {
            return new VatResponse(VatTreatment.Charged, terms.VatPercent);
        }

        if (VatNumbers.EuCountries.Contains(country))
        {
            return customer!.VatNumber is null ? new VatResponse(VatTreatment.Charged, terms.VatPercent) : new VatResponse(VatTreatment.ReverseCharge, 0);
        }

        return new VatResponse(VatTreatment.OutsideEu, 0);
    }

    /// <summary>The VAT on an amount without VAT, rounded to whole cents.</summary>
    public static ChargeVat On(decimal net, VatResponse vat) =>
        new(vat.Treatment, vat.Percent, decimal.Round(net * vat.Percent / 100m, 2, MidpointRounding.AwayFromZero));

    /// <summary>What an invoice says about VAT that is not added. Null when it is added, or was not recorded.</summary>
    public static string? NoteOf(VatTreatment treatment) => treatment switch
    {
        VatTreatment.ReverseCharge => "Reverse charge: the buyer accounts for the VAT (Article 196 of Council Directive 2006/112/EC).",
        VatTreatment.OutsideEu => "No VAT: the service is supplied to a business outside the EU.",
        _ => null,
    };
}
