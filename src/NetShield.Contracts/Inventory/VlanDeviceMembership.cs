namespace NetShield.Contracts.Inventory;

/// <summary>
/// One device's share of a VLAN, as it appears on the VLAN's own page.
/// </summary>
/// <remarks>
/// The same facts as <see cref="DeviceVlanSummary"/>, read from the other direction: that shape
/// answers "what VLANs is this switch carrying" and this one answers "which switches carry this
/// VLAN, and on what". Both exist because the aggregated view is the point of the package and a
/// caller should not have to page every device to assemble it.
/// </remarks>
/// <param name="DeviceId">The device.</param>
/// <param name="Hostname">Its hostname, joined from the live device row.</param>
/// <param name="Name">What this device calls the VLAN, where it said.</param>
/// <param name="Ports">Its member ports, ascending by interface index.</param>
/// <param name="PortCount">How many ports the device's own bitmap named.</param>
/// <param name="ClientCount">How many tracked clients this device reports on the VLAN.</param>
/// <param name="FirstDiscoveredAt">When this device was first seen carrying it. UTC.</param>
/// <param name="LastSeenAt">When a walk last confirmed it here. UTC.</param>
public sealed record VlanDeviceMembership(
    Guid DeviceId,
    string Hostname,
    string? Name,
    IReadOnlyList<VlanPortMembership> Ports,
    int PortCount,
    int ClientCount,
    DateTimeOffset FirstDiscoveredAt,
    DateTimeOffset LastSeenAt);
