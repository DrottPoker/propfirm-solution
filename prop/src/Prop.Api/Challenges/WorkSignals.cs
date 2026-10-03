using System.Collections.Concurrent;

namespace Prop.Api.Challenges;

/// <summary>
/// Wakes the background workers as soon as there is new work, instead of waiting for their next look in the
/// database. The database is still the truth: a signal that is lost only delays the work.
/// </summary>
internal sealed class WorkSignals
{
    private readonly ConcurrentDictionary<string, WakeUp> _commands = new(StringComparer.Ordinal);

    public WakeUp Webhooks { get; } = new();

    public WakeUp CommandsOf(string firmId) => _commands.GetOrAdd(firmId, _ => new WakeUp());
}

/// <summary>Set by whoever adds work; a wait returns once it is set and resets it.</summary>
internal sealed class WakeUp
{
    private readonly Lock _lock = new();
    private TaskCompletionSource _next = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Set()
    {
        lock (_lock)
        {
            _next.TrySetResult();
        }
    }

    /// <summary>Returns when set, at once if it was set since the last wait, or after the timeout.</summary>
    public async Task WaitAsync(TimeSpan timeout, TimeProvider time, CancellationToken cancellationToken)
    {
        Task next;
        lock (_lock)
        {
            next = _next.Task;
        }

        try
        {
            await next.WaitAsync(timeout, time, cancellationToken);
        }
        catch (TimeoutException)
        {
        }

        lock (_lock)
        {
            if (_next.Task.IsCompleted)
            {
                _next = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }
}
