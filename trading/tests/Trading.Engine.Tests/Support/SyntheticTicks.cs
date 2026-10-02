namespace Trading.Engine.Tests.Support;

/// <summary>
/// Deterministic EURUSD random walk. Uses its own linear congruential generator so the
/// sequence never depends on System.Random or the .NET version.
/// </summary>
internal static class SyntheticTicks
{
    public static IEnumerable<(decimal Bid, decimal Ask)> EurUsd(int count)
    {
        var state = 0x2545F4914F6CDD1DUL;
        var bidPoints = 108_000L;
        for (var i = 0; i < count; i++)
        {
            state = unchecked((state * 6364136223846793005UL) + 1442695040888963407UL);
            var step = (long)((state >> 33) % 11) - 5;
            var spreadPoints = 1 + (long)((state >> 17) % 3);
            bidPoints += step;
            yield return (bidPoints / 100_000m, (bidPoints + spreadPoints) / 100_000m);
        }
    }
}
