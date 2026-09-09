namespace NetShield.Inventory.Topology;

/// <summary>
/// One VLAN a device reported, normalised, on its way into <c>device_vlans</c>.
/// </summary>
/// <remarks>
/// The shape <c>NeighborObservation</c> has: the payload's own type is what the collector wrote
/// and this is what the applier folds in, and keeping them apart is what lets the stored document
/// stay readable while the entity changes. Everything here has already been checked — the VLAN id
/// is in range, the port lists are sorted and de-duplicated, the name is trimmed or absent.
/// </remarks>
internal sealed record VlanObservation
{
    /// <summary>The VLAN id, 1 to 4094.</summary>
    public required int VlanId { get; init; }

    /// <summary>What the device calls it, where it said.</summary>
    public string? Name { get; init; }

    /// <summary>Its member ports, as interface indexes, ascending and distinct.</summary>
    public required IReadOnlyList<int> IfIndexes { get; init; }

    /// <summary>The members that leave untagged, ascending and distinct.</summary>
    public required IReadOnlyList<int> UntaggedIfIndexes { get; init; }

    /// <summary>How many bridge ports the device's bitmap named.</summary>
    public required int PortCount { get; init; }

    /// <summary>How many of those it could not place against an interface.</summary>
    public required int UnresolvedPortCount { get; init; }

    /// <summary>Which table it came from.</summary>
    public string? Source { get; init; }
}
