using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NetShield.Contracts.Collector;
using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Collector;
using NetShield.Inventory.Devices;
using NetShield.Inventory.Devices.Handlers;
using NetShield.Inventory.Persistence;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Discovery.Handlers;

/// <summary>
/// Queues one SNMP walk of one device, on demand.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is a fingerprint refresh, not a discovery run.</strong> It walks a device that is
/// already in the inventory and tells NetShield what it is. The range sweep that finds hosts
/// nobody has entered — seeds, exclusions, reviewable candidates, an ignore list — is WP-1.6's,
/// and nothing here schedules anything: this queues one job because a person asked for one.
/// </para>
/// <para>
/// <strong>Choosing the credential.</strong> The job names exactly one profile and the lease
/// opens exactly that one, so something has to choose. WP-1.5 made that choice here with the
/// order written into this file; WP-1.6 moved it into <see cref="SnmpCredentialSelector"/>,
/// where it is read from <c>DiscoveryOptions.CredentialKindOrder</c> — the "credential profile
/// order" that package's entry names — so that the sweep and the walk cannot come to disagree
/// about which credential a device is reached with.
/// </para>
/// </remarks>
internal sealed class QueueDeviceWalkHandler(
    InventoryDbContext context,
    ICollectorJobQueue queue,
    SnmpCredentialSelector credentials,
    IOptions<DiscoveryOptions> options,
    IResourceGuard guard,
    IAuditContext audit,
    IClock clock,
    ILogger<QueueDeviceWalkHandler> logger)
{
    public async Task<Result<DeviceWalkQueued>> HandleAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        Result permitted = guard.Require(Permission.DiscoveryRun, GetDeviceListHandler.ResourceType);

        if (!permitted.IsSuccess)
        {
            return Result<DeviceWalkQueued>.Failure(permitted.Error);
        }

        Device? device = await context.Devices.AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == deviceId && candidate.DeletedAt == null,
                cancellationToken);

        if (device is null)
        {
            return DeviceErrors.NotFound(deviceId);
        }

        if (await HasOutstandingWalkAsync(deviceId, cancellationToken))
        {
            return DiscoveryErrors.WalkOutstanding(deviceId);
        }

        Guid? profileId = await credentials.ChooseAsync(deviceId, cancellationToken);

        if (profileId is not { } chosen)
        {
            return DiscoveryErrors.NoSnmpCredential(deviceId);
        }

        DateTimeOffset now = clock.UtcNow;

        Result<Guid> queued = await queue.EnqueueAsync(
            new NewCollectorJob(
                CollectorJobKind.Discover,
                deviceId,
                chosen,
                Parameters(options.Value),
                DueAt: now),
            cancellationToken);

        if (!queued.IsSuccess)
        {
            return Result<DeviceWalkQueued>.Failure(queued.Error);
        }

        logger.LogInformation(
            "Queued an SNMP walk {JobId} for device {DeviceId} with credential profile {CredentialProfileId}",
            queued.Value,
            deviceId,
            chosen);

        audit.Target(GetDeviceListHandler.ResourceType, deviceId.ToString());

        // The chosen profile is logged and is on the job row; it is not in the answer. See
        // DeviceWalkQueued for why.
        return new DeviceWalkQueued(queued.Value, deviceId, now);
    }

    /// <summary>Whether a walk for this device is already queued or leased.</summary>
    /// <remarks>
    /// Every unfinished <c>Discover</c> that names this device counts. WP-1.6's range sweep is a
    /// <c>Discover</c> too but names no device, so it cannot collide here — what this rule is
    /// for is two collectors walking one device at once and having their results applied in
    /// whichever order they came back.
    /// </remarks>
    private Task<bool> HasOutstandingWalkAsync(Guid deviceId, CancellationToken cancellationToken) =>
        context.CollectorJobs.AnyAsync(
            job => job.DeviceId == deviceId
                && job.Kind == CollectorJobKind.Discover
                && (job.Status == CollectorJobStatus.Pending || job.Status == CollectorJobStatus.Leased),
            cancellationToken);

    private static JsonElement Parameters(DiscoveryOptions settings)
    {
        using JsonDocument document = JsonSerializer.SerializeToDocument(
            SnmpWalkParameters.From(settings),
            DiscoverySerializerContext.Default.SnmpWalkParameters);

        return document.RootElement.Clone();
    }
}
