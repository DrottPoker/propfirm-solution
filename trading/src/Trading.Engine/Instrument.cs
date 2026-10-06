namespace Trading.Engine;

/// <summary>Contract specification of a tradable symbol.</summary>
/// <param name="ContractSize">Units of the base currency per lot, for example 100 000 for forex and 100 for gold.</param>
/// <param name="Digits">Number of price decimals. One point is 10^-Digits.</param>
/// <param name="VolumeMin">Smallest order volume in lots.</param>
/// <param name="VolumeStep">Order volumes must be a multiple of this.</param>
/// <param name="VolumeMax">Largest order volume in lots.</param>
/// <param name="TradingHours">When the market is open. Null for a market that never closes, like crypto.</param>
public sealed record Instrument(
    string Symbol,
    string BaseCurrency,
    string QuoteCurrency,
    decimal ContractSize,
    int Digits,
    decimal VolumeMin,
    decimal VolumeStep,
    decimal VolumeMax,
    TradingHours? TradingHours = null)
{
    /// <summary>Smallest price increment.</summary>
    public decimal Point => new(1, 0, 0, false, (byte)Digits);

    internal bool IsOnGrid(decimal price) => price % Point == 0m;

    internal bool IsOpen(DateTimeOffset at) => TradingHours?.IsOpen(at) ?? true;
}
