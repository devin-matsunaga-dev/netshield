using Microsoft.Extensions.Logging;

namespace NetShield.Platform.Logging;

/// <summary>
/// Wraps one logger so that nothing reaches a provider with a secret still in it. Every
/// provider sits behind this — console, OpenTelemetry, and anything a later package adds —
/// because the wrapping happens at the factory rather than at a provider (ARCHITECTURE.md §8).
/// </summary>
/// <remarks>
/// The boundary is the log line: state, scope and message are redacted. An exception passed
/// alongside them is forwarded as it was, because rewriting an exception would change the stack
/// trace an operator needs. Anything that puts a credential into an exception message is a bug
/// at the throw site, not something a sink can repair.
/// </remarks>
internal sealed class RedactingLogger(ILogger inner, SecretRedactor redactor) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull =>
        state is IReadOnlyList<KeyValuePair<string, object?>> values
        && RedactedLogValues.TryRedact(values, redactor, out RedactedLogValues? redacted)
            ? inner.BeginScope(redacted)
            : inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (state is IReadOnlyList<KeyValuePair<string, object?>> values
            && RedactedLogValues.TryRedact(values, redactor, out RedactedLogValues? redacted))
        {
            inner.Log(logLevel, eventId, redacted, exception, static (safe, _) => safe.ToString());
            return;
        }

        // A state that is not a structured property list still gets its rendered text scanned,
        // which is what covers a caller who interpolated a value into the message itself.
        string message = formatter(state, exception);
        string safeMessage = redactor.RedactText(message);

        if (!ReferenceEquals(safeMessage, message))
        {
            inner.Log(logLevel, eventId, safeMessage, exception, static (text, _) => text);
            return;
        }

        inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
