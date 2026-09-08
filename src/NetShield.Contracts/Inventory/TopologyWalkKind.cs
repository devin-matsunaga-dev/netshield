using System.Text.Json.Serialization;

namespace NetShield.Contracts.Inventory;

/// <summary>
/// Which of the two topology reads a job is.
/// </summary>
/// <remarks>
/// They are separate walks, separate rows in <c>collector_jobs</c> and separate schedules on
/// purpose. LLDP and CDP are small, bounded and predictable; a routing table is none of the
/// three, and a device that answered its neighbour protocols and then timed out reading forty
/// thousand routes must not have the edges it already established discarded (WP-2.1).
///
/// Serialised as its name rather than its ordinal (WP-0.4).
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<TopologyWalkKind>))]
public enum TopologyWalkKind
{
    /// <summary>The LLDP remote table, and the CDP cache on a vendor that has one.</summary>
    Neighbors,

    /// <summary>The routing table, reduced to the gateways it forwards through.</summary>
    Routes
}
