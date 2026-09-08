using System.Net;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NetShield.Contracts.Collector;
using NetShield.Contracts.Collector.Events;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Inventory.Events;

using NetShield.Inventory.Collector;
using NetShield.Inventory.Persistence;

using NetShield.Platform.Messaging;

namespace NetShield.Inventory.Topology.Handlers;

/// <summary>
/// Reads a finished neighbour walk and records what a device is cabled to.
/// </summary>
/// <remarks>
/// <para>
/// The fifth subscriber to <c>CollectorJobCompleted</c>. Like the four before it, it reads only
/// the rows this package queued — a <c>Discover</c> whose parameters name the <c>neighbors</c>
/// walk — and leaves every other job to whoever queued it.
/// </para>
/// <para>
/// <strong>A failed walk changes no edge.</strong> An unreachable device, a refused community
/// string or a timeout means nothing was observed. Letting that withdraw edges would report an
/// estate coming apart because one switch stopped answering SNMP — and Phase 6's topology-aware
/// suppression would then be reasoning about a network that had fallen apart on paper. The same
/// reasoning WP-1.4 applied to device state, WP-1.5 to device identity and WP-1.8 to client
/// bindings: a collector's health is never evidence about the estate.
/// </para>
/// <para>
/// <strong>A protocol the device does not implement is not an empty one.</strong> The payload
/// carries a <c>supported</c> flag per protocol for exactly this. Only a protocol that was
/// supported <em>and</em> untruncated is allowed to withdraw an edge by not mentioning it, which
/// is what makes "a removed link ages out" safe rather than destructive.
/// </para>
/// </remarks>
internal sealed class RecordNeighborWalkResultHandler(
    InventoryDbContext context,
    TopologyWalkResultReader reader,
    NeighborObservationApplier applier,
    OutboxEnlistment outbox,
    IOptions<TopologyOptions> options,
    ILogger<RecordNeighborWalkResultHandler> logger) : IIntegrationEventHandler<CollectorJobCompleted>
{
    /// <summary>The two protocols this walk is responsible for.</summary>
    private static readonly IReadOnlySet<NeighborSource> Handled =
        new HashSet<NeighborSource> { NeighborSource.Lldp, NeighborSource.Cdp };

    public async Task HandleAsync(
        CollectorJobCompleted integrationEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        TopologyWalkResultReader.Match? matched = await reader.MatchAsync(
            integrationEvent,
            TopologyWalkKind.Neighbors,
            options.Value,
            cancellationToken);

        if (matched is not { } match)
        {
            return;
        }

        DeviceTopologyScan scan = match.Scan;

        scan.LastNeighborJobId = integrationEvent.JobId;
        scan.LastNeighborWalkAt = match.Now;
        scan.UpdatedAt = match.Now;

        NeighborObservationApplier.Applied? applied = null;

        if (integrationEvent.Outcome != CollectorJobOutcome.Succeeded)
        {
            // Already through SecretRedactor on its way into the column (WP-1.3), so it is safe
            // to carry across and safe to log.
            scan.LastNeighborError =
                match.Job.Detail ?? "The collector could not read the device's neighbour tables.";

            logger.LogWarning(
                "A neighbour walk of device {DeviceId} could not be performed: {Detail}",
                match.DeviceId,
                scan.LastNeighborError);
        }
        else if (Parse(match.Job) is { } result)
        {
            applied = await ApplyAsync(scan, result, match, cancellationToken);
        }
        else
        {
            scan.LastNeighborError = "The collector reported a result this walk could not read.";

            logger.LogWarning(
                "Collector job {JobId} succeeded but carried no readable neighbour walk result.",
                integrationEvent.JobId);
        }

        await context.SaveChangesAsync(cancellationToken);

        if (applied is { Changed: true })
        {
            await PublishAsync(match, applied, cancellationToken);
        }
    }

    private async Task<NeighborObservationApplier.Applied?> ApplyAsync(
        DeviceTopologyScan scan,
        NeighborWalkResult result,
        TopologyWalkResultReader.Match match,
        CancellationToken cancellationToken)
    {
        if (!await reader.IsLiveAsync(match.DeviceId, cancellationToken))
        {
            logger.LogInformation(
                "A neighbour walk result for device {DeviceId} was dropped: the device has been removed.",
                match.DeviceId);

            return null;
        }

        scan.LastNeighborError = null;
        scan.LldpSupported = result.LldpSupported;
        scan.CdpSupported = result.CdpSupported;
        scan.LastLldpCount = result.LldpSupported ? result.LldpCount : null;
        scan.LastCdpCount = result.CdpSupported ? result.CdpCount : null;

        List<NeighborObservation> observations = [];

        if (result.LldpSupported)
        {
            observations.AddRange(FromLldp(result.Lldp));
        }

        if (result.CdpSupported)
        {
            observations.AddRange(FromCdp(result.Cdp));
        }

        // Only a protocol that answered and was not cut short may say a link has gone. A device
        // with CDP disabled withdraws no CDP edges, and a reading that hit its ceiling withdraws
        // nothing at all — it saw part of a table, so an absence from it is not an absence.
        HashSet<NeighborSource> complete = [];

        if (result.LldpSupported && !result.LldpTruncated)
        {
            complete.Add(NeighborSource.Lldp);
        }

        if (result.CdpSupported && !result.CdpTruncated)
        {
            complete.Add(NeighborSource.Cdp);
        }

        if (result.LldpTruncated || result.CdpTruncated)
        {
            logger.LogWarning(
                "A neighbour walk of device {DeviceId} hit a reporting ceiling; "
                + "LLDP truncated: {LldpTruncated}, CDP truncated: {CdpTruncated}. "
                + "No edge will be withdrawn from a truncated reading.",
                match.DeviceId,
                result.LldpTruncated,
                result.CdpTruncated);
        }

        NeighborObservationApplier.Applied applied = await applier.ApplyAsync(
            match.DeviceId,
            new NeighborObservationApplier.LocalIdentity(
                result.LocalChassisId,
                Kind(result.LocalChassisIdKind),
                result.LocalSystemName),
            observations,
            Handled,
            complete,
            match.Now,
            cancellationToken);

        if (applied.Changed)
        {
            logger.LogInformation(
                "A neighbour walk of device {DeviceId} added {Added} edges and withdrew {Withdrawn}; "
                + "{EdgeCount} live, {Suppressed} observations set aside",
                match.DeviceId,
                applied.Added,
                applied.Withdrawn,
                applied.EdgeCount,
                applied.Suppressed);
        }

        return applied;
    }

    /// <summary>
    /// The LLDP entries that name a far end, as observations.
    /// </summary>
    /// <remarks>
    /// An entry with no local interface, no chassis identifier or a non-positive interface index
    /// is dropped. None of them names an edge NetShield could do anything with: the chassis id is
    /// what every match and every merge is built on, and an interface the rest of the system
    /// cannot identify is not somewhere a cable can be said to leave from.
    /// </remarks>
    private static IEnumerable<NeighborObservation> FromLldp(IReadOnlyList<LldpNeighborEntry>? entries)
    {
        foreach (LldpNeighborEntry entry in entries ?? [])
        {
            if (entry.LocalIfIndex is not { } ifIndex
                || ifIndex <= 0
                || string.IsNullOrWhiteSpace(entry.ChassisId))
            {
                continue;
            }

            yield return new NeighborObservation
            {
                Source = NeighborSource.Lldp,
                LocalIfIndex = ifIndex,
                LocalInterfaceName = entry.LocalPortName,
                RemoteChassisId = entry.ChassisId.Trim(),
                RemoteChassisIdKind = Kind(entry.ChassisIdKind),
                RemotePortId = Blank(entry.PortId),
                RemotePortIdKind = Kind(entry.PortIdKind),
                RemotePortDescription = Blank(entry.PortDescription),
                RemoteSystemName = Blank(entry.SystemName),
                RemoteSystemDescription = Blank(entry.SystemDescription),
                RemoteManagementAddress = Address(entry.ManagementAddress),
                Capabilities = entry.Capabilities
            };
        }
    }

    /// <summary>
    /// The CDP entries that name a far end, as observations.
    /// </summary>
    /// <remarks>
    /// The device id becomes the chassis identifier, of kind <see cref="NeighborIdKind.DeviceId"/>
    /// — which is not an 802.1AB subtype and is the point: it says out loud that this identity is
    /// a host name rather than an identity, which is why the conflict rule lets LLDP outrank it.
    /// </remarks>
    private static IEnumerable<NeighborObservation> FromCdp(IReadOnlyList<CdpNeighborEntry>? entries)
    {
        foreach (CdpNeighborEntry entry in entries ?? [])
        {
            if (entry.LocalIfIndex is not { } ifIndex
                || ifIndex <= 0
                || string.IsNullOrWhiteSpace(entry.DeviceId))
            {
                continue;
            }

            yield return new NeighborObservation
            {
                Source = NeighborSource.Cdp,
                LocalIfIndex = ifIndex,
                LocalInterfaceName = entry.LocalPortName,
                RemoteChassisId = entry.DeviceId.Trim(),
                RemoteChassisIdKind = NeighborIdKind.DeviceId,
                RemotePortId = Blank(entry.DevicePort),
                RemotePortIdKind = NeighborIdKind.InterfaceName,
                RemoteSystemName = Blank(entry.DeviceId),
                RemoteSystemDescription = Blank(entry.Version),
                RemotePlatform = Blank(entry.Platform),
                RemoteManagementAddress = Address(entry.Address),
                Capabilities = entry.Capabilities
            };
        }
    }

    /// <summary>
    /// A collector-supplied identifier-kind name, or <see cref="NeighborIdKind.Unknown"/>.
    /// </summary>
    /// <remarks>
    /// Parsed rather than trusted, so a collector newer than the API naming a subtype this build
    /// does not have leaves an edge that reads <c>Unknown</c> rather than failing the walk. The
    /// same direction WP-1.5 chose for an unrecognised vendor.
    /// </remarks>
    private static NeighborIdKind Kind(string? name) =>
        Enum.TryParse(name, out NeighborIdKind kind) ? kind : NeighborIdKind.Unknown;

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IPAddress? Address(string? value) =>
        IPAddress.TryParse(value, out IPAddress? address) ? address : null;

    private async Task PublishAsync(
        TopologyWalkResultReader.Match match,
        NeighborObservationApplier.Applied applied,
        CancellationToken cancellationToken)
    {
        string? hostname = await context.Devices.AsNoTracking()
            .Where(device => device.Id == match.DeviceId)
            .Select(device => device.Hostname)
            .SingleOrDefaultAsync(cancellationToken);

        if (hostname is null)
        {
            return;
        }

        // Staged on this context and saved with it, so the edges and the event announcing them
        // commit together or not at all — the OutboxEnlistment arrangement WP-1.1 settled.
        outbox.Enlist(context, new DeviceAdjacencyChanged(
            match.DeviceId,
            hostname,
            TopologyWalkKind.Neighbors,
            applied.EdgeCount,
            applied.Added,
            applied.Withdrawn,
            match.Now));

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The walk result on the job row, or nothing if it does not read as one.</summary>
    private NeighborWalkResult? Parse(CollectorJob job)
    {
        if (string.IsNullOrEmpty(job.Result))
        {
            return null;
        }

        try
        {
            NeighborWalkResult? result = JsonSerializer.Deserialize(
                job.Result,
                TopologySerializerContext.Default.NeighborWalkResult);

            // The discriminator has to agree with the parameters. A payload that does not name
            // this walk is a collector answering a question nobody asked.
            return string.Equals(
                result?.Walk,
                NeighborWalkParameters.WalkName,
                StringComparison.Ordinal)
                ? result
                : null;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "Collector job {JobId} carried a result that is not a neighbour walk result.",
                job.Id);

            return null;
        }
    }
}
