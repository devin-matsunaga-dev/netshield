namespace NetShield.Platform.Messaging;

/// <summary>
/// One event waiting to be delivered, or one that already was. Written in the same transaction
/// as the domain change it describes (ARCHITECTURE.md §5).
/// </summary>
/// <remarks>
/// Rows are kept after delivery rather than deleted. They are the record of what the system
/// told itself, they make a redelivery investigation possible, and pruning them is a retention
/// concern like any other — not something the dispatcher should be doing on the hot path.
/// </remarks>
public sealed class OutboxMessage
{
    /// <summary>UUID v7, so the primary key is also the insertion order (CONVENTIONS.md §3).</summary>
    public Guid Id { get; init; }

    /// <summary>The registered name of the event type, per <see cref="IntegrationEventRegistry"/>.</summary>
    public required string EventType { get; init; }

    /// <summary>The serialised event, stored as <c>jsonb</c>.</summary>
    public required string Payload { get; init; }

    /// <summary>When the publishing transaction created the row. UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the row last changed — an attempt, or delivery. UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>When every handler completed. <see langword="null"/> while the row is pending.</summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>
    /// How many times delivery has been attempted. A row that reaches the configured maximum is
    /// left alone rather than discarded, so a poison message parks visibly instead of vanishing.
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// Why the last attempt failed, redacted before it is stored — SPEC.md §5 covers the
    /// database as well as the log.
    /// </summary>
    public string? Error { get; set; }
}
