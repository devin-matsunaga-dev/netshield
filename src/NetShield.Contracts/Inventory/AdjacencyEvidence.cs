namespace NetShield.Contracts.Inventory;

/// <summary>
/// One protocol's account of an edge, as it appears beside the edge itself.
/// </summary>
/// <remarks>
/// An adjacency is a conclusion; this is the observation behind it. Both are shown because they
/// disagree in ways an operator has to be able to see: LLDP names a neighbour by a chassis
/// address and CDP by a host name, so an edge supported by both carries two spellings of one
/// far end, and an edge NetShield had to choose between carries the evidence for the choice.
/// </remarks>
/// <param name="ObservedByDeviceId">Which device reported it — either endpoint may have.</param>
/// <param name="Source">Which protocol reported it.</param>
/// <param name="LocalIfIndex">The interface on the observing device.</param>
/// <param name="RemoteChassisId">The far end's identifier, as this protocol spelled it.</param>
/// <param name="RemoteChassisIdKind">What kind of identifier that is.</param>
/// <param name="RemotePortId">The far end's port, as this protocol spelled it.</param>
/// <param name="RemoteSystemName">What the far end calls itself, where it said.</param>
/// <param name="RemoteManagementAddress">The management address it advertised, where it did.</param>
/// <param name="EvidenceCount">
/// How much of the evidence there was: the number of routes that named this gateway, for a
/// routing observation. Absent for a neighbour protocol, where an entry is an entry.
/// </param>
/// <param name="FirstDiscoveredAt">When this protocol first reported it. UTC.</param>
/// <param name="LastSeenAt">When it last reported it. UTC.</param>
public sealed record AdjacencyEvidence(
    Guid ObservedByDeviceId,
    NeighborSource Source,
    int LocalIfIndex,
    string RemoteChassisId,
    NeighborIdKind RemoteChassisIdKind,
    string? RemotePortId,
    string? RemoteSystemName,
    string? RemoteManagementAddress,
    int? EvidenceCount,
    DateTimeOffset FirstDiscoveredAt,
    DateTimeOffset LastSeenAt);
