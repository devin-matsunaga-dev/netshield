namespace NetShield.Inventory.Topology;

/// <summary>
/// One VLAN as one device is configured with it, alive from when it was first seen until a
/// complete reading stopped containing it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the observation, and there is no conclusion table beside it.</strong> WP-2.1
/// split <c>device_neighbors</c> from <c>device_adjacencies</c> because an edge merges two
/// devices' accounts into one row carrying per-side state that cannot be recovered from either
/// account alone. A VLAN does not work that way: the estate-wide view is a grouping of these rows
/// by VLAN id, its device and port counts are sums over the group, and its client count is a join
/// against bindings that move on every client walk. Every one of those is derived on read
/// (WP-2.2), because a stored copy of a number that changes when a laptop is unplugged could only
/// ever be a stale one.
/// </para>
/// <para>
/// <strong>An absence withdraws, as it does for a neighbour table.</strong>
/// <c>dot1qVlanStaticTable</c> is the device's complete current statement about the VLANs it is
/// configured with, so a VLAN a supported, untruncated reading no longer contains has been
/// removed from that switch. The row survives with <see cref="WithdrawnAt"/> set, because "this
/// VLAN was on that switch until Tuesday" is worth being able to answer.
/// </para>
/// </remarks>
internal sealed class DeviceVlan
{
    /// <summary>UUID v7.</summary>
    public Guid Id { get; init; }

    /// <summary>The device that reported it.</summary>
    public Guid DeviceId { get; init; }

    /// <summary>The VLAN id, 1 to 4094. Unique per device among live rows.</summary>
    public int VlanId { get; init; }

    /// <summary>
    /// What this device calls it. Absent where the device answered only
    /// <c>dot1qVlanCurrentTable</c>, which carries membership and no names.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The member ports, as this device's own interface indexes, ascending.
    /// </summary>
    /// <remarks>
    /// An array column rather than a second table, the way <c>devices.tags</c> is one: a VLAN's
    /// membership is always read and written whole, and nothing in V1 asks the reverse question —
    /// "which VLANs is port 7 in" — that a row-per-port table would exist to answer.
    /// </remarks>
    public int[] IfIndexes { get; set; } = [];

    /// <summary>The subset that leaves untagged. A subset of <see cref="IfIndexes"/>.</summary>
    public int[] UntaggedIfIndexes { get; set; } = [];

    /// <summary>How many bridge ports the device's own bitmap named, before resolution.</summary>
    public int PortCount { get; set; }

    /// <summary>
    /// How many of those the device's bridge-port table could not place against an interface.
    /// </summary>
    /// <remarks>
    /// Kept because it separates two facts that look the same on a screen: a VLAN configured on
    /// no ports, and a VLAN whose ports the device would not name.
    /// </remarks>
    public int UnresolvedPortCount { get; set; }

    /// <summary>Which table the reading came from — the static one, or the fallback.</summary>
    public string? Source { get; set; }

    /// <summary>When this device was first seen carrying it. UTC.</summary>
    public DateTimeOffset FirstDiscoveredAt { get; init; }

    /// <summary>When a walk last confirmed it. UTC.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>When a complete reading stopped containing it, while it is live. UTC.</summary>
    public DateTimeOffset? WithdrawnAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
