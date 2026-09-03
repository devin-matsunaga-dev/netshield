namespace NetShield.Platform.Time;

/// <summary>
/// The real clock, over <see cref="TimeProvider"/> so that a test can substitute the framework's
/// own fake without NetShield inventing one.
/// </summary>
public sealed class SystemClock(TimeProvider timeProvider) : IClock
{
    /// <inheritdoc />
    /// <remarks>
    /// Normalised rather than trusted. <see cref="TimeProvider.GetUtcNow"/> is documented to
    /// return UTC, but a substituted provider is the one place that promise can be broken, and
    /// a local time reaching a <c>timestamptz</c> column is the sort of bug that is only found
    /// months later in a report.
    /// </remarks>
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow().ToUniversalTime();
}
