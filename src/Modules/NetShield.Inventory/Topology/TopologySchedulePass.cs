using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NetShield.Contracts.Collector;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Collector;
using NetShield.Inventory.Discovery;
using NetShield.Inventory.Persistence;
using NetShield.Inventory.Topology.Handlers;

using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Topology;

/// <summary>
/// One pass of the topology schedule: find the devices whose neighbour tables or routing tables
/// are due to be read, queue one walk for each, and record when the next is expected.
/// </summary>
/// <remarks>
/// <para>
/// The fourth producer of collector work, after WP-1.4's reachability schedule, WP-1.6's
/// discovery schedule and WP-1.8's client schedule, and shaped like all three: separated from
/// <see cref="TopologyScheduler"/> so a pass can be driven by a test rather than by a timer,
/// bounded by <c>TopologyOptions.MaxJobsPerScan</c>, and skipping any device that already has a
/// <c>Discover</c> outstanding so that a collector outage cannot build a backlog nobody will run.
/// </para>
/// <para>
/// <strong>Two walks, one pass, one job per device at a time.</strong> A device may be due for
/// both reads at once, and only one is queued — the outstanding-walk rule refuses the second
/// anyway, and queueing it to be refused would burn a lease slot to learn nothing. The neighbour
/// walk goes first when both are due, because it is the primary source for adjacency and the
/// routing read is a supplement to it; the route walk's due time is left where it is, so it wins
/// the next pass.
/// </para>
/// <para>
/// <strong>Only devices with an SNMP credential are scheduled.</strong> Both reads need one, and
/// a device without would produce one failed job per interval for ever, each recording the same
/// sentence on the scan row. It is skipped instead, and the absence shows on the device rather
/// than as a queue full of failures — the rule WP-1.8's client schedule settled.
/// </para>
/// </remarks>
internal sealed class TopologySchedulePass(
    InventoryDbContext context,
    ICollectorJobQueue queue,
    SnmpCredentialSelector credentials,
    IOptions<TopologyOptions> options,
    IClock clock,
    ILogger<TopologySchedulePass> logger)
{
    /// <summary>Queues a topology walk for every device that is due, up to the configured ceiling.</summary>
    /// <returns>How many walks were queued.</returns>
    public async Task<int> ScheduleDueAsync(CancellationToken cancellationToken)
    {
        TopologyOptions settings = options.Value;

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

        // Serialised once for the whole pass: the parameters say how to read, not which device is
        // being read.
        JsonElement neighbors = QueueTopologyWalkHandler.Parameters(
            settings,
            TopologyWalkKind.Neighbors);

        JsonElement routes = QueueTopologyWalkHandler.Parameters(settings, TopologyWalkKind.Routes);

        int queued = 0;

        foreach (DueDevice device in due)
        {
            Guid? profileId = await credentials.ChooseAsync(device.DeviceId, cancellationToken);

            if (profileId is not { } chosen)
            {
                // Both due times are pushed forward anyway, for the reason a discovery seed's
                // next run is: the pass would otherwise find this device, refuse it and log the
                // same sentence on every scan for as long as it had no credential.
                Reschedule(device.Scan, TopologyWalkKind.Neighbors, settings, now);
                Reschedule(device.Scan, TopologyWalkKind.Routes, settings, now);

                continue;
            }

            TopologyWalkKind walk = device.Walk;

            Result<Guid> job = await queue.EnlistAsync(
                context,
                new NewCollectorJob(
                    CollectorJobKind.Discover,
                    device.DeviceId,
                    chosen,
                    walk == TopologyWalkKind.Routes ? routes : neighbors,
                    DueAt: now),
                cancellationToken);

            if (!job.IsSuccess)
            {
                logger.LogInformation(
                    "A {Walk} topology walk for device {DeviceId} was not queued: {Reason}",
                    walk,
                    device.DeviceId,
                    job.Error.Message);

                continue;
            }

            Reschedule(device.Scan, walk, settings, now);
            queued++;
        }

        // One save for the pass, so every queued job and every next-walk stamp commits together.
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Queued {Count} topology walks", queued);

        return queued;
    }

    /// <summary>
    /// The live devices with a walk due and no <c>Discover</c> outstanding, and which walk it is.
    /// </summary>
    /// <remarks>
    /// The outstanding check covers every <c>Discover</c> naming the device rather than only a
    /// topology walk, which is deliberately stricter than it needs to be: a fingerprint walk, a
    /// client walk and a neighbour walk all open an SNMP session to the same agent with the same
    /// credential, and running them at once is several conversations a small device has no reason
    /// to be asked to hold. It is the rule WP-1.8 settled, applied from a fourth side.
    /// </remarks>
    private async Task<IReadOnlyList<DueDevice>> FindDueAsync(
        TopologyOptions settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var candidates = await (
            from device in context.Devices
            join scan in context.DeviceTopologyScans on device.Id equals scan.DeviceId into matched
            from scan in matched.DefaultIfEmpty()
            where device.DeletedAt == null
                && (scan == null || scan.NextNeighborWalkAt <= now || scan.NextRouteWalkAt <= now)
                && !context.CollectorJobs.Any(job =>
                    job.DeviceId == device.Id
                    && job.Kind == CollectorJobKind.Discover
                    && (job.Status == CollectorJobStatus.Pending
                        || job.Status == CollectorJobStatus.Leased))
            orderby scan == null ? DateTimeOffset.MinValue : scan.NextNeighborWalkAt, device.Id
            select new { DeviceId = device.Id, Scan = scan })
            .Take(settings.MaxJobsPerScan)
            .ToListAsync(cancellationToken);

        List<DueDevice> due = new(candidates.Count);

        foreach (var candidate in candidates)
        {
            DeviceTopologyScan scan = candidate.Scan ?? Create(candidate.DeviceId, now);

            // The neighbour walk wins a tie, because it is what establishes adjacency; the route
            // walk's due time is untouched and it takes the next pass.
            TopologyWalkKind walk = scan.NextNeighborWalkAt <= now
                ? TopologyWalkKind.Neighbors
                : TopologyWalkKind.Routes;

            due.Add(new DueDevice(candidate.DeviceId, scan, walk));
        }

        return due;
    }

    private DeviceTopologyScan Create(Guid deviceId, DateTimeOffset now)
    {
        DeviceTopologyScan row = new()
        {
            Id = Guid.CreateVersion7(now),
            DeviceId = deviceId,
            NextNeighborWalkAt = now,
            NextRouteWalkAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.DeviceTopologyScans.Add(row);

        return row;
    }

    /// <summary>Moves one walk's next due time out by its interval, with a small deterministic spread.</summary>
    /// <remarks>
    /// The spread is what stops an estate imported in one discovery run from falling due in the
    /// same second for ever afterwards, and it is derived from the device's own id rather than
    /// from a random source so that a device's offset survives a restart — exactly as
    /// <c>ReachabilitySchedulePass</c> and <c>ClientSchedulePass</c> do it.
    /// </remarks>
    private static void Reschedule(
        DeviceTopologyScan scan,
        TopologyWalkKind walk,
        TopologyOptions settings,
        DateTimeOffset now)
    {
        int spread = scan.DeviceId.ToByteArray()[^1] % settings.ScanIntervalSeconds;

        if (walk == TopologyWalkKind.Routes)
        {
            scan.NextRouteWalkAt = now.AddSeconds(settings.RouteWalkIntervalSeconds + spread);
        }
        else
        {
            scan.NextNeighborWalkAt = now.AddSeconds(settings.NeighborWalkIntervalSeconds + spread);
        }

        scan.UpdatedAt = now;
    }

    /// <summary>A device that is due, which read it is due for, and the row recording both.</summary>
    private sealed record DueDevice(Guid DeviceId, DeviceTopologyScan Scan, TopologyWalkKind Walk);
}
