namespace NetShield.Contracts.Inventory;

/// <summary>
/// One tracked client, as the client list shows it.
/// </summary>
/// <remarks>
/// A client is identified by its MAC address and by nothing else. An address is a lease and a
/// port is a cable; the hardware address is the one thing that stays the same across both, which
/// is why the history tables hang off it rather than the other way round.
/// </remarks>
/// <param name="Id">The client row.</param>
/// <param name="MacAddress">The MAC, as colon-separated uppercase hex.</param>
/// <param name="Oui">
/// The first three octets — the IEEE Organizationally Unique Identifier. The vendor *name* it
/// stands for is deliberately not resolved: the IEEE registry is a file with a licence, a size
/// and an update cadence that deserve a decision of their own rather than a casual commit.
/// </param>
/// <param name="LocallyAdministered">
/// Whether the address's local bit is set — a MAC somebody assigned rather than one a
/// manufacturer burned in, which is what MAC randomisation on a phone looks like, and the reason
/// an unresolvable <see cref="Oui"/> is not a gap in the registry.
/// </param>
/// <param name="Hostname">
/// What the client calls itself, when something has said. Nothing writes this in V1 — the two
/// sources that would, a DHCP lease and a wireless association, are declared and unproduced
/// (<see cref="ClientObservationSource"/>).
/// </param>
/// <param name="IpAddress">The address it holds now, or nothing if it holds none.</param>
/// <param name="DeviceId">The device whose port it is attached to now, if one reports it.</param>
/// <param name="DeviceHostname">That device's hostname.</param>
/// <param name="IfIndex">That device's own index for the port.</param>
/// <param name="VlanId">The VLAN the port learned it on, where the device says.</param>
/// <param name="FirstSeenAt">When any observation first named this MAC. UTC.</param>
/// <param name="LastSeenAt">When one last did. UTC.</param>
public sealed record ClientSummary(
    Guid Id,
    string MacAddress,
    string Oui,
    bool LocallyAdministered,
    string? Hostname,
    string? IpAddress,
    Guid? DeviceId,
    string? DeviceHostname,
    int? IfIndex,
    int? VlanId,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);
