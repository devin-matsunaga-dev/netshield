namespace NetShield.Contracts.Inventory;

/// <summary>
/// One VLAN across the whole estate: the conclusion, with the counts a VLAN tile reads.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A VLAN id identifies a VLAN.</strong> SPEC.md §1 describes a single estate and a VLAN
/// id is unique within one by convention, so two switches carrying VLAN 20 are carrying the same
/// VLAN and this row is what both of them add up to. Where they disagree about what to call it,
/// <see cref="NameDisputed"/> says so rather than the disagreement being hidden or the VLAN being
/// split in two.
/// </para>
/// <para>
/// <strong>The counts are derived, not stored.</strong> <see cref="DeviceCount"/> is how many
/// monitored devices carry it and <see cref="ClientCount"/> how many tracked endpoints are
/// currently on it, and both are computed when this is read: the second joins the open client
/// port bindings, which move on every client walk, so a stored copy could only ever be a stale
/// one.
/// </para>
/// </remarks>
/// <param name="VlanId">The VLAN id, 1 to 4094.</param>
/// <param name="Name">The name NetShield shows for it, chosen from what the devices said.</param>
/// <param name="NameDisputed">Whether the devices carrying it do not agree on that name.</param>
/// <param name="DeviceCount">How many monitored devices carry it.</param>
/// <param name="ClientCount">How many tracked clients are currently on it.</param>
/// <param name="PortCount">How many ports carry it, across every device.</param>
/// <param name="FirstDiscoveredAt">When any device was first seen carrying it. UTC.</param>
/// <param name="LastSeenAt">When any device last confirmed it. UTC.</param>
public sealed record VlanSummary(
    int VlanId,
    string? Name,
    bool NameDisputed,
    int DeviceCount,
    int ClientCount,
    int PortCount,
    DateTimeOffset FirstDiscoveredAt,
    DateTimeOffset LastSeenAt);
