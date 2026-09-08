using System.Net;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NetShield.Contracts.Collector;
using NetShield.Contracts.Collector.Events;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Collector;
using NetShield.Inventory.Persistence;
using NetShield.Inventory.Resolution;

using NetShield.Platform.Caching;
using NetShield.Platform.Messaging;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Clients.Handlers;

/// <summary>
/// Reads a finished client walk and records who is on the network and where.
/// </summary>
/// <remarks>
/// <para>
/// The fourth subscriber to <c>CollectorJobCompleted</c>. Like the three before it, it reads only
/// the rows this package queued — a <c>Discover</c> whose parameters name the <c>clients</c> walk
/// — and leaves every other job to whoever queued it.
/// </para>
/// <para>
/// <strong>A failed walk changes no binding.</strong> An unreachable device, a refused community
/// string or a timeout means nothing was observed, and letting that close intervals would report
/// every endpoint behind a switch as having left because the switch stopped answering SNMP. The
/// same reasoning WP-1.4 applied to device state and WP-1.5 to device identity, applied to who is
/// attached: a collector's health is never evidence about the estate.
/// </para>
/// <para>
/// <strong>A table the device does not implement is not an empty table.</strong> The payload
/// carries a <c>supported</c> flag per reading for exactly this: a router implements no
/// forwarding database and an access switch no neighbour cache, and treating either absence as
/// "this device sees nobody" would be reading silence as a statement.
/// </para>
/// <para>
/// <strong>Safe to run twice.</strong> Outbox delivery is at-least-once, and the scan row records
/// the job it last applied. A redelivered walk is dropped rather than folded in again — which
/// matters more here than for a counter, because a second application arriving after a competing
/// observation would close and reopen an interval on evidence that had already been spent.
/// </para>
/// </remarks>
internal sealed class RecordClientWalkResultHandler(
    InventoryDbContext context,
    ClientObservationApplier applier,
    ICacheStore cache,
    IOptions<ClientOptions> options,
    IClock clock,
    ILogger<RecordClientWalkResultHandler> logger) : IIntegrationEventHandler<CollectorJobCompleted>
{
    public async Task HandleAsync(
        CollectorJobCompleted integrationEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (integrationEvent.Kind != CollectorJobKind.Discover
            || integrationEvent.DeviceId is not { } deviceId)
        {
            return;
        }

        CollectorJob? job = await context.CollectorJobs.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == integrationEvent.JobId, cancellationToken);

        if (job is null || !IsClientWalk(job))
        {
            return;
        }

        DateTimeOffset now = clock.UtcNow;

        DeviceClientScan scan = await context.DeviceClientScans
            .SingleOrDefaultAsync(row => row.DeviceId == deviceId, cancellationToken)
            ?? Create(deviceId, now);

        if (scan.LastAppliedJobId == integrationEvent.JobId)
        {
            return;
        }

        scan.LastAppliedJobId = integrationEvent.JobId;
        scan.LastWalkAt = now;
        scan.UpdatedAt = now;

        IReadOnlySet<IPAddress> changed = new HashSet<IPAddress>();

        if (integrationEvent.Outcome != CollectorJobOutcome.Succeeded)
        {
            // Already through SecretRedactor on its way into the column (WP-1.3), so it is safe
            // to carry across and safe to log.
            scan.LastError = job.Detail ?? "The collector could not read the device's client tables.";

            logger.LogWarning(
                "A client walk of device {DeviceId} could not be performed: {Detail}",
                deviceId,
                scan.LastError);
        }
        else if (Parse(job) is { } result)
        {
            changed = await ApplyAsync(scan, result, deviceId, now, cancellationToken);
        }
        else
        {
            scan.LastError = "The collector reported a result this walk could not read.";

            logger.LogWarning(
                "Collector job {JobId} succeeded but carried no readable client walk result.",
                integrationEvent.JobId);
        }

        await context.SaveChangesAsync(cancellationToken);

        // After the commit, deliberately. Dropping the entries first would let a concurrent
        // resolution rebuild them from rows that had not landed yet; dropping them after means
        // the worst case is one rebuild too many. An invalidation lost to an unreachable Redis is
        // bounded by the entry's own lifetime, which is why every entry has one.
        await InvalidateAsync(changed, cancellationToken);
    }

    /// <summary>Folds a walk that ran into the client tables.</summary>
    private async Task<IReadOnlySet<IPAddress>> ApplyAsync(
        DeviceClientScan scan,
        ClientWalkResult result,
        Guid deviceId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        bool live = await context.Devices.AnyAsync(
            device => device.Id == deviceId && device.DeletedAt == null,
            cancellationToken);

        if (!live)
        {
            // Removed while the walk was in flight. Writing bindings that name a device an
            // operator deleted would resurrect it in every join that forgot to filter, and the
            // observations are about a device NetShield no longer monitors.
            logger.LogInformation(
                "A client walk result for device {DeviceId} was dropped: the device has been removed.",
                deviceId);

            return new HashSet<IPAddress>();
        }

        scan.LastError = null;
        scan.NeighborsSupported = result.NeighborsSupported;
        scan.ForwardingSupported = result.ForwardingSupported;
        scan.LastNeighborCount = result.NeighborsSupported ? result.NeighborCount : null;
        scan.LastForwardingCount = result.ForwardingSupported ? result.ForwardingCount : null;

        List<ClientObservation.Address> addresses = result.NeighborsSupported
            ? [.. Addresses(result.Neighbors)]
            : [];

        List<ClientObservation.Port> ports = result.ForwardingSupported
            ? [.. Ports(result.Forwarding)]
            : [];

        ClientObservationApplier.Applied applied = await applier.ApplyAsync(
            deviceId,
            addresses,
            ports,
            now,
            cancellationToken);

        if (applied.NewClients > 0 || applied.ClosedBindings > 0 || applied.MovedPorts > 0)
        {
            logger.LogInformation(
                "A client walk of device {DeviceId} found {NewClients} new clients, "
                + "{ClosedBindings} address handovers and {MovedPorts} port moves",
                deviceId,
                applied.NewClients,
                applied.ClosedBindings,
                applied.MovedPorts);
        }

        if (result.NeighborsTruncated || result.ForwardingTruncated)
        {
            // Worth a line because it changes what the data means: a truncated reading saw part
            // of a table, so nothing absent from it is absent from the device.
            logger.LogWarning(
                "A client walk of device {DeviceId} hit a reporting ceiling; "
                + "neighbours truncated: {NeighborsTruncated}, forwarding truncated: {ForwardingTruncated}",
                deviceId,
                result.NeighborsTruncated,
                result.ForwardingTruncated);
        }

        return applied.ChangedAddresses;
    }

    /// <summary>
    /// The neighbour entries that describe an endpoint, as observations.
    /// </summary>
    /// <remarks>
    /// A MAC that will not parse, an address that will not parse, the all-zero address of an
    /// incomplete ARP entry, and a group address are all dropped. None of them names an endpoint:
    /// an incomplete entry is the device saying it asked and got no answer, and an IP bound to a
    /// multicast or broadcast MAC is not a host holding that address.
    /// </remarks>
    private static IEnumerable<ClientObservation.Address> Addresses(
        IReadOnlyList<ClientWalkNeighbor>? neighbors)
    {
        foreach (ClientWalkNeighbor neighbor in neighbors ?? [])
        {
            if (MacAddress.Normalize(neighbor.MacAddress) is not { } mac
                || MacAddress.IsMulticast(mac)
                || IsUnset(mac)
                || !IPAddress.TryParse(neighbor.IpAddress, out IPAddress? address))
            {
                continue;
            }

            yield return new ClientObservation.Address(mac, address, ClientObservationSource.ArpTable);
        }
    }

    /// <summary>The forwarding entries that describe an endpoint, as observations.</summary>
    /// <remarks>
    /// A forwarding database legitimately holds multicast and broadcast entries, and a switch
    /// forwarding to a group address is telling the truth about itself — it is simply not telling
    /// us about a client. An entry with no port has been learned by nothing this can attribute.
    /// </remarks>
    private static IEnumerable<ClientObservation.Port> Ports(
        IReadOnlyList<ClientWalkForwardingEntry>? forwarding)
    {
        foreach (ClientWalkForwardingEntry entry in forwarding ?? [])
        {
            if (MacAddress.Normalize(entry.MacAddress) is not { } mac
                || MacAddress.IsMulticast(mac)
                || IsUnset(mac)
                || entry.IfIndex is not { } ifIndex
                || ifIndex <= 0)
            {
                continue;
            }

            yield return new ClientObservation.Port(
                mac,
                ifIndex,
                entry.VlanId,
                entry.MacCountOnPort,
                ClientObservationSource.MacAddressTable);
        }
    }

    /// <summary>Whether this is the all-zero address an incomplete entry carries.</summary>
    private static bool IsUnset(string mac) => mac.All(character => character is '0' or ':');

    private async Task InvalidateAsync(
        IReadOnlySet<IPAddress> addresses,
        CancellationToken cancellationToken)
    {
        if (addresses.Count == 0)
        {
            return;
        }

        await cache.RemoveAsync([.. addresses.Select(AssetCacheKeys.For)], cancellationToken);
    }

    private DeviceClientScan Create(Guid deviceId, DateTimeOffset now)
    {
        DeviceClientScan row = new()
        {
            Id = Guid.CreateVersion7(now),
            DeviceId = deviceId,
            NextWalkAt = now.AddSeconds(options.Value.WalkIntervalSeconds),
            CreatedAt = now,
            UpdatedAt = now
        };

        context.DeviceClientScans.Add(row);

        return row;
    }

    /// <summary>Whether this job is one this package queued.</summary>
    private static bool IsClientWalk(CollectorJob job)
    {
        if (string.IsNullOrEmpty(job.Parameters))
        {
            return false;
        }

        try
        {
            ClientWalkParameters? parameters = JsonSerializer.Deserialize(
                job.Parameters,
                ClientSerializerContext.Default.ClientWalkParameters);

            return string.Equals(parameters?.Walk, ClientWalkParameters.WalkName, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            // Another package's parameter document, shaped differently. Not ours.
            return false;
        }
    }

    /// <summary>The walk result on the job row, or nothing if it does not read as one.</summary>
    private ClientWalkResult? Parse(CollectorJob job)
    {
        if (string.IsNullOrEmpty(job.Result))
        {
            return null;
        }

        try
        {
            ClientWalkResult? result = JsonSerializer.Deserialize(
                job.Result,
                ClientSerializerContext.Default.ClientWalkResult);

            // The discriminator has to agree with the parameters. A payload that does not name
            // this walk is a collector answering a question nobody asked.
            return string.Equals(result?.Walk, ClientWalkParameters.WalkName, StringComparison.Ordinal)
                ? result
                : null;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "Collector job {JobId} carried a result that is not a client walk result.",
                job.Id);

            return null;
        }
    }
}
