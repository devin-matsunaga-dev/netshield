namespace NetShield.Contracts.Inventory;

/// <summary>
/// One VLAN, with the aggregated membership that is the point of the package.
/// </summary>
/// <remarks>
/// <see cref="VlanSummary"/> with the devices behind it — which is the "appears once with
/// aggregated membership" half of the WP-2.2 criterion made readable: one row for the VLAN, and
/// under it every switch that carries it and the ports it carries it on.
///
/// <see cref="Names"/> is every distinct spelling the estate uses, so a disagreement can be
/// looked at rather than merely flagged. It holds one entry, or none, on an estate that agrees.
/// </remarks>
/// <param name="VlanId">The VLAN id, 1 to 4094.</param>
/// <param name="Name">The name NetShield shows for it.</param>
/// <param name="NameDisputed">Whether the devices carrying it do not agree on that name.</param>
/// <param name="Names">Every distinct name the estate uses for it, most used first.</param>
/// <param name="DeviceCount">How many monitored devices carry it.</param>
/// <param name="ClientCount">How many tracked clients are currently on it.</param>
/// <param name="PortCount">How many ports carry it, across every device.</param>
/// <param name="FirstDiscoveredAt">When any device was first seen carrying it. UTC.</param>
/// <param name="LastSeenAt">When any device last confirmed it. UTC.</param>
/// <param name="Devices">The devices carrying it, by hostname.</param>
public sealed record VlanDetail(
    int VlanId,
    string? Name,
    bool NameDisputed,
    IReadOnlyList<string> Names,
    int DeviceCount,
    int ClientCount,
    int PortCount,
    DateTimeOffset FirstDiscoveredAt,
    DateTimeOffset LastSeenAt,
    IReadOnlyList<VlanDeviceMembership> Devices);
