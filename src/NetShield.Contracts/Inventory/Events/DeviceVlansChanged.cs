using NetShield.Contracts.Messaging;

namespace NetShield.Contracts.Inventory.Events;

/// <summary>
/// A device's VLAN inventory changed: a VLAN appeared on it, or one was withdrawn from it.
/// </summary>
/// <remarks>
/// <para>
/// Published only when the set actually changed, for the reason <c>DeviceAdjacencyChanged</c> and
/// <c>DeviceFingerprinted</c> are conditional: a VLAN walk runs on every device on an interval,
/// and a subscriber should not be woken every time the schedule confirms what it already knew.
/// </para>
/// <para>
/// It carries counts and identifiers and no VLANs. An outbox row is readable by every module, and
/// a payload wide enough to save a query would put one switch's whole VLAN table in a column all
/// of them can read.
/// </para>
/// <para>
/// A membership change with no change to the *set* — a port added to a VLAN the device already
/// carried — does not publish. The event exists for "which VLANs exist and where", which is what
/// Phase 4's flow enrichment and WP-2.3's VLAN filter ask; a port list is read from the API when
/// something actually needs it.
/// </para>
/// </remarks>
/// <param name="DeviceId">The device whose VLAN inventory changed.</param>
/// <param name="Hostname">Its hostname, so a subscriber need not query for one.</param>
/// <param name="VlanCount">How many VLANs the device carries now.</param>
/// <param name="Added">How many appeared in this walk.</param>
/// <param name="Withdrawn">How many it withdrew.</param>
/// <param name="ObservedAt">When the walk was applied. UTC.</param>
public sealed record DeviceVlansChanged(
    Guid DeviceId,
    string Hostname,
    int VlanCount,
    int Added,
    int Withdrawn,
    DateTimeOffset ObservedAt) : IIntegrationEvent;
