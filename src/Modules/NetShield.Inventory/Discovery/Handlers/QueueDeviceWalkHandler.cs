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
/// opens exactly that one, so something has to choose. SNMPv3 comes before SNMPv2c because v3 is
/// the one that authenticates and encrypts, and among equals the earliest assignment wins so the
/// answer does not depend on the order rows came back in. Configurable ordering is WP-1.6's, and
/// this is deliberately the smallest deterministic rule that does not pre-empt it.
/// </para>
/// </remarks>
internal sealed class QueueDeviceWalkHandler(
    InventoryDbContext context,
    ICollectorJobQueue queue,
    IOptions<DiscoveryOptions> options,
    IResourceGuard guard,
    IAuditContext audit,
    IClock clock,
    ILogger<QueueDeviceWalkHandler> logger)
{
    /// <summary>The kinds of credential an SNMP walk can be run with, best first.</summary>
    private static readonly CredentialKind[] SnmpKinds = [CredentialKind.SnmpV3, CredentialKind.SnmpV2c];

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

        Guid? profileId = await ChooseCredentialAsync(deviceId, cancellationToken);

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
    /// Every unfinished <c>Discover</c> counts, not only one this package queued. WP-1.6's sweep
    /// will queue <c>Discover</c> rows too, and two collectors walking one device at once would
    /// have their results applied in whichever order they came back.
    /// </remarks>
    private Task<bool> HasOutstandingWalkAsync(Guid deviceId, CancellationToken cancellationToken) =>
        context.CollectorJobs.AnyAsync(
            job => job.DeviceId == deviceId
                && job.Kind == CollectorJobKind.Discover
                && (job.Status == CollectorJobStatus.Pending || job.Status == CollectorJobStatus.Leased),
            cancellationToken);

    /// <summary>
    /// The device's SNMP credential profile: v3 before v2c, then the earliest assignment.
    /// </summary>
    /// <remarks>
    /// Soft-deleted profiles are excluded. A profile an operator revoked must not keep reaching a
    /// collector, which is the same rule the lease applies one step later (WP-1.3).
    /// </remarks>
    private async Task<Guid?> ChooseCredentialAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        var candidates = await (
            from assignment in context.DeviceCredentialProfiles
            join profile in context.CredentialProfiles
                on assignment.CredentialProfileId equals profile.Id
            where assignment.DeviceId == deviceId
                && profile.DeletedAt == null
                && SnmpKinds.Contains(profile.Kind)
            select new { profile.Id, profile.Kind, assignment.CreatedAt })
            .ToListAsync(cancellationToken);

        return candidates
            .OrderBy(candidate => Array.IndexOf(SnmpKinds, candidate.Kind))
            .ThenBy(candidate => candidate.CreatedAt)
            .ThenBy(candidate => candidate.Id)
            .Select(candidate => (Guid?)candidate.Id)
            .FirstOrDefault();
    }

    private static JsonElement Parameters(DiscoveryOptions settings)
    {
        using JsonDocument document = JsonSerializer.SerializeToDocument(
            SnmpWalkParameters.From(settings),
            DiscoverySerializerContext.Default.SnmpWalkParameters);

        return document.RootElement.Clone();
    }
}
