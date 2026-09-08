namespace NetShield.Contracts.Inventory;

/// <summary>
/// One interface, as the last SNMP walk read it.
/// </summary>
/// <remarks>
/// Derived data that no operator edits: a walk that read the whole table removes the rows it did
/// not see, so an interface that has genuinely gone stops being listed.
/// <see cref="FirstSeenAt"/> survives across walks, which is what makes "this port appeared last
/// Tuesday" answerable.
/// </remarks>
/// <param name="Id">The interface row.</param>
/// <param name="IfIndex">The device's own index for it. Unique per device, and not stable across reboots on every platform.</param>
/// <param name="Name">``ifName``, where the device answers one.</param>
/// <param name="Description">``ifDescr``.</param>
/// <param name="Alias">``ifAlias`` — whatever an operator configured on the device itself.</param>
/// <param name="InterfaceType">``ifType``, as the IANA number. Nothing names them yet.</param>
/// <param name="Mtu">``ifMtu``.</param>
/// <param name="SpeedBitsPerSecond">
/// ``ifHighSpeed`` where the device answers one, ``ifSpeed`` otherwise. Absent when a saturated
/// 32-bit gauge had no high-speed counter beside it, because 4.29 Gbit/s for a 100G port is a
/// measurement rather than the absence of one.
/// </param>
/// <param name="PhysicalAddress">``ifPhysAddress``, as colon-separated hex.</param>
/// <param name="AdminStatus">What the interface is configured to do.</param>
/// <param name="OperStatus">What it is actually doing.</param>
/// <param name="FirstSeenAt">When a walk first saw this interface. UTC.</param>
/// <param name="LastSeenAt">When a walk last saw it. UTC.</param>
public sealed record DeviceInterfaceSummary(
    Guid Id,
    int IfIndex,
    string? Name,
    string? Description,
    string? Alias,
    int? InterfaceType,
    int? Mtu,
    long? SpeedBitsPerSecond,
    string? PhysicalAddress,
    InterfaceStatus AdminStatus,
    InterfaceStatus OperStatus,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);
