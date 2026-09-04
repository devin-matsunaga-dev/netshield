using NetShield.Contracts.Messaging;

namespace NetShield.Contracts.Inventory.Events;

/// <summary>A device was added to the inventory.</summary>
/// <remarks>
/// It carries identifiers and the two attributes a subscriber is most likely to need to act
/// without a second query — never the entity (ARCHITECTURE.md §4).
/// </remarks>
/// <param name="DeviceId">The device.</param>
/// <param name="Hostname">The name it was added under.</param>
/// <param name="PrimaryIpAddress">The address it was added with.</param>
public sealed record DeviceCreated(Guid DeviceId, string Hostname, string PrimaryIpAddress)
    : IIntegrationEvent;
