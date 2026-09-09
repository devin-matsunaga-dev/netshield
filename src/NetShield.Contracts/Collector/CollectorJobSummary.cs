namespace NetShield.Contracts.Collector;

/// <summary>
/// One row of a device's collector queue, as a person reading it needs it.
/// </summary>
/// <remarks>
/// <para>
/// This is the first time <c>collector_jobs</c> has been readable outside
/// <c>/internal/collector/*</c>. It exists because "I pressed walk — did anything happen?" had no
/// answer in the product: the job was queued, the screen said <c>202</c>, and whether a collector
/// ever claimed it was visible only in the database (WP-2.5).
/// </para>
/// <para>
/// <strong>Three things on the row are deliberately not here.</strong>
/// <c>CredentialProfileId</c>, because this endpoint is gated on reading inventory and which
/// credential a job runs under is a statement about which accounts NetShield holds passwords for
/// — the thing WP-1.2 gated behind <c>CredentialsManage</c>. <c>LeaseToken</c>, because it is the
/// capability that lets a caller submit a result for the job. And <c>Result</c>, because it is
/// an unbounded blob whose shape belongs to whichever package owns that job kind, and a queue
/// screen has no use for it.
/// </para>
/// <para>
/// <see cref="Detail"/> is here and is already redacted at the point it is stored: a failure
/// sentence is written by the collector and is not trusted to be free of a credential
/// (<c>SPEC.md</c> §5).
/// </para>
/// <para>
/// <strong>There is no <c>Outcome</c> either, for two reasons that agree.</strong> It carries
/// nothing <see cref="Status"/> does not — completion sets the status from the outcome and
/// nothing else can produce <c>Succeeded</c> or <c>Failed</c> — while the status also
/// distinguishes the three states a job can be in before it has one. And a nullable enum on a
/// response contract is the defect WP-1.8 recorded, WP-1.9 hit again and WP-2.1 worked around:
/// ASP.NET appends <c>null</c> to the <em>shared</em> enum schema, so every other shape using
/// that type reads as nullable in the generated client. <c>CollectorJobOutcome</c> reaches the
/// document through this shape alone today, which makes it harmless today and a trap for the
/// next shape that uses it.
/// </para>
/// </remarks>
/// <param name="Id">The job.</param>
/// <param name="Kind">What the collector was asked to do.</param>
/// <param name="Walk">
/// Which read a <see cref="CollectorJobKind.Discover"/> job is — <c>snmp</c>, <c>clients</c>,
/// <c>neighbors</c>, <c>routes</c> or <c>vlans</c>. It is the discriminator the job's parameters
/// carry rather than a column, because the kinds are the collector contract and the walks are
/// not; <see langword="null"/> for a job whose kind has no walks.
/// </param>
/// <param name="Status">Where it is in its life.</param>
/// <param name="Attempts">How many times a collector has leased it.</param>
/// <param name="MaxAttempts">How many leases it gets before it is abandoned as failed.</param>
/// <param name="DueAt">The earliest it may be leased. UTC.</param>
/// <param name="LeasedBy">The collector holding it, or the last one that did.</param>
/// <param name="LeasedUntil">When that lease expires and the job becomes claimable again. UTC.</param>
/// <param name="CompletedAt">When a result was recorded. UTC.</param>
/// <param name="Detail">A sentence about how it ended, for a person reading the queue.</param>
/// <param name="Cancellable">
/// Whether this job can still be taken out of the queue — true only while it is
/// <see cref="CollectorJobStatus.Pending"/>. Computed on the server so that the screen and the
/// endpoint cannot come to different answers about which rows offer the control.
/// </param>
/// <param name="CreatedAt">When it was queued. UTC.</param>
public sealed record CollectorJobSummary(
    Guid Id,
    CollectorJobKind Kind,
    string? Walk,
    CollectorJobStatus Status,
    int Attempts,
    int MaxAttempts,
    DateTimeOffset DueAt,
    string? LeasedBy,
    DateTimeOffset? LeasedUntil,
    DateTimeOffset? CompletedAt,
    string? Detail,
    bool Cancellable,
    DateTimeOffset CreatedAt);
