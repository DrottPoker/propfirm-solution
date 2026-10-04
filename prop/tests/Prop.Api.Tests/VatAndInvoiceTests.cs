using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using Prop.Api.Billing;
using Prop.Api.Configuration;

namespace Prop.Api.Tests;

/// <summary>VAT on what firms pay us, by country and VAT number, and the invoice of a paid charge as a PDF (ADR 0032).</summary>
public sealed partial class VatAndInvoiceTests
{
    private static readonly BillingTerms Terms = new("USD", 700m, 500m, 25, [new SlotPrice(26, 5m), new SlotPrice(101, 4m)], 10_000, 5, 200m, 25m, "SE");

    [Theory]
    [InlineData("SE", "SE559000123401", VatTreatment.Charged, "25")]
    [InlineData("SE", null, VatTreatment.Charged, "25")]
    [InlineData("DE", "DE123456789", VatTreatment.ReverseCharge, "0")]
    [InlineData("DE", null, VatTreatment.Charged, "25")]
    [InlineData("AE", null, VatTreatment.OutsideEu, "0")]
    [InlineData("US", "123456789", VatTreatment.OutsideEu, "0")]
    [InlineData(null, null, VatTreatment.Charged, "25")]
    public void VatFollowsTheCountryAndTheVatNumber(string? country, string? vatNumber, VatTreatment treatment, string percent)
    {
        var vat = VatRules.Of(country is null ? null : new ChargeCustomer("Acme", "1", "Street 1", country, vatNumber), Terms);

        Assert.Equal((treatment, decimal.Parse(percent, CultureInfo.InvariantCulture)), (vat.Treatment, vat.Percent));
    }

    [Fact]
    public void VatIsRoundedToWholeCents()
    {
        Assert.Equal(289.31m, VatRules.On(1157.25m, new VatResponse(VatTreatment.Charged, 25m)).Amount);
        Assert.Equal(0m, VatRules.On(1157.25m, new VatResponse(VatTreatment.ReverseCharge, 0m)).Amount);
    }

    [Fact]
    public void InvoiceNumbersAreInTheFirmsOwnSeries()
    {
        Assert.Equal(("ACME-0001", "NORDIC-EDGE-0042"), (BillingRules.InvoiceOf("acme", 1), BillingRules.InvoiceOf("nordic-edge", 42)));
    }

    // Monday 5 October 2026: 27 of October's 31 days are left.
    [Fact]
    public void AnExpansionAddsItsSlotsMonthlyAndForTheRestOfTheMonth()
    {
        var october5 = new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

        Assert.Equal((10, 50m, 43.55m), BillingRules.Expansion(october5, 30, 10, Terms));
        Assert.Equal((10, 45m, 39.19m), BillingRules.Expansion(october5, 95, 10, Terms));
        Assert.Equal((5, 20m, 17.42m), BillingRules.Expansion(october5, 9_995, 10, Terms with { MaxSlots = 10_000 }));
    }

    [Fact]
    public void TheInvoiceIsAPdfWithTheChargeTheVatAndBothCompanies()
    {
        var paidAt = new DateTimeOffset(2026, 10, 5, 9, 30, 0, TimeSpan.Zero);
        var charge = new Charge(
            Guid.CreateVersion7(paidAt),
            "acme",
            1002,
            ChargeKind.Activation,
            ChargeStatus.Paid,
            new DateOnly(2026, 10, 1),
            1,
            30,
            [new ChargeLine("Startup fee, less the deposit of 200.00 USD", 1, 500m), new ChargeLine("Package with 25 slots, October 2026 (27 of 31 days)", 25, 435.48m)],
            935.48m,
            new ChargeVat(VatTreatment.Charged, 25m, 233.87m),
            1169.35m,
            "USD",
            new ChargeCustomer("Acme Trading AB", "559000-1234", "Storgatan 1\n111 22 Stockholm", "SE", "SE559000123401"),
            BillingProvider.Test,
            "test_payment",
            null,
            1,
            null,
            paidAt,
            paidAt,
            null,
            2);
        var seller = new SellerOptions { Name = "Prop Platform AB", Address = "Kungsgatan 1\n111 43 Stockholm", OrganizationNumber = "559000-0000", VatNumber = "SE559000000001" };

        var pdf = InvoicePdf.Render(charge, "Firm acme", seller);
        var text = Encoding.Latin1.GetString(pdf);

        Assert.StartsWith("%PDF-1.4", text, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);
        foreach (var expected in new[] { "(ACME-0002)", "(Acme Trading AB)", "(Prop Platform AB)", "(VAT 25%)", "(233.87)", "(1,169.35 USD)", "(Paid by card on 5 Oct 2026)" })
        {
            Assert.Contains(expected, text, StringComparison.Ordinal);
        }

        // Every object is where the cross-reference table says, so readers open it without repairing it.
        var startXref = long.Parse(StartXref().Match(text).Groups[1].Value, CultureInfo.InvariantCulture);
        var offsets = XrefEntry().Matches(text[(int)startXref..]).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(7, offsets.Count);
        for (var i = 0; i < offsets.Count; i++)
        {
            Assert.StartsWith($"{i + 1} 0 obj", text[offsets[i]..], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AReverseChargeInvoiceSaysWhyThereIsNoVat()
    {
        var paidAt = new DateTimeOffset(2026, 10, 5, 9, 30, 0, TimeSpan.Zero);
        var charge = new Charge(
            Guid.CreateVersion7(paidAt), "acme", 1001, ChargeKind.Deposit, ChargeStatus.Paid, new DateOnly(2026, 10, 1), 0, 0,
            [new ChargeLine("Review deposit, taken off the startup fee", 1, 200m)], 200m, new ChargeVat(VatTreatment.ReverseCharge, 0m, 0m), 200m, "USD",
            new ChargeCustomer("Acme GmbH", "HRB 1", "Berlin", "DE", "DE123456789"), BillingProvider.Test, null, null, 1, null, paidAt, paidAt, null, 1);

        var text = Encoding.Latin1.GetString(InvoicePdf.Render(charge, "Firm acme", new SellerOptions { Name = "Prop Platform AB" }));

        // Parentheses in PDF text are escaped.
        Assert.Contains(@"(Reverse charge: the buyer accounts for the VAT \(Article 196 of Council Directive 2006/112/EC\).)", text, StringComparison.Ordinal);
        Assert.Contains("(VAT 0%)", text, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"startxref\n(\d+)\n%%EOF")]
    private static partial Regex StartXref();

    [GeneratedRegex(@"(\d{10}) 00000 n ")]
    private static partial Regex XrefEntry();
}
