using System.Text.Json.Serialization;

namespace NetShield.Contracts.Inventory;

/// <summary>
/// Where NetShield learned that two things are next to each other.
/// </summary>
/// <remarks>
/// <para>
/// SPEC.md §2 builds the L2/L3 adjacency graph from "LLDP/CDP + ARP + routing tables". These are
/// the three this package produces; the ARP half is WP-1.8's and answers a different question —
/// which endpoint holds an address — rather than which device is on the other end of a cable.
/// </para>
/// <para>
/// <strong>They are not equally good evidence, and the reconciler knows it.</strong>
/// <see cref="Lldp"/> carries a chassis identifier, which is an identity. <see cref="Cdp"/>
/// carries a device id, which is usually a host name — and WP-1.1 settled that a host name is not
/// an identity, because DHCP naming, cloned systems and reused defaults all produce duplicates.
/// So where the two disagree about one port, LLDP is the one that wins. <see cref="Routing"/> is
/// weaker still: a next hop says two devices are one hop apart at layer 3, which need not be one
/// cable.
/// </para>
/// <para>
/// Serialised as its name rather than its ordinal (WP-0.4), so inserting a member cannot renumber
/// what a stored response already meant. Pinned to the collector's own constants by
/// <c>VendorParityTests</c>.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<NeighborSource>))]
public enum NeighborSource
{
    /// <summary>
    /// IEEE 802.1AB, read from <c>lldpRemTable</c>. The standard protocol, implemented by every
    /// platform SPEC.md §4 names, and the primary source for adjacency.
    /// </summary>
    Lldp,

    /// <summary>
    /// Cisco Discovery Protocol, read from <c>cdpCacheTable</c> in Cisco's private MIB. Asked for
    /// on Cisco platforms alone, because reading a vendor's private MIB from another vendor's
    /// device is a guess the vendor seam exists to prevent.
    /// </summary>
    Cdp,

    /// <summary>
    /// A next hop in the device's routing table — the L3 adjacency SPEC.md §2 asks for. It says
    /// the two are one hop apart at layer 3, which is weaker than a cable: a gateway reached
    /// through a switch NetShield also monitors is still one hop away.
    /// </summary>
    Routing
}
