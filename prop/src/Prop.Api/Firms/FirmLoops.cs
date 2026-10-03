namespace Prop.Api.Firms;

/// <summary>
/// Runs a background loop for each firm that is ready for it, and starts loops for firms that become ready
/// later, such as a firm whose server on the trading platform was just created. A loop gets the firm's id and
/// reads the firm from the catalog each time, so it sees changes such as a new key.
/// </summary>
internal static class FirmLoops
{
    public static async Task RunAsync(FirmCatalog firms, Func<Firm, bool> isReady, Func<string, CancellationToken, Task> loop, CancellationToken cancellationToken)
    {
        var running = new Dictionary<string, Task>(StringComparer.Ordinal);
        try
        {
            await firms.Ready.WaitAsync(cancellationToken);
            while (true)
            {
                var (all, changed) = firms.Watch();
                foreach (var firm in all.Where(f => isReady(f) && !running.ContainsKey(f.Id)))
                {
                    running.Add(firm.Id, loop(firm.Id, cancellationToken));
                }

                await changed.WaitAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        await Task.WhenAll(running.Values);
    }
}
