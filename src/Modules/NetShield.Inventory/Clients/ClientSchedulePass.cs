using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NetShield.Contracts.Collector;

using NetShield.Inventory.Collector;
using NetShield.Inventory.Discovery;
using NetShield.Inventory.Persistence;

using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Clients;

/// <summary>
/// One pass of the client schedule: find the devices whose tables are due to be read, queue a
/// walk for each, and record when the next is expected.
/// </summary>
/// <remarks>
/// <para>
/// The third producer of collector work, after WP-1.4's reachability schedule and WP-1.6's
/// discovery schedule, and shaped like both: separated from <see cref="ClientScheduler"/> so a
/// pass can be driven by a test rather than by a timer, bounded by
/// <c>ClientOptions.MaxJobsPerScan</c>, and skipping any device that already has a walk
/// outstanding so that a collector outage cannot build a backlog nobody will run.
/// </para>
/// <para>
/// <strong>Only devices with an SNMP credential are scheduled.</strong> Unlike a reachability
/// probe, which authenticates to nothing, this reads two MIB tables and needs a credential to do
/// it. A device with none would produce one failed job per interval for ever, each one recording
/// the same sentence on the scan row — so it is skipped, and the absence shows on the device
/// rather than as a queue full of failures.
/// </para>
/// </remarks>
internal sealed class ClientSchedulePass(
    InventoryDbContext context,
    ICollectorJobQueue queue,
    SnmpCredentialSelector credentials,
    IOptions<ClientOptions> options,
    IClock clock,
    ILogger<ClientSchedulePass> logger)
{
    /// <summary>Queues a client walk for every device that is due, up to the configured ceiling.</summary>
    /// <returns>How many walks were queued.</returns>
    public async Task<int> ScheduleDueAsync(CancellationToken cancellationToken)
    {
        ClientOptions settings = options.Value;

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

        // Serialised once for the whole pass: the parameters say how to read, not which device
        // is being read.
        JsonElement parameters = WalkParameters(settings);

        int queued = 0;

        foreach (DueDevice device in due)
        {
            Guid? profileId = await credentials.ChooseAsync(device.DeviceId, cancellationToken);

            if (profileId is not { } chosen)
            {
                // Pushed forward anyway, for the reason a discovery seed's next run is: the pass
                // would otherwise find this device, refuse it and log the same sentence on every
                // scan for as long as it had no credential.
                Reschedule(device, settings, now);

                continue;
            }

            Result<Guid> job = await queue.EnlistAsync(
                context,
                new NewCollectorJob(
                    CollectorJobKind.Discover,
                    device.DeviceId,
                    chosen,
                    parameters,
                    DueAt: now),
                cancellationToken);

            if (!job.IsSuccess)
            {
                logger.LogInformation(
                    "A client walk for device {DeviceId} was not queued: {Reason}",
                    device.DeviceId,
                    job.Error.Message);

                continue;
            }

            Reschedule(device, settings, now);
            queued++;
        }

        // One save for the pass, so every queued job and every next-walk stamp commits together.
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Queued {Count} client walks", queued);

        return queued;
    }

    /// <summary>
    /// The live devices that have never been read, or whose next walk is due, and which have no
    /// <c>Discover</c> outstanding.
    /// </summary>
    /// <remarks>
    /// The outstanding check covers every <c>Discover</c> naming the device rather than only a
    /// client walk, which is deliberately stricter than it needs to be: a fingerprint walk and a
    /// client walk both open an SNMP session to the same agent with the same credential, and
    /// running them at once is two conversations a small device has no reason to be asked to
    /// hold. It is the same rule <c>QueueDeviceWalkHandler</c> applies from the other side.
    /// </remarks>
    private async Task<IReadOnlyList<DueDevice>> FindDueAsync(
        ClientOptions settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var candidates = await (
            from device in context.Devices
            join scan in context.DeviceClientScans on device.Id equals scan.DeviceId into matched
            from scan in matched.DefaultIfEmpty()
            where device.DeletedAt == null
                && (scan == null || scan.NextWalkAt <= now)
                && !context.CollectorJobs.Any(job =>
                    job.DeviceId == device.Id
                    && job.Kind == CollectorJobKind.Discover
                    && (job.Status == CollectorJobStatus.Pending || job.Status == CollectorJobStatus.Leased))
            orderby scan == null ? DateTimeOffset.MinValue : scan.NextWalkAt, device.Id
            select new { DeviceId = device.Id, Scan = scan })
            .Take(settings.MaxJobsPerScan)
            .ToListAsync(cancellationToken);

        List<DueDevice> due = new(candidates.Count);

        foreach (var candidate in candidates)
        {
            due.Add(new DueDevice(candidate.DeviceId, candidate.Scan ?? Create(candidate.DeviceId, now)));
        }

        return due;
    }

    private DeviceClientScan Create(Guid deviceId, DateTimeOffset now)
    {
        DeviceClientScan row = new()
        {
            Id = Guid.CreateVersion7(now),
            DeviceId = deviceId,
            NextWalkAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.DeviceClientScans.Add(row);

        return row;
    }

    /// <summary>Moves a device's next walk one interval out, with a small deterministic spread.</summary>
    /// <remarks>
    /// The spread is what stops an estate imported in one discovery run from falling due in the
    /// same second for ever afterwards, and it is derived from the device's own id rather than
    /// from a random source so that a device's offset survives a restart — exactly as
    /// <c>ReachabilitySchedulePass</c> does it, for exactly the same reason.
    /// </remarks>
    private static void Reschedule(DueDevice device, ClientOptions settings, DateTimeOffset now)
    {
        int spread = device.Scan.DeviceId.ToByteArray()[^1] % settings.ScanIntervalSeconds;

        device.Scan.NextWalkAt = now.AddSeconds(settings.WalkIntervalSeconds + spread);
        device.Scan.UpdatedAt = now;
    }

    private static JsonElement WalkParameters(ClientOptions settings)
    {
        using JsonDocument document = JsonSerializer.SerializeToDocument(
            ClientWalkParameters.From(settings),
            ClientSerializerContext.Default.ClientWalkParameters);

        return document.RootElement.Clone();
    }

    /// <summary>A device that is due, and the row recording when it will be due again.</summary>
    private sealed record DueDevice(Guid DeviceId, DeviceClientScan Scan);
}
