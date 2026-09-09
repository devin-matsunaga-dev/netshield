using System.Text.Json.Serialization;

namespace NetShield.Contracts.Inventory;

/// <summary>
/// Which of the three topology reads a job is.
/// </summary>
/// <remarks>
/// They are separate walks, separate rows in <c>collector_jobs</c> and separate schedules on
/// purpose. LLDP and CDP are small, bounded and predictable; a routing table is none of the
/// three, and a device that answered its neighbour protocols and then timed out reading forty
/// thousand routes must not have the edges it already established discarded (WP-2.1). The VLAN
/// read is separated from both on the same grounds and one more (WP-2.2): what a device is cabled
/// to changes when somebody re-cables it, and which VLANs it carries changes when somebody
/// re-designs the network, so the two do not deserve the same interval.
///
/// Serialised as its name rather than its ordinal (WP-0.4).
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<TopologyWalkKind>))]
public enum TopologyWalkKind
{
    /// <summary>The LLDP remote table, and the CDP cache on a vendor that has one.</summary>
    Neighbors,

    /// <summary>The routing table, reduced to the gateways it forwards through.</summary>
    Routes,

    /// <summary>The Q-BRIDGE VLAN tables, and the port membership bitmaps beside them.</summary>
    Vlans
}
