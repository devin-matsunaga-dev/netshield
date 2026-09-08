using System.Text.Json.Serialization;

namespace NetShield.Contracts.Inventory;

/// <summary>
/// Where NetShield learned that a client held an address, or was attached to a port.
/// </summary>
/// <remarks>
/// <para>
/// SPEC.md §2 names four sources for endpoint tracking, and all four are members here because a
/// binding has to be able to say what its evidence was: an ARP entry and a DHCP lease are
/// different claims about the same fact, and an operator reading a handover needs to know which
/// one moved.
/// </para>
/// <para>
/// <strong>Two of the four have a producer and two do not.</strong> <see cref="ArpTable"/> and
/// <see cref="MacAddressTable"/> are plain SNMP reads of IP-MIB and BRIDGE-MIB, which every
/// SPEC.md §4 vendor implements, and WP-1.8 builds both. <see cref="DhcpLease"/> and
/// <see cref="WirelessAssociation"/> have no source in V1 that does not mean inventing per-vendor
/// MIB support the supported-vendor list does not cover — a DHCP server is not a network device
/// NetShield reaches at all, and no platform in SPEC.md §4 is a wireless controller. They are
/// declared and deliberately unproduced, so that the package which finds a real source for one
/// writes a producer rather than a migration.
/// </para>
/// <para>
/// Serialised as its name rather than its ordinal (WP-0.4), so inserting a member cannot
/// renumber what a stored response already meant.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ClientObservationSource>))]
public enum ClientObservationSource
{
    /// <summary>
    /// A router or L3 switch's ARP or neighbour cache — <c>ipNetToPhysicalTable</c>, or
    /// <c>ipNetToMediaTable</c> on an agent that implements only the older table. This is what
    /// binds an address to a MAC.
    /// </summary>
    ArpTable,

    /// <summary>
    /// A switch's forwarding database — <c>dot1qTpFdbTable</c>, or <c>dot1dTpFdbTable</c> on an
    /// agent that implements only the older table. This is what binds a MAC to a port and a VLAN.
    /// </summary>
    MacAddressTable,

    /// <summary>
    /// A DHCP server's lease table. Declared and unproduced: the realistic V1 source is a DHCP
    /// server's syslog, which is Phase 5's, and no SPEC.md §4 vendor answers a lease table over
    /// SNMP in a way NetShield could rely on.
    /// </summary>
    DhcpLease,

    /// <summary>
    /// A wireless controller or access point's association table. Declared and unproduced: it
    /// needs controller MIBs from platforms SPEC.md §4 does not list, and adding a vendor needs a
    /// work package that says to.
    /// </summary>
    WirelessAssociation
}
