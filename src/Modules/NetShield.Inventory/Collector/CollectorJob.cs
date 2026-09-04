using NetShield.Contracts.Collector;

namespace NetShield.Inventory.Collector;

/// <summary>
/// One unit of work the API has scheduled for <c>netshield-collector</c>.
/// </summary>
/// <remarks>
/// <para>
/// Internal, like every other entity in this module (ARCHITECTURE.md §4). What crosses to the
/// collector is <c>CollectorJobLease</c>, which is a different shape for a different reason: the
/// lease carries an opened credential and the row never does.
/// </para>
/// <para>
/// There is no <c>deleted_at</c>. CONVENTIONS.md §3 puts soft delete on inventory tables, and a
/// job is not inventory — it is a record of work, kept for as long as the retention policy says
/// and then removed by it. Nothing in the system asks for a job that was cancelled, either;
/// there is no cancellation path in V1 and so no state for one.
/// </para>
/// <para>
/// The lease is the whole of the concurrency model. A collector claims a job by writing its own
/// token and an expiry onto the row; a result is accepted only under the token that is currently
/// on it. That is what makes a duplicate submission a no-op and a late submission from a lease
/// that has already expired a refusal rather than an overwrite of whoever holds it now.
/// </para>
/// </remarks>
internal sealed class CollectorJob
{
    /// <summary>UUID v7, so the primary key is also the order jobs were queued in.</summary>
    public Guid Id { get; init; }

    /// <summary>What the collector is being asked to do.</summary>
    public CollectorJobKind Kind { get; init; }

    /// <summary>Where the job is in its life.</summary>
    public CollectorJobStatus Status { get; set; }

    /// <summary>
    /// The device the job is about, when it is about one. A discovery sweep over a range is not.
    /// </summary>
    public Guid? DeviceId { get; init; }

    /// <summary>
    /// Which credential the job is to be run with, chosen when the job was queued.
    /// </summary>
    /// <remarks>
    /// The job names the profile; the lease opens exactly that one and no other. Choosing between
    /// a device's several credentials is scheduling policy — WP-1.6's — and a lease that guessed
    /// would have to decrypt more than it needed in order to guess.
    /// </remarks>
    public Guid? CredentialProfileId { get; init; }

    /// <summary>
    /// Job-specific arguments as JSON, or <see langword="null"/>. Opaque here: WP-1.3 defines no
    /// job kind's parameters, and a shape invented before the first kind exists would be a shape
    /// the first kind has to work around.
    /// </summary>
    public string? Parameters { get; init; }

    /// <summary>The earliest the job may be leased. UTC.</summary>
    public DateTimeOffset DueAt { get; set; }

    /// <summary>How many times the job has been leased.</summary>
    public int Attempts { get; set; }

    /// <summary>How many leases it gets before it is abandoned as failed.</summary>
    public int MaxAttempts { get; init; }

    /// <summary>
    /// The token identifying the current lease generation, or <see langword="null"/> if the job
    /// has never been leased. It is kept after completion, so that a repeated submission under
    /// the same token is recognisable as the duplicate it is.
    /// </summary>
    public string? LeaseToken { get; set; }

    /// <summary>The name the collector holding the lease called itself.</summary>
    public string? LeasedBy { get; set; }

    /// <summary>When the lease expires and the job becomes claimable again. UTC.</summary>
    public DateTimeOffset? LeasedUntil { get; set; }

    /// <summary>When a result was recorded. UTC.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>What the collector reported, once it has reported.</summary>
    public CollectorJobOutcome? Outcome { get; set; }

    /// <summary>
    /// A sentence about how it ended, for a person reading the queue. It is redacted on the way
    /// in: a failure detail is written by the collector and is not trusted to be free of a
    /// credential (SPEC.md §5).
    /// </summary>
    public string? Detail { get; set; }

    /// <summary>
    /// What the collector found, as JSON, or <see langword="null"/>. WP-1.3 stores it and reads
    /// none of it; the packages that own each kind interpret it.
    /// </summary>
    public string? Result { get; set; }

    /// <summary>When the job was queued. UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the row last changed. UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Whether a result has already been recorded for this job.</summary>
    public bool IsComplete =>
        Status is CollectorJobStatus.Succeeded or CollectorJobStatus.Failed;
}
