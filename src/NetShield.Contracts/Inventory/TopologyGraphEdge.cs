namespace NetShield.Contracts.Inventory;

/// <summary>
/// One edge of the topology graph: two monitored devices and what supports the claim they are
/// connected.
/// </summary>
/// <remarks>
/// <para>
/// A narrower shape than <see cref="DeviceAdjacencySummary"/> deliberately. That one answers
/// "what is this switch connected to, and why does NetShield think so", and carries the
/// per-protocol evidence to prove it; this one is drawn on a canvas beside five hundred others,
/// and the evidence is one request away on the device's own route.
/// </para>
/// <para>
/// <strong>Both endpoints are always nodes of the graph, and may be on another page.</strong>
/// A page is a slice of the node ordering, so an edge is returned with the page holding either
/// of its endpoints and <see cref="ADeviceIncluded"/> and <see cref="BDeviceIncluded"/> say which
/// of them this page actually carries. An edge is therefore never dropped for straddling a page
/// boundary, and a client accumulating pages ends with each edge exactly once — it is returned
/// with the earlier of its two endpoints.
/// </para>
/// </remarks>
/// <param name="Id">The adjacency row, so this edge can be looked up in full.</param>
/// <param name="ADeviceId">One endpoint.</param>
/// <param name="AIfIndex">The interface on it.</param>
/// <param name="AInterfaceName">What that device calls the interface.</param>
/// <param name="BDeviceId">The other endpoint.</param>
/// <param name="BIfIndex">The interface on it, where it could be resolved.</param>
/// <param name="BInterfaceName">What that device calls the interface.</param>
/// <param name="Sources">Every protocol supporting this edge, in declaration order.</param>
/// <param name="Confidence">How much the edge is worth.</param>
/// <param name="Bidirectional">Whether both endpoints reported the link, rather than only one.</param>
/// <param name="ComponentIndex">The component both endpoints belong to. An edge never spans two.</param>
/// <param name="ADeviceIncluded">Whether this page carries the <c>A</c> node.</param>
/// <param name="BDeviceIncluded">Whether this page carries the <c>B</c> node.</param>
/// <param name="LastSeenAt">When the edge was last observed, from either end. UTC.</param>
public sealed record TopologyGraphEdge(
    Guid Id,
    Guid ADeviceId,
    int AIfIndex,
    string? AInterfaceName,
    Guid BDeviceId,
    int? BIfIndex,
    string? BInterfaceName,
    IReadOnlyList<NeighborSource> Sources,
    AdjacencyConfidence Confidence,
    bool Bidirectional,
    int ComponentIndex,
    bool ADeviceIncluded,
    bool BDeviceIncluded,
    DateTimeOffset LastSeenAt);
