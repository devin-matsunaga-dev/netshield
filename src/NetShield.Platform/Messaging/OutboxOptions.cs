using System.ComponentModel.DataAnnotations;

namespace NetShield.Platform.Messaging;

/// <summary>
/// How the outbox dispatcher behaves. Bound from the <c>Outbox</c> configuration section.
/// </summary>
public sealed class OutboxOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "Outbox";

    /// <summary>How long the dispatcher waits after an empty pass before looking again.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The longest the dispatcher waits between passes while the database is unreachable. A
    /// failing pass backs off up to this, so an outage produces a handful of log lines rather
    /// than one every second.
    /// </summary>
    public TimeSpan MaxPollInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>How many pending rows one pass claims.</summary>
    [Range(1, 1000)]
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// How many times a row is retried before it parks. A parked row stays in the table,
    /// unprocessed, with its last error recorded — visible to an operator, invisible to the
    /// dispatcher.
    /// </summary>
    [Range(1, 100)]
    public int MaxAttempts { get; set; } = 5;
}
