using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NetShield.Contracts.Collector;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Collector;
using NetShield.Inventory.Persistence;

using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Reachability;

/// <summary>
/// One pass of the reachability schedule: find the devices whose next probe has fallen due, queue
/// one for each, and record when the next is expected.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the collector queue's first producer.</strong> Until this package, nothing in
/// the running system put a row in <c>collector_jobs</c> — a fresh <c>aspire run</c> showed a
/// collector heartbeating against an empty queue for ever.
/// </para>
/// <para>
/// Separated from <see cref="ReachabilityScheduler"/> so that a pass can be driven by a test
/// instead of by a timer, which is the split <c>OutboxProcessor</c> and <c>OutboxDispatcher</c>
/// already use and for the same reason.
/// </para>
/// <para>
/// A device with an unfinished probe is skipped. Without that, a collector that stopped
/// answering would leave one job per device per interval accumulating for as long as the outage
/// lasted — five hundred devices on a sixty-second interval is half a million rows a day of work
/// nobody will ever do. Skipping bounds the queue at one outstanding probe per device, and the
/// lease's own visibility timeout is what recovers a job whose collector died holding it.
/// </para>
/// </remarks>
internal sealed class ReachabilitySchedulePass(
    InventoryDbContext context,
    ICollectorJobQueue queue,
    IOptions<ReachabilityOptions> options,
    IClock clock,
    ILogger<ReachabilitySchedulePass> logger)
{
    /// <summary>
    /// Queues a probe for every device that is due, up to the configured ceiling.
    /// </summary>
    /// <returns>How many probes were queued.</returns>
    public async Task<int> ScheduleDueAsync(CancellationToken cancellationToken)
    {
        ReachabilityOptions settings = options.Value;

        if (!settings.Enabled)
        {
            return 0;
        }

        DateTimeOffset now = clock.UtcNow;

        IReadOnlyList<DueDevice> due = await FindDueAsync(settings, now, cancellationToken);

        if (due.Count == 0)
        {
            return 0;
        }

        // Serialised once for the whole pass. The parameters are the same for every device —
        // they are settings about how to probe, not about which device is being probed.
        JsonElement parameters = ProbeParameters(settings);

        int queued = 0;

        foreach (DueDevice device in due)
        {
            Result<Guid> job = await queue.EnlistAsync(
                context,
                new NewCollectorJob(
                    CollectorJobKind.Poll,
                    device.DeviceId,

                    // No credential. ICMP authenticates to nothing, and a job that named a
                    // profile would have the lease open one for no reason and write an audit row
                    // saying a credential was released when none was needed.
                    CredentialProfileId: null,
                    parameters,
                    DueAt: now),
                cancellationToken);

            if (!job.IsSuccess)
            {
                // The device was live when the query ran and is not now. Nothing to do but
                // leave it: the next pass will not see it either, because the query reads live
                // devices only.
                logger.LogInformation(
                    "A reachability probe for device {DeviceId} was not queued: {Reason}",
                    device.DeviceId,
                    job.Error.Message);

                continue;
            }

            Reschedule(device, settings, now);
            queued++;
        }

        // One save for the pass. Every queued job and every next-probe stamp commits together,
        // so a failure here leaves neither — a device is not left marked as probed with nothing
        // queued to probe it.
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Queued {Count} reachability probes", queued);

        return queued;
    }

    /// <summary>
    /// The live devices that have never been considered, or whose next probe is due, and which
    /// have no probe already outstanding.
    /// </summary>
    /// <remarks>
    /// A device with no reachability row has never been scheduled, so it sorts ahead of every
    /// device that has: nothing at all is known about it, which makes it the most urgent thing
    /// in the estate to ask about. The reachability rows come back tracked, because this pass is
    /// about to change them and the change has to commit with the jobs it queues.
    /// </remarks>
    private async Task<IReadOnlyList<DueDevice>> FindDueAsync(
        ReachabilityOptions settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var candidates = await (
            from device in context.Devices
            join reachability in context.DeviceReachabilities
                on device.Id equals reachability.DeviceId into matched
            from reachability in matched.DefaultIfEmpty()
            where device.DeletedAt == null
                && (reachability == null || reachability.NextProbeAt <= now)

                // Bounds the queue at one outstanding probe per device, so a collector outage
                // cannot leave a backlog of work nobody will ever do.
                && !context.CollectorJobs.Any(job =>
                    job.DeviceId == device.Id
                    && (job.Status == CollectorJobStatus.Pending || job.Status == CollectorJobStatus.Leased))
            orderby reachability == null ? DateTimeOffset.MinValue : reachability.NextProbeAt, device.Id
            select new { DeviceId = device.Id, Reachability = reachability })
            .Take(settings.MaxJobsPerScan)
            .ToListAsync(cancellationToken);

        List<DueDevice> due = new(candidates.Count);

        foreach (var candidate in candidates)
        {
            due.Add(new DueDevice(
                candidate.DeviceId,
                candidate.Reachability ?? Create(candidate.DeviceId, now)));
        }

        return due;
    }

    private DeviceReachability Create(Guid deviceId, DateTimeOffset now)
    {
        DeviceReachability row = new()
        {
            Id = Guid.CreateVersion7(now),
            DeviceId = deviceId,
            PendingState = DeviceState.Unknown,
            PendingObservations = 0,
            NextProbeAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.DeviceReachabilities.Add(row);

        return row;
    }

    /// <summary>Moves a device's next probe one interval out, with a small deterministic spread.</summary>
    /// <remarks>
    /// The spread is what stops five hundred devices imported in one discovery run from falling
    /// due in the same second for ever afterwards. It is derived from the device's own id rather
    /// than from a random source, so a device's offset is stable across restarts and a scheduler
    /// that ran twice cannot produce two different answers for the same device — and it is
    /// bounded by the scan interval, so it costs a device at most one scan of delay rather than
    /// a share of its polling interval.
    /// </remarks>
    private static void Reschedule(DueDevice device, ReachabilityOptions settings, DateTimeOffset now)
    {
        int spread = device.DeviceId.ToByteArray()[^1] % settings.ScanIntervalSeconds;

        device.Reachability.NextProbeAt = now.AddSeconds(settings.PollIntervalSeconds + spread);
        device.Reachability.UpdatedAt = now;
    }

    private static JsonElement ProbeParameters(ReachabilityOptions settings)
    {
        using JsonDocument document = JsonSerializer.SerializeToDocument(
            IcmpProbeParameters.From(settings),
            ReachabilitySerializerContext.Default.IcmpProbeParameters);

        return document.RootElement.Clone();
    }

    /// <summary>A device that is due, and the row recording when it will be due again.</summary>
    private sealed record DueDevice(Guid DeviceId, DeviceReachability Reachability);
}
