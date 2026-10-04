using Prop.Api.Review;

namespace Prop.Api.Tests;

/// <summary>
/// The VAT number in the application: a company in the EU gives it or says it has none, and it is written as the
/// EU's VAT register writes it.
/// </summary>
public sealed class VatNumbersTests
{
    [Fact]
    public void AFirmInTheEuGivesItsNumberOrSaysItHasNone()
    {
        Assert.Equal("vatNumber", VatNumbers.Problem(null, noVatNumber: false, "DE", complete: true)?.Field);
        Assert.Equal("vatNumber", VatNumbers.Problem(null, noVatNumber: false, "SE", complete: true)?.Field);
        Assert.Null(VatNumbers.Problem(null, noVatNumber: true, "SE", complete: true));
        Assert.Null(VatNumbers.Problem(null, noVatNumber: false, "AE", complete: true));
        Assert.Null(VatNumbers.Problem(null, noVatNumber: false, "DE", complete: false));
        Assert.Equal(27, VatNumbers.EuCountries.Count);
        Assert.Contains("GR", VatNumbers.EuCountries);
        Assert.DoesNotContain("GB", VatNumbers.EuCountries);
    }

    [Fact]
    public void ItIsWrittenInCapitalsWithoutSpacesDotsOrDashes()
    {
        Assert.Equal("SE559000123401", VatNumbers.Normalize(" se 5590-0012.3401 "));
        Assert.Null(VatNumbers.Normalize(" - . "));
        Assert.Null(VatNumbers.Normalize(null));
    }

    [Theory]
    [InlineData("DE123456789", "DE")]
    [InlineData("SE559000123401", "SE")]
    [InlineData("EL123456789", "GR")]
    [InlineData("IE1234567FA", "IE")]
    [InlineData("FRXX123456789", "FR")]
    [InlineData("100123456700003", "AE")]
    [InlineData("GB123456789", "GB")]
    [InlineData("DE123456789", null)]
    public void ANumberInTheRightFormIsTaken(string vatNumber, string? country)
    {
        Assert.Null(VatNumbers.Problem(vatNumber, noVatNumber: false, country, complete: true));
    }

    [Theory]
    [InlineData("123456789", "DE")]
    [InlineData("SE559000123401", "DE")]
    [InlineData("GR123456789", "GR")]
    [InlineData("DE1", "DE")]
    [InlineData("DE1234567890123", "DE")]
    [InlineData("1234567890123456789012345678901", "AE")]
    [InlineData("TRN/123", "AE")]
    public void ANumberInTheWrongFormIsRefused(string vatNumber, string country)
    {
        Assert.Equal("vatNumber", VatNumbers.Problem(vatNumber, noVatNumber: false, country, complete: false)?.Field);
    }
}
