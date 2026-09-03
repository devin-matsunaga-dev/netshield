using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

namespace NetShield.IntegrationTests.Identity;

/// <summary>
/// Captures every log line the host writes, at every level, so a test can assert what did not
/// appear in one.
/// </summary>
/// <remarks>
/// Registered as an <see cref="ILoggerProvider"/> rather than replacing the factory, so it sits
/// behind the platform's redaction exactly where a console or OpenTelemetry sink would.
/// </remarks>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<RecordedLog> _records = new();

    /// <summary>Everything written so far.</summary>
    public IReadOnlyList<RecordedLog> Records => [.. _records];

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, _records);

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(string category, ConcurrentQueue<RecordedLog> records) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            // Rendered here, while the request that produced it is still alive.
            IReadOnlyList<string> values = state is IReadOnlyList<KeyValuePair<string, object?>> properties
                ? [.. properties.Select(property => property.Value?.ToString() ?? string.Empty)]
                : [];

            records.Enqueue(new RecordedLog(
                logLevel,
                category,
                formatter(state, exception),
                values));
        }
    }
}
