namespace NetShield.Inventory.Discovery;

/// <summary>
/// One sweep job of one run: which span it probes, and whether its result has been applied.
/// </summary>
/// <remarks>
/// <para>
/// This is how a job in <c>collector_jobs</c> is known to belong to a run. The alternative — a
/// run id inside the job's <c>parameters</c> document — would mean asking PostgreSQL to search
/// JSON to answer "is this run finished", which is the question the completion rule is built on.
/// </para>
/// <para>
/// <strong><see cref="AppliedAt"/> is the idempotency guard.</strong> Outbox delivery is
/// at-least-once, and applying one sweep result twice would double every counter on the run and
/// re-record every host it saw. A redelivery finds this stamped and stops, the same shape
/// <c>device_reachability</c> and <c>device_fingerprints</c> use.
/// </para>
/// </remarks>
internal sealed class DiscoveryRunJob
{
    /// <summary>UUID v7.</summary>
    public Guid Id { get; init; }

    /// <summary>The run this job belongs to.</summary>
    public Guid RunId { get; init; }

    /// <summary>The queued job. Unique — a job belongs to at most one run.</summary>
    public Guid CollectorJobId { get; init; }

    /// <summary>Which chunk of the run this is, counting from one.</summary>
    /// <remarks>
    /// A run's jobs are created in one loop and stamped with one timestamp, so their UUID v7 keys
    /// share a millisecond and are ordered by their random half rather than by the span they
    /// cover. This is the order a reader means when they ask which part of the range failed.
    /// </remarks>
    public int Sequence { get; init; }

    /// <summary>The first address of the span this job probes.</summary>
    public string FirstAddress { get; init; } = string.Empty;

    /// <summary>The last address of the span, inclusive.</summary>
    public string LastAddress { get; init; } = string.Empty;

    /// <summary>How many addresses that span holds, before the collector applies exclusions.</summary>
    public int AddressCount { get; init; }

    /// <summary>When this job's result was applied to the run, or null while it is outstanding.</summary>
    public DateTimeOffset? AppliedAt { get; set; }

    /// <summary>Whether the job succeeded, once it has been applied.</summary>
    public bool? Succeeded { get; set; }

    /// <summary>When the row was created. UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the row last changed. UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
