namespace NetShield.Contracts.Inventory;

/// <summary>
/// What NetShield knows about reading one device's topology, and when it will ask again.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as <see cref="DeviceReachabilityDetail"/>, and there for the same reason: an
/// operator looking at a device with no edges has to be able to tell "this device answered
/// nothing" from "nobody asked it". The four <c>Supported</c> members are that distinction — an
/// access switch implements no routing table, a non-Cisco no CDP cache, a router no VLAN table —
/// and they are <c>null</c> until a walk has actually reported.
/// </para>
/// <para>
/// The three walks are separate rows in <c>collector_jobs</c> and are reported separately here.
/// A routing table that times out costs the L3 half and leaves the LLDP and CDP edges standing,
/// which is the whole reason they are separate walks.
/// </para>
/// </remarks>
/// <param name="DeviceId">The device this is about.</param>
/// <param name="NextNeighborWalkAt">The earliest the next LLDP and CDP read will be queued. UTC.</param>
/// <param name="LastNeighborWalkAt">When a neighbour walk last reported, successfully or not. UTC.</param>
/// <param name="LldpSupported">Whether the device answered an LLDP remote table at the last walk.</param>
/// <param name="CdpSupported">Whether it answered a CDP cache.</param>
/// <param name="LastLldpCount">How many LLDP neighbours the last walk read.</param>
/// <param name="LastCdpCount">How many CDP neighbours it read.</param>
/// <param name="LastNeighborError">Why the last neighbour walk could not be performed, where it could not.</param>
/// <param name="NextRouteWalkAt">The earliest the next routing-table read will be queued. UTC.</param>
/// <param name="LastRouteWalkAt">When a route walk last reported. UTC.</param>
/// <param name="RoutingSupported">Whether the device answered a routing table at all.</param>
/// <param name="RouteTable">Which of the three tables answered, where one did.</param>
/// <param name="LastRouteCount">How many routes that table held.</param>
/// <param name="LastNextHopCount">How many distinct gateways they reduced to.</param>
/// <param name="LastRouteError">Why the last route walk could not be performed, where it could not.</param>
/// <param name="NextVlanWalkAt">The earliest the next VLAN read will be queued. UTC.</param>
/// <param name="LastVlanWalkAt">When a VLAN walk last reported. UTC.</param>
/// <param name="VlansSupported">Whether the device answered a Q-BRIDGE VLAN table at all.</param>
/// <param name="VlanTable">Which of the two tables answered, where one did.</param>
/// <param name="LastVlanCount">How many VLANs it held.</param>
/// <param name="LastVlanError">Why the last VLAN walk could not be performed, where it could not.</param>
public sealed record DeviceTopologyScanDetail(
    Guid DeviceId,
    DateTimeOffset NextNeighborWalkAt,
    DateTimeOffset? LastNeighborWalkAt,
    bool? LldpSupported,
    bool? CdpSupported,
    int? LastLldpCount,
    int? LastCdpCount,
    string? LastNeighborError,
    DateTimeOffset NextRouteWalkAt,
    DateTimeOffset? LastRouteWalkAt,
    bool? RoutingSupported,
    string? RouteTable,
    int? LastRouteCount,
    int? LastNextHopCount,
    string? LastRouteError,
    DateTimeOffset NextVlanWalkAt,
    DateTimeOffset? LastVlanWalkAt,
    bool? VlansSupported,
    string? VlanTable,
    int? LastVlanCount,
    string? LastVlanError);
