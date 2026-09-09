using System.Text.Json.Serialization;

namespace NetShield.Inventory.Topology;

/// <summary>
/// What a VLAN walk found, as it is written into <c>collector_jobs.result</c>.
/// </summary>
/// <remarks>
/// <para>
/// The shape every walk since WP-1.5 has had, with the two flags that decide whether an absence
/// is allowed to mean anything. <see cref="VlansSupported"/> is false for a device that
/// implements neither Q-BRIDGE table — a router, an access point, anything that is not a bridge —
/// and the difference between that and "this bridge is configured with no VLANs" is the
/// difference between leaving a device's VLAN inventory alone and emptying it.
/// </para>
/// <para>
/// <see cref="VlansTruncated"/> carries the same weight: a reading that hit its ceiling saw part
/// of a table, so nothing may be withdrawn on the strength of its absence. WP-2.1 was the first
/// package to act on those two flags and this is the second.
/// </para>
/// </remarks>
/// <param name="Walk">The discriminator, matching <see cref="VlanWalkParameters.WalkName"/>.</param>
/// <param name="VlansSupported">Whether the device answered a VLAN table at all.</param>
/// <param name="VlanTable">Which of the two answered, where one did.</param>
/// <param name="VlanCount">How many VLANs it held, before the report ceiling.</param>
/// <param name="VlansTruncated">Whether more VLANs existed than the result carries.</param>
/// <param name="Vlans">The VLANs the device is configured with.</param>
internal sealed record VlanWalkResult(
    [property: JsonPropertyName("walk")] string? Walk,
    [property: JsonPropertyName("vlansSupported")] bool VlansSupported,
    [property: JsonPropertyName("vlanTable")] string? VlanTable,
    [property: JsonPropertyName("vlanCount")] int VlanCount,
    [property: JsonPropertyName("vlansTruncated")] bool VlansTruncated,
    [property: JsonPropertyName("vlans")] IReadOnlyList<VlanEntry>? Vlans);

/// <summary>One VLAN as one device is configured with it.</summary>
/// <param name="VlanId">The VLAN id, 1 to 4094.</param>
/// <param name="Name">What the device calls it. Absent from the nameless fallback table.</param>
/// <param name="IfIndexes">
/// Its member ports, already joined from the <c>PortList</c> bitmap's bridge ports back to
/// interface indexes by the collector — which is where the join belongs, since the evidence for
/// it is <c>dot1dBasePortTable</c> on the device.
/// </param>
/// <param name="UntaggedIfIndexes">The members that leave untagged.</param>
/// <param name="PortCount">How many bridge ports the bitmap named, before resolution.</param>
/// <param name="UnresolvedPortCount">How many of those the device could not place.</param>
internal sealed record VlanEntry(
    [property: JsonPropertyName("vlanId")] int VlanId,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("ifIndexes")] IReadOnlyList<int>? IfIndexes,
    [property: JsonPropertyName("untaggedIfIndexes")] IReadOnlyList<int>? UntaggedIfIndexes,
    [property: JsonPropertyName("portCount")] int PortCount,
    [property: JsonPropertyName("unresolvedPortCount")] int UnresolvedPortCount);
