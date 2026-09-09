namespace NetShield.Contracts.Inventory;

/// <summary>
/// One thing on a port that announced itself — a device NetShield monitors, or an unmanaged
/// speaker of LLDP or CDP.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these is an edge: it comes from <c>device_adjacencies</c>, which is what
/// NetShield <em>concluded</em>, rather than from the raw observations behind it. An observation
/// WP-2.1's reconciler set aside — a CDP account of a port an LLDP account disagrees about — is
/// deliberately not here. It is evidence for a decision and it is still returned in full by
/// <c>GET /api/v1/devices/{id}/adjacencies</c>; showing it as an occupant would put a second
/// thing on a port that has one.
/// </para>
/// <para>
/// <see cref="Capabilities"/> is the part that cannot be got any other way. It is what the far
/// end said it is, decoded from the capability map its protocol carried, and for an access point
/// or a phone it is the difference between a name and an identification.
/// </para>
/// </remarks>
/// <param name="AdjacencyId">The edge, so a reader can go from the port to the whole of it.</param>
/// <param name="DeviceId">The far device, where NetShield monitors it.</param>
/// <param name="Managed">Whether the far end is a device NetShield monitors.</param>
/// <param name="Hostname">The far device's hostname, where it is one NetShield monitors.</param>
/// <param name="SystemName">What the far end calls itself, where it said.</param>
/// <param name="SystemDescription">What the far end says it is, in its own prose.</param>
/// <param name="ChassisId">The far end's identifier as it was advertised. Always present.</param>
/// <param name="ChassisIdKind">What kind of identifier that is.</param>
/// <param name="RemotePortName">
/// The far end's own port — its interface name where the port resolved against a monitored
/// device's inventory, and whatever it advertised otherwise.
/// </param>
/// <param name="Confidence">How much the edge is worth. Never absent: every row here is an edge.</param>
/// <param name="Bidirectional">Whether both ends reported the link, rather than only one.</param>
/// <param name="Sources">Which protocols reported it, in declaration order.</param>
/// <param name="Capabilities">
/// What the far end advertised itself as, in words. Empty where it advertised nothing, which is
/// what a routing-only edge and a great deal of older kit look like.
/// </param>
/// <param name="FirstDiscoveredAt">When the edge was first observed. UTC.</param>
/// <param name="LastSeenAt">When it was last observed. UTC.</param>
public sealed record PortNeighbor(
    Guid AdjacencyId,
    Guid? DeviceId,
    bool Managed,
    string? Hostname,
    string? SystemName,
    string? SystemDescription,
    string ChassisId,
    NeighborIdKind ChassisIdKind,
    string? RemotePortName,
    AdjacencyConfidence Confidence,
    bool Bidirectional,
    IReadOnlyList<NeighborSource> Sources,
    IReadOnlyList<SystemCapability> Capabilities,
    DateTimeOffset FirstDiscoveredAt,
    DateTimeOffset LastSeenAt);
