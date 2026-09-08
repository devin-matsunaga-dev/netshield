using NetShield.Contracts.Messaging;

namespace NetShield.Contracts.Inventory.Events;

/// <summary>
/// A MAC address NetShield had never seen before is now a tracked client.
/// </summary>
/// <remarks>
/// Published once per client, when the row is created — not once per sighting, for the reason
/// <c>DeviceDiscovered</c> and <c>DeviceFingerprinted</c> are conditional: a subscriber should
/// not be woken every time the schedule confirms what it already knew, and a switch's forwarding
/// database re-reports every client on it on every walk.
///
/// It carries identifiers and the address the client was first seen on, and never the entity
/// (ARCHITECTURE.md §4). Nothing subscribes yet; Phase 6's alerting is what a "new endpoint
/// appeared on the network" rule would hang from.
/// </remarks>
/// <param name="ClientId">The client.</param>
/// <param name="MacAddress">Its MAC, as colon-separated uppercase hex.</param>
/// <param name="IpAddress">The address it was first seen holding, if it was seen holding one.</param>
/// <param name="Source">The kind of observation that found it.</param>
/// <param name="FirstSeenAt">When it was found. UTC.</param>
public sealed record ClientDiscovered(
    Guid ClientId,
    string MacAddress,
    string? IpAddress,
    ClientObservationSource Source,
    DateTimeOffset FirstSeenAt) : IIntegrationEvent;
