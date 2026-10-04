using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Prop.Api.Tests.Support;

/// <summary>
/// Remembers why the service failed to start, from the host's own log. The service starts on a thread of its own,
/// and when it fails at once it disposes its host, which the test factory may then only see as a disposed object.
/// The host logs the failure before it is disposed, so the reason is kept here.
/// </summary>
internal sealed class StartupFailureLog : ILoggerProvider
{
    private const string HostCategory = "Microsoft.Extensions.Hosting.Internal.Host";

    private Exception? _failure;

    /// <summary>The first error the host logged with an exception, such as "Hosting failed to start". Null while there is none.</summary>
    public Exception? Failure => Volatile.Read(ref _failure);

    public ILogger CreateLogger(string categoryName) => categoryName == HostCategory ? new Recorder(this) : NullLogger.Instance;

    public void Dispose()
    {
    }

    private sealed class Recorder(StartupFailureLog log) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error && exception is not null)
            {
                Interlocked.CompareExchange(ref log._failure, exception, null);
            }
        }
    }
}
