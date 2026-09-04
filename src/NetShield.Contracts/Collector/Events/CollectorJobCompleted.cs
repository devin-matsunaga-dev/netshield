using NetShield.Contracts.Messaging;

namespace NetShield.Contracts.Collector.Events;

/// <summary>
/// A collector reported on a job, and the API recorded the report.
/// </summary>
/// <remarks>
/// <para>
/// This is the seam every later collection package hangs from: WP-1.4 reads the reachability
/// results, WP-1.5 the discovery walks, Phase 7 the configuration fetches. WP-1.3 stores a
/// result and interprets none of them, so the event says a job of a kind finished and how, and
/// nothing about what was found.
/// </para>
/// <para>
/// It carries identifiers only. A subscriber that needs the payload reads the job row; putting
/// the payload on the event would put a device's configuration, or its interface table, into a
/// column that every module can read (ARCHITECTURE.md §4).
/// </para>
/// </remarks>
/// <param name="JobId">The job that was reported on.</param>
/// <param name="Kind">What the job was for.</param>
/// <param name="DeviceId">The device it named, when it named one.</param>
/// <param name="Outcome">Whether the collector managed to do the work.</param>
/// <param name="CompletedAt">When the API recorded the report. UTC.</param>
public sealed record CollectorJobCompleted(
    Guid JobId,
    CollectorJobKind Kind,
    Guid? DeviceId,
    CollectorJobOutcome Outcome,
    DateTimeOffset CompletedAt) : IIntegrationEvent;
