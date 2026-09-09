using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Topology;

/// <summary>
/// Turns the topology entities into the shapes that leave the module. The one place the boundary
/// in ARCHITECTURE.md §4 is crossed for this feature, and the reason nothing outside it needs to
/// see <see cref="DeviceAdjacency"/>, <see cref="DeviceNeighbor"/>, <see cref="DeviceVlan"/> or
/// <see cref="DeviceTopologyScan"/>.
/// </summary>
internal static class TopologyMapping
{
    /// <summary>
    /// An edge, with the hostnames of whichever endpoints are still live devices and the
    /// observations behind it.
    /// </summary>
    /// <remarks>
    /// The two source lists are unioned here rather than in the database, because each side's
    /// walk writes only its own — see <see cref="DeviceAdjacency.SourcesA"/> for why one shared
    /// list would be overwritten with half its contents on every walk.
    /// </remarks>
    internal static DeviceAdjacencySummary ToSummary(
        this DeviceAdjacency edge,
        string? aHostname,
        string? bHostname,
        IReadOnlyList<AdjacencyEvidence> evidence) =>
        new(
            edge.Id,
            edge.ADeviceId,
            aHostname,
            edge.AIfIndex,
            edge.AInterfaceName,
            edge.BDeviceId,
            bHostname,
            edge.BIfIndex,
            edge.BInterfaceName,
            edge.BChassisId,
            edge.BChassisIdKind,
            edge.BPortId,
            edge.BSystemName,
            Sources(edge),
            edge.Confidence,
            edge.ObservedFromA && edge.ObservedFromB,
            edge.FirstDiscoveredAt,
            edge.LastSeenAt,
            evidence);

    /// <summary>
    /// An edge as the graph draws it: both endpoints, what supports it, and which of them the
    /// page carries.
    /// </summary>
    /// <remarks>
    /// Narrower than <see cref="ToSummary"/> on purpose. The device route's shape answers "why
    /// does NetShield think these two are connected" and carries the per-protocol evidence to
    /// prove it; this one is drawn beside five hundred others, and the evidence is one request
    /// away on <see cref="DeviceAdjacencySummary"/>. Only an edge whose far end resolved to a
    /// device reaches here, which is why <c>BDeviceId</c> is not nullable on the graph shape.
    /// </remarks>
    internal static TopologyGraphEdge ToGraphEdge(
        this DeviceAdjacency edge,
        int componentIndex,
        bool aIncluded,
        bool bIncluded) =>
        new(
            edge.Id,
            edge.ADeviceId,
            edge.AIfIndex,
            edge.AInterfaceName,
            edge.BDeviceId!.Value,
            edge.BIfIndex,
            edge.BInterfaceName,
            Sources(edge),
            edge.Confidence,
            edge.ObservedFromA && edge.ObservedFromB,
            componentIndex,
            aIncluded,
            bIncluded,
            edge.LastSeenAt);

    internal static AdjacencyEvidence ToEvidence(this DeviceNeighbor row) =>
        new(
            row.DeviceId,
            row.Source,
            row.LocalIfIndex,
            row.RemoteChassisId,
            row.RemoteChassisIdKind,
            row.RemotePortId,
            row.RemoteSystemName,
            row.RemoteManagementAddress?.ToString(),
            row.EvidenceCount,
            row.FirstDiscoveredAt,
            row.LastSeenAt);

    internal static DeviceTopologyScanDetail ToDetail(this DeviceTopologyScan scan) =>
        new(
            scan.DeviceId,
            scan.NextNeighborWalkAt,
            scan.LastNeighborWalkAt,
            scan.LldpSupported,
            scan.CdpSupported,
            scan.LastLldpCount,
            scan.LastCdpCount,
            scan.LastNeighborError,
            scan.NextRouteWalkAt,
            scan.LastRouteWalkAt,
            scan.RoutingSupported,
            scan.RouteTable,
            scan.LastRouteCount,
            scan.LastNextHopCount,
            scan.LastRouteError,
            scan.NextVlanWalkAt,
            scan.LastVlanWalkAt,
            scan.VlansSupported,
            scan.VlanTable,
            scan.LastVlanCount,
            scan.LastVlanError);

    /// <summary>Every protocol supporting the edge, from either end, in declaration order.</summary>
    private static IReadOnlyList<NeighborSource> Sources(DeviceAdjacency edge)
    {
        HashSet<NeighborSource> sources = [];

        foreach (string name in edge.SourcesA.Concat(edge.SourcesB))
        {
            if (Enum.TryParse(name, out NeighborSource source))
            {
                sources.Add(source);
            }
        }

        return [.. sources.OrderBy(source => source)];
    }
}
