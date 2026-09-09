namespace NetShield.Inventory.Topology;

/// <summary>
/// The column widths the topology tables and their handlers agree on, in one place so that a
/// value the collector can report cannot be one the database refuses.
/// </summary>
/// <remarks>
/// Everything here is a length rather than a policy. A device advertises whatever its operator
/// typed, and an over-long value is truncated on the way in rather than failing the walk — losing
/// the tail of a system description is a smaller harm than losing the edge it came with.
/// </remarks>
internal static class TopologyLimits
{
    /// <summary>The longest chassis or device identifier stored. Generous: a local(7) id is free text.</summary>
    internal const int IdentifierLength = 255;

    /// <summary>The longest port identifier stored.</summary>
    internal const int PortIdLength = 255;

    /// <summary>The longest system or interface name stored. The width <c>devices.hostname</c> uses.</summary>
    internal const int NameLength = 255;

    /// <summary>The longest description stored. A <c>sysDescr</c> is routinely a paragraph.</summary>
    internal const int DescriptionLength = 1_024;

    /// <summary>The longest remote or adjacency key. Built from an identifier and a port.</summary>
    internal const int KeyLength = 600;

    /// <summary>The longest routing-table name recorded on a scan row.</summary>
    internal const int TableNameLength = 64;

    /// <summary>The longest error recorded on a scan row.</summary>
    internal const int ErrorLength = 1_024;

    /// <summary>The value stored when a device advertised no port identifier at all.</summary>
    internal const string UnknownPort = "";

    /// <summary>
    /// The most member ports one VLAN row will hold.
    /// </summary>
    /// <remarks>
    /// A chassis switch can carry a VLAN on several hundred ports; a list far past that is an
    /// agent contradicting itself, and it is cut rather than the walk failed — the rule at the
    /// top of this file.
    /// </remarks>
    internal const int MaxPortsPerVlan = 1_024;

    /// <summary>The lowest VLAN id IEEE 802.1Q admits.</summary>
    internal const int MinVlanId = 1;

    /// <summary>The highest. <c>VlanIndex</c> in Q-BRIDGE-MIB is <c>1..4094</c>.</summary>
    internal const int MaxVlanId = 4_094;
}
