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

namespace NetShield.Inventory.Clients.Handlers;

/// <summary>
/// Queues one read of one device's client tables, on demand.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as WP-1.5's on-demand fingerprint walk and gated on the same permission, for
/// the same reason: it makes NetShield open a credential and read a device outside its schedule,
/// which is what <see cref="Permission.DiscoveryRun"/> names and a different privilege from
/// editing that device's notes.
/// </para>
/// <para>
/// It refuses a device that already has a <c>Discover</c> outstanding, which for this walk is
/// more than a convenience: two collectors reading one forwarding database and having their
/// results applied in whichever order they came back would mean two walks each closing intervals
/// the other had just opened.
/// </para>
/// <para>
/// The credential comes from <see cref="SnmpCredentialSelector"/> — the same order the
/// fingerprint walk and the discovery schedule use, so the three cannot come to disagree about
/// which credential a device is reached with.
/// </para>
/// </remarks>
internal sealed class QueueClientWalkHandler(
    InventoryDbContext context,
    ICollectorJobQueue queue,
    SnmpCredentialSelector credentials,
    IOptions<ClientOptions> options,
    IResourceGuard guard,
    IAuditContext audit,
    IClock clock,
    ILogger<QueueClientWalkHandler> logger)
{
    public async Task<Result<ClientWalkQueued>> HandleAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        Result permitted = guard.Require(Permission.DiscoveryRun, GetDeviceListHandler.ResourceType);

        if (!permitted.IsSuccess)
        {
            return Result<ClientWalkQueued>.Failure(permitted.Error);
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
            return ClientErrors.WalkOutstanding(deviceId);
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
            return Result<ClientWalkQueued>.Failure(queued.Error);
        }

        logger.LogInformation(
            "Queued a client walk {JobId} for device {DeviceId} with credential profile {CredentialProfileId}",
            queued.Value,
            deviceId,
            chosen);

        audit.Target(GetDeviceListHandler.ResourceType, deviceId.ToString());

        // The chosen profile is on the job row and in the log, and deliberately not in the
        // answer: this route is gated on DiscoveryRun, which says nothing about credentials.
        return new ClientWalkQueued(queued.Value, deviceId, now);
    }

    private Task<bool> HasOutstandingWalkAsync(Guid deviceId, CancellationToken cancellationToken) =>
        context.CollectorJobs.AnyAsync(
            job => job.DeviceId == deviceId
                && job.Kind == CollectorJobKind.Discover
                && (job.Status == CollectorJobStatus.Pending || job.Status == CollectorJobStatus.Leased),
            cancellationToken);

    private static JsonElement Parameters(ClientOptions settings)
    {
        using JsonDocument document = JsonSerializer.SerializeToDocument(
            ClientWalkParameters.From(settings),
            ClientSerializerContext.Default.ClientWalkParameters);

        return document.RootElement.Clone();
    }
}
