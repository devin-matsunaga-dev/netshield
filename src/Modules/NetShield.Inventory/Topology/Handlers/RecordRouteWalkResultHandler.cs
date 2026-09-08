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
/// Reads a finished route walk and records the L3 adjacency SPEC.md §2 asks for.
/// </summary>
/// <remarks>
/// <para>
/// The sixth subscriber to <c>CollectorJobCompleted</c>, and the second this package owns. It
/// reads only a <c>Discover</c> whose parameters name the <c>routes</c> walk, which is why a
/// route table that never answers costs the L3 half of a device's topology and nothing else: the
/// neighbour walk is a different row with a different result, and the edges it established are
/// untouched.
/// </para>
/// <para>
/// <strong>A next hop is a weaker claim than a cable, and the model says so.</strong> An edge
/// supported by routing alone is <see cref="AdjacencyConfidence.Possible"/> — two devices one hop
/// apart at layer 3 need not be plugged into each other, because a gateway reached through a
/// switch NetShield also monitors is still one hop away. Where a neighbour protocol has already
/// established the same edge, the routing observation merges into it as another source rather
/// than creating a second.
/// </para>
/// </remarks>
internal sealed class RecordRouteWalkResultHandler(
    InventoryDbContext context,
    TopologyWalkResultReader reader,
    NeighborObservationApplier applier,
    OutboxEnlistment outbox,
    IOptions<TopologyOptions> options,
    ILogger<RecordRouteWalkResultHandler> logger) : IIntegrationEventHandler<CollectorJobCompleted>
{
    /// <summary>The one source this walk is responsible for.</summary>
    private static readonly IReadOnlySet<NeighborSource> Handled =
        new HashSet<NeighborSource> { NeighborSource.Routing };

    private static readonly IReadOnlySet<NeighborSource> None = new HashSet<NeighborSource>();

    public async Task HandleAsync(
        CollectorJobCompleted integrationEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        TopologyWalkResultReader.Match? matched = await reader.MatchAsync(
            integrationEvent,
            TopologyWalkKind.Routes,
            options.Value,
            cancellationToken);

        if (matched is not { } match)
        {
            return;
        }

        DeviceTopologyScan scan = match.Scan;

        scan.LastRouteJobId = integrationEvent.JobId;
        scan.LastRouteWalkAt = match.Now;
        scan.UpdatedAt = match.Now;

        NeighborObservationApplier.Applied? applied = null;

        if (integrationEvent.Outcome != CollectorJobOutcome.Succeeded)
        {
            scan.LastRouteError =
                match.Job.Detail ?? "The collector could not read the device's routing table.";

            logger.LogWarning(
                "A route walk of device {DeviceId} could not be performed: {Detail}",
                match.DeviceId,
                scan.LastRouteError);
        }
        else if (Parse(match.Job) is { } result)
        {
            applied = await ApplyAsync(scan, result, match, cancellationToken);
        }
        else
        {
            scan.LastRouteError = "The collector reported a result this walk could not read.";

            logger.LogWarning(
                "Collector job {JobId} succeeded but carried no readable route walk result.",
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
        RouteWalkResult result,
        TopologyWalkResultReader.Match match,
        CancellationToken cancellationToken)
    {
        if (!await reader.IsLiveAsync(match.DeviceId, cancellationToken))
        {
            logger.LogInformation(
                "A route walk result for device {DeviceId} was dropped: the device has been removed.",
                match.DeviceId);

            return null;
        }

        scan.LastRouteError = null;
        scan.RoutingSupported = result.RoutesSupported;
        scan.RouteTable = result.RoutesSupported ? result.RouteTable : null;
        scan.LastRouteCount = result.RoutesSupported ? result.RouteCount : null;
        scan.LastNextHopCount = result.RoutesSupported ? result.NextHopCount : null;

        List<NeighborObservation> observations = result.RoutesSupported
            ? [.. FromNextHops(result.NextHops)]
            : [];

        if (result.NextHopsTruncated)
        {
            logger.LogWarning(
                "A route walk of device {DeviceId} hit a reporting ceiling. "
                + "No L3 edge will be withdrawn from a truncated reading.",
                match.DeviceId);
        }

        NeighborObservationApplier.Applied applied = await applier.ApplyAsync(
            match.DeviceId,
            // A routing table says nothing about how the device identifies itself, so the walk
            // carries no local identity. The neighbour walk is what establishes that, and where
            // it never runs the device is described by its own id — which is the honest answer
            // for a device that advertises nothing.
            new NeighborObservationApplier.LocalIdentity(null, NeighborIdKind.Unknown, null),
            observations,
            Handled,
            result is { RoutesSupported: true, NextHopsTruncated: false } ? Handled : None,
            match.Now,
            cancellationToken);

        if (applied.Changed)
        {
            logger.LogInformation(
                "A route walk of device {DeviceId} added {Added} edges and withdrew {Withdrawn}; "
                + "{EdgeCount} live",
                match.DeviceId,
                applied.Added,
                applied.Withdrawn,
                applied.EdgeCount);
        }

        return applied;
    }

    /// <summary>
    /// The gateways that name a far end, as observations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The gateway's address <em>is</em> its identity here, of kind
    /// <see cref="NeighborIdKind.NetworkAddress"/>, and it is also carried as the management
    /// address so that the resolver's strongest route — matching
    /// <c>devices.primary_ip_address</c> — is the one that answers.
    /// </para>
    /// <para>
    /// A hop with no interface index is dropped. An adjacency is a statement about a port, and a
    /// gateway the table cannot say which interface it is reached over is a route rather than an
    /// edge.
    /// </para>
    /// </remarks>
    private static IEnumerable<NeighborObservation> FromNextHops(
        IReadOnlyList<RouteNextHopEntry>? entries)
    {
        foreach (RouteNextHopEntry entry in entries ?? [])
        {
            if (entry.IfIndex is not { } ifIndex
                || ifIndex <= 0
                || !IPAddress.TryParse(entry.Address, out IPAddress? gateway))
            {
                continue;
            }

            yield return new NeighborObservation
            {
                Source = NeighborSource.Routing,
                LocalIfIndex = ifIndex,
                LocalInterfaceName = entry.LocalPortName,
                RemoteChassisId = gateway.ToString(),
                RemoteChassisIdKind = NeighborIdKind.NetworkAddress,
                RemoteManagementAddress = gateway,
                EvidenceCount = entry.RouteCount
            };
        }
    }

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

        outbox.Enlist(context, new DeviceAdjacencyChanged(
            match.DeviceId,
            hostname,
            TopologyWalkKind.Routes,
            applied.EdgeCount,
            applied.Added,
            applied.Withdrawn,
            match.Now));

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The walk result on the job row, or nothing if it does not read as one.</summary>
    private RouteWalkResult? Parse(CollectorJob job)
    {
        if (string.IsNullOrEmpty(job.Result))
        {
            return null;
        }

        try
        {
            RouteWalkResult? result = JsonSerializer.Deserialize(
                job.Result,
                TopologySerializerContext.Default.RouteWalkResult);

            return string.Equals(result?.Walk, RouteWalkParameters.WalkName, StringComparison.Ordinal)
                ? result
                : null;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "Collector job {JobId} carried a result that is not a route walk result.",
                job.Id);

            return null;
        }
    }
}
