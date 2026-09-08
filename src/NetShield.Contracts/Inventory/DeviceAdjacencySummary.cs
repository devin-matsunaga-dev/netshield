namespace NetShield.Contracts.Inventory;

/// <summary>
/// One edge of the topology graph: two endpoints, what supports the claim, and since when.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Undirected where it can be.</strong> When both ends are devices NetShield monitors and
/// each has resolved the other's port, the two devices' walks converge on one row rather than two
/// — which is what makes "the correct edge set" for a three-switch estate two edges and not four.
/// Where the far end is not a device, or where its port could not be resolved, the observing
/// device is the <c>A</c> end and the far end is described by whatever it advertised.
/// </para>
/// <para>
/// The endpoints are ordered canonically, not by who asked. A caller reading a device's own
/// adjacencies will find that device at either end, which is why both carry a hostname.
/// </para>
/// </remarks>
/// <param name="Id">The adjacency row.</param>
/// <param name="ADeviceId">One endpoint's device.</param>
/// <param name="ADeviceHostname">That device's hostname, where it is still live.</param>
/// <param name="AIfIndex">The interface on it.</param>
/// <param name="AInterfaceName">What that device calls the interface, where a walk has recorded one.</param>
/// <param name="BDeviceId">The far endpoint's device, when the far end is one NetShield monitors.</param>
/// <param name="BDeviceHostname">That device's hostname, where it is still live.</param>
/// <param name="BIfIndex">The interface on it, when it could be resolved.</param>
/// <param name="BInterfaceName">What that device calls the interface.</param>
/// <param name="BChassisId">The far end's identifier as it was advertised. Always present.</param>
/// <param name="BChassisIdKind">What kind of identifier that is.</param>
/// <param name="BPortId">The far end's port as it was advertised.</param>
/// <param name="BSystemName">What the far end calls itself.</param>
/// <param name="Sources">Every protocol supporting this edge, in declaration order.</param>
/// <param name="Confidence">How much the edge is worth, from the sources and from whether both ends reported it.</param>
/// <param name="Bidirectional">Whether both endpoints reported the link, rather than only one.</param>
/// <param name="FirstDiscoveredAt">When the edge was first observed. UTC.</param>
/// <param name="LastSeenAt">When it was last observed. UTC.</param>
/// <param name="Evidence">The per-protocol observations behind it.</param>
public sealed record DeviceAdjacencySummary(
    Guid Id,
    Guid ADeviceId,
    string? ADeviceHostname,
    int AIfIndex,
    string? AInterfaceName,
    Guid? BDeviceId,
    string? BDeviceHostname,
    int? BIfIndex,
    string? BInterfaceName,
    string BChassisId,
    NeighborIdKind BChassisIdKind,
    string? BPortId,
    string? BSystemName,
    IReadOnlyList<NeighborSource> Sources,
    AdjacencyConfidence Confidence,
    bool Bidirectional,
    DateTimeOffset FirstDiscoveredAt,
    DateTimeOffset LastSeenAt,
    IReadOnlyList<AdjacencyEvidence> Evidence);
