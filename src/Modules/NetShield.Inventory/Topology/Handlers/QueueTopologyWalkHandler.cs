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
using NetShield.Inventory.Discovery;
using NetShield.Inventory.Persistence;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Topology.Handlers;

/// <summary>
/// Queues one topology read of one device, on demand.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as WP-1.5's on-demand fingerprint walk and WP-1.8's client walk, gated on the
/// same permission and for the same reason: it makes NetShield open a credential and read a
/// device outside its schedule, which is what <see cref="Permission.DiscoveryRun"/> names and a
/// different privilege from editing that device's notes.
/// </para>
/// <para>
/// One handler serves both walks. They differ in which parameters they carry and in nothing else
/// that queueing has an opinion about — the permission, the outstanding-walk refusal, the
/// credential choice and the audit target are identical, and writing them twice would be two
/// places for them to drift apart.
/// </para>
/// <para>
/// It refuses a device that already has a <c>Discover</c> outstanding, which for these walks is
/// more than a convenience: an edge is withdrawn when a complete reading no longer contains it,
/// so two walks of one device applied in whichever order they came back would have each
/// withdrawing what the other had just established.
/// </para>
/// </remarks>
internal sealed class QueueTopologyWalkHandler(
    InventoryDbContext context,
    ICollectorJobQueue queue,
    SnmpCredentialSelector credentials,
    IOptions<TopologyOptions> options,
    IResourceGuard guard,
    IAuditContext audit,
    IClock clock,
    ILogger<QueueTopologyWalkHandler> logger)
{
    public async Task<Result<NeighborWalkQueued>> HandleAsync(
        Guid deviceId,
        TopologyWalkKind walk,
        CancellationToken cancellationToken)
    {
        Result permitted = guard.Require(Permission.DiscoveryRun, GetDeviceListHandler.ResourceType);

        if (!permitted.IsSuccess)
        {
            return Result<NeighborWalkQueued>.Failure(permitted.Error);
        }

        bool exists = await context.Devices.AsNoTracking().AnyAsync(
            candidate => candidate.Id == deviceId && candidate.DeletedAt == null,
            cancellationToken);

        if (!exists)
        {
            return DeviceErrors.NotFound(deviceId);
        }

        if (await HasOutstandingWalkAsync(deviceId, cancellationToken))
        {
            return TopologyErrors.WalkOutstanding(deviceId, walk);
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
                Parameters(options.Value, walk),
                DueAt: now),
            cancellationToken);

        if (!queued.IsSuccess)
        {
            return Result<NeighborWalkQueued>.Failure(queued.Error);
        }

        logger.LogInformation(
            "Queued a {Walk} topology walk {JobId} for device {DeviceId} "
            + "with credential profile {CredentialProfileId}",
            walk,
            queued.Value,
            deviceId,
            chosen);

        audit.Target(GetDeviceListHandler.ResourceType, deviceId.ToString());

        // The chosen profile is on the job row and in the log, and deliberately not in the
        // answer: this route is gated on DiscoveryRun, which says nothing about credentials.
        return new NeighborWalkQueued(queued.Value, deviceId, walk, now);
    }

    private Task<bool> HasOutstandingWalkAsync(Guid deviceId, CancellationToken cancellationToken) =>
        context.CollectorJobs.AnyAsync(
            job => job.DeviceId == deviceId
                && job.Kind == CollectorJobKind.Discover
                && (job.Status == CollectorJobStatus.Pending
                    || job.Status == CollectorJobStatus.Leased),
            cancellationToken);

    internal static JsonElement Parameters(TopologyOptions settings, TopologyWalkKind walk)
    {
        using JsonDocument document = walk == TopologyWalkKind.Routes
            ? JsonSerializer.SerializeToDocument(
                RouteWalkParameters.From(settings),
                TopologySerializerContext.Default.RouteWalkParameters)
            : JsonSerializer.SerializeToDocument(
                NeighborWalkParameters.From(settings),
                TopologySerializerContext.Default.NeighborWalkParameters);

        return document.RootElement.Clone();
    }
}
