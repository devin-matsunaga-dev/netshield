using System.Text.Json;

using NetShield.Contracts.Collector;

using NetShield.Platform.Results;

namespace NetShield.Inventory.Collector;

/// <summary>
/// How work reaches the collector queue.
/// </summary>
/// <remarks>
/// <para>
/// Internal, and there is deliberately no HTTP route behind it. Nothing schedules work in
/// WP-1.3: the reachability schedule is WP-1.4's, the discovery schedule and its on-demand run
/// are WP-1.6's, and the polling schedule is Phase 3's. This is the seam each of them enqueues
/// through, so that the shape of a queued job is settled once, here, with the lease model that
/// reads it.
/// </para>
/// <para>
/// It saves. A caller that needs a job queued in the same transaction as a domain change will
/// need an enlisting form of this, the way <c>OutboxEnlistment</c> is the enlisting form of
/// <c>IEventBus</c> — the first package that actually needs it is the one that should add it,
/// because it will know which context it is enlisting on.
/// </para>
/// </remarks>
internal interface ICollectorJobQueue
{
    /// <summary>Queues one job.</summary>
    /// <returns>
    /// The job's id, or a refusal when it names a device or a credential profile that is not
    /// live — a job pointing at something that has been removed can only ever fail, and failing
    /// at the enqueue names the caller that made the mistake.
    /// </returns>
    Task<Result<Guid>> EnqueueAsync(NewCollectorJob job, CancellationToken cancellationToken);
}

/// <summary>A job to queue.</summary>
/// <param name="Kind">What the collector is being asked to do.</param>
/// <param name="DeviceId">The device it is about, when it is about one.</param>
/// <param name="CredentialProfileId">
/// Which credential to run it with. The lease opens exactly this profile: choosing between a
/// device's several is scheduling policy and belongs to whoever is queueing the job.
/// </param>
/// <param name="Parameters">Kind-specific arguments as JSON.</param>
/// <param name="DueAt">The earliest it may be leased. Defaults to now.</param>
internal sealed record NewCollectorJob(
    CollectorJobKind Kind,
    Guid? DeviceId = null,
    Guid? CredentialProfileId = null,
    JsonElement? Parameters = null,
    DateTimeOffset? DueAt = null);
