using System.Diagnostics;

namespace Prop.Api.Tests.Support;

internal static class Eventually
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Waits in real time until the condition holds, for work that finishes on another thread.</summary>
    public static async Task ThatAsync(Func<bool> condition, string what)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.Elapsed > Timeout)
            {
                throw new TimeoutException($"Timed out waiting for {what}.");
            }

            await Task.Delay(5);
        }
    }

    public static async Task ThatAsync(Func<Task<bool>> condition, string what)
    {
        var elapsed = Stopwatch.StartNew();
        while (!await condition())
        {
            if (elapsed.Elapsed > Timeout)
            {
                throw new TimeoutException($"Timed out waiting for {what}.");
            }

            await Task.Delay(5);
        }
    }

    public static Task<T> Within<T>(Task<T> task) => task.WaitAsync(Timeout);
}
