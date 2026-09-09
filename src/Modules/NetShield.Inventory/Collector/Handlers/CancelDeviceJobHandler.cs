using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NetShield.Contracts.Collector;
using NetShield.Contracts.Identity;

using NetShield.Inventory.Devices;
using NetShield.Inventory.Devices.Handlers;
using NetShield.Inventory.Persistence;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Collector.Handlers;

/// <summary>
/// Takes one queued job back out of the queue.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the seam WP-1.3 named and deliberately did not cut.</strong> That package
/// wrote "nothing cancels a job in V1" onto <see cref="CollectorJobStatus"/> and left the shape
/// of the answer described in <c>STATUS.md</c>; WP-2.5 cuts it at the human's explicit
/// instruction. Nothing about the collector's own contract moves: the lease, result and
/// heartbeat endpoints are untouched, and a cancelled job is simply a row the claim query — which
/// selects <c>Pending</c>, or <c>Leased</c> with an expired lease — stops returning.
/// </para>
/// <para>
/// <strong>Only from <c>Pending</c>.</strong> A leased job is being run right now by a collector
/// that has no idea anybody changed their mind. Cancelling it would leave that collector to post
/// a result the API then refuses, which is a worse end than letting a read that is already open
/// finish and be recorded. Revoking a lease is a change to the collector contract rather than to
/// this handler, and nothing asks for one yet.
/// </para>
/// <para>
/// <strong>Cancelled, not deleted.</strong> <c>collector_jobs</c> is the record of what NetShield
/// asked for and what came back. An operator asking "why was this device never walked" is owed
/// the answer that somebody withdrew it, which a deleted row cannot give — and a delete would
/// also race a collector mid-lease, which the status transition cannot.
/// </para>
/// </remarks>
internal sealed class CancelDeviceJobHandler(
    InventoryDbContext context,
    IResourceGuard guard,
    IAuditContext audit,
    IClock clock,
    ILogger<CancelDeviceJobHandler> logger)
{
    public async Task<Result<CollectorJobSummary>> HandleAsync(
        Guid deviceId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        // DiscoveryRun, not InventoryWrite: this stops NetShield reading a device outside its
        // schedule, which is the same authority as starting one and a different privilege from
        // editing that device's notes. No RBAC table changes — Administrator and Operator hold it.
        Result permitted = guard.Require(
            Permission.DiscoveryRun,
            GetDeviceListHandler.ResourceType,
            deviceId.ToString());

        if (!permitted.IsSuccess)
        {
            return Result<CollectorJobSummary>.Failure(permitted.Error);
        }

        bool exists = await context.Devices.AsNoTracking()
            .AnyAsync(device => device.Id == deviceId && device.DeletedAt == null, cancellationToken);

        if (!exists)
        {
            return DeviceErrors.NotFound(deviceId);
        }

        // Read first, so that "no such job" and "that job is already running" are told apart
        // before anything is written.
        CollectorJob? job = await context.CollectorJobs.AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == jobId && candidate.DeviceId == deviceId,
                cancellationToken);

        if (job is null)
        {
            return CollectorJobErrors.NotFound(deviceId, jobId);
        }

        if (job.Status != CollectorJobStatus.Pending)
        {
            return CollectorJobErrors.NotCancellable(jobId, job.Status);
        }

        DateTimeOffset now = clock.UtcNow;

        // The write carries the same condition the read checked, so a collector that claims the
        // job in between loses nothing: the update matches no row and the caller is told the job
        // is already running. `collector_jobs` has no concurrency token — the lease *is* the
        // concurrency model (WP-1.3) — so the guard has to be in the predicate rather than in a
        // version check, and `updated_at` is set here because this bypasses SaveChanges.
        int changed = await context.CollectorJobs
            .Where(candidate => candidate.Id == jobId
                && candidate.DeviceId == deviceId
                && candidate.Status == CollectorJobStatus.Pending)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(candidate => candidate.Status, CollectorJobStatus.Cancelled)
                    .SetProperty(candidate => candidate.UpdatedAt, now),
                cancellationToken);

        if (changed == 0)
        {
            // Claimed between the read and the write. Re-read rather than guess, so the message
            // names the state the job is actually in.
            CollectorJob? current = await context.CollectorJobs.AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Id == jobId, cancellationToken);

            return current is null
                ? CollectorJobErrors.NotFound(deviceId, jobId)
                : CollectorJobErrors.NotCancellable(jobId, current.Status);
        }

        logger.LogInformation(
            "Collector job {JobId} for device {DeviceId} was cancelled before it was leased",
            jobId,
            deviceId);

        audit.Target(GetDeviceListHandler.ResourceType, deviceId.ToString());

        // The row as it now is, rather than as it was read. `job` came back untracked, so
        // moving it forward here changes nothing in the database — it is the projection that has
        // to agree with what was just written.
        job.Status = CollectorJobStatus.Cancelled;
        job.UpdatedAt = now;

        return GetDeviceJobListHandler.ToSummary(job);
    }
}
