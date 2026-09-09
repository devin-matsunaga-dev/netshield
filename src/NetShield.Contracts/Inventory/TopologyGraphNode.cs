namespace NetShield.Contracts.Inventory;

/// <summary>
/// One node of the topology graph: a monitored device, its state, and where to draw it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A node is a device.</strong> An edge whose far end is not a device NetShield monitors
/// has nothing to draw at that end — no state, no site, no VLAN, and therefore nothing any of the
/// filters could match — so it is counted on this node as
/// <see cref="ExternalEdgeCount"/> rather than being invented as a node with no facts behind it.
/// The evidence for it is still on <c>GET /api/v1/devices/{id}/adjacencies</c>, which returns
/// every edge whatever its far end.
/// </para>
/// <para>
/// <see cref="State"/> is <c>devices.state</c>, which the reachability work in WP-1.4 owns, and
/// is what <c>DESIGN.md</c> §6 encodes as the node tile's border colour.
/// </para>
/// </remarks>
/// <param name="DeviceId">The device this node is.</param>
/// <param name="Hostname">What it is called.</param>
/// <param name="Vendor">The platform, where something has identified it.</param>
/// <param name="Role">What the device is for.</param>
/// <param name="Site">Where it is, where an operator has said.</param>
/// <param name="State">Whether it is answering.</param>
/// <param name="ComponentIndex">Which connected component of this graph it belongs to.</param>
/// <param name="Rank">How many hops it is from its component's root. The root is rank 0.</param>
/// <param name="Degree">How many edges of this graph are incident to it.</param>
/// <param name="ExternalEdgeCount">How many of its live edges lead to something that is not a monitored device.</param>
/// <param name="X">Where to draw it, in the units <see cref="TopologyGraph.Layout"/> describes.</param>
/// <param name="Y">The same, vertically. Rank 0 is at the top.</param>
public sealed record TopologyGraphNode(
    Guid DeviceId,
    string Hostname,
    DeviceVendor Vendor,
    DeviceRole Role,
    string? Site,
    DeviceState State,
    int ComponentIndex,
    int Rank,
    int Degree,
    int ExternalEdgeCount,
    double X,
    double Y);
