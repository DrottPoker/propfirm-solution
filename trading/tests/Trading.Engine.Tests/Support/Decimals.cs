using System.Globalization;

namespace Trading.Engine.Tests.Support;

/// <summary>Attributes cannot hold decimals, so theory data passes them as strings.</summary>
internal static class Decimals
{
    public static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    public static decimal? ParseOptional(string? value) => value is null ? null : Parse(value);
}
