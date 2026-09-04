using System.Text.Json;

using NetShield.Contracts.Collector;

using NetShield.Inventory.Persistence;

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
/// Two forms. <see cref="EnqueueAsync"/> saves, which is what a caller with nothing else to
/// write wants. <see cref="EnlistAsync"/> stages the row on a context the caller is already
/// changing and leaves the save to them, the way <c>OutboxEnlistment</c> does for an event —
/// WP-1.3 left this to the first package that needed it, "because it will know which context it
/// is enlisting on", and WP-1.4's scheduler is that package: queueing a probe and stamping when
/// the next one is due have to commit together or the device is either probed twice or never
/// again.
/// </para>
/// </remarks>
internal interface ICollectorJobQueue
{
    /// <summary>Queues one job and saves it.</summary>
    /// <returns>
    /// The job's id, or a refusal when it names a device or a credential profile that is not
    /// live — a job pointing at something that has been removed can only ever fail, and failing
    /// at the enqueue names the caller that made the mistake.
    /// </returns>
    Task<Result<Guid>> EnqueueAsync(NewCollectorJob job, CancellationToken cancellationToken);

    /// <summary>
    /// Stages one job on <paramref name="context"/> without saving, so it commits with whatever
    /// else the caller is writing.
    /// </summary>
    /// <remarks>
    /// The context is a parameter rather than the one this service was resolved with, so that
    /// "these are one transaction" is visible at the call site instead of being a property of how
    /// two services happened to be scoped.
    /// </remarks>
    /// <returns>The id the job will have, or the same refusals <see cref="EnqueueAsync"/> makes.</returns>
    Task<Result<Guid>> EnlistAsync(
        InventoryDbContext context,
        NewCollectorJob job,
        CancellationToken cancellationToken);
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
