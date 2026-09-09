namespace NetShield.Contracts.Inventory;

/// <summary>
/// One VLAN as one device is configured with it — the observation, before any aggregation.
/// </summary>
/// <remarks>
/// <para>
/// This is what a switch said about itself. <see cref="VlanSummary"/> is what NetShield concluded
/// from every switch that said something, and the two are deliberately different shapes: a VLAN
/// that lives on six switches has six of these and one of those.
/// </para>
/// <para>
/// <see cref="Name"/> is nullable because the fallback table carries none. Q-BRIDGE's
/// <c>dot1qVlanCurrentTable</c> is membership without names, and a VLAN read from it is a real
/// VLAN with a real port list that nobody has named.
/// </para>
/// </remarks>
/// <param name="DeviceId">The device that reported it.</param>
/// <param name="VlanId">The VLAN id, 1 to 4094.</param>
/// <param name="Name">What this device calls it, where it said.</param>
/// <param name="Ports">Its member ports on this device, ascending by interface index.</param>
/// <param name="PortCount">How many ports the device's own bitmap named.</param>
/// <param name="UnresolvedPortCount">
/// How many of those the device could not place against an interface. Reported rather than
/// hidden: a switch whose bridge-port table is unreadable is not a switch whose VLANs are empty.
/// </param>
/// <param name="ClientCount">
/// How many tracked clients this device currently reports on the VLAN — derived from the open
/// port bindings WP-1.8 writes, not stored here.
/// </param>
/// <param name="FirstDiscoveredAt">When this device was first seen carrying it. UTC.</param>
/// <param name="LastSeenAt">When a walk last confirmed it. UTC.</param>
public sealed record DeviceVlanSummary(
    Guid DeviceId,
    int VlanId,
    string? Name,
    IReadOnlyList<VlanPortMembership> Ports,
    int PortCount,
    int UnresolvedPortCount,
    int ClientCount,
    DateTimeOffset FirstDiscoveredAt,
    DateTimeOffset LastSeenAt);
