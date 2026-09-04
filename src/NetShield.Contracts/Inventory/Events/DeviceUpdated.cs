using NetShield.Contracts.Messaging;

namespace NetShield.Contracts.Inventory.Events;

/// <summary>A device's attributes changed.</summary>
/// <remarks>
/// <paramref name="PreviousPrimaryIpAddress"/> is what lets a subscriber invalidate a cache keyed
/// by address without holding its own copy of the inventory — the address resolution in WP-1.8
/// is the first thing that will need it. It equals <paramref name="PrimaryIpAddress"/> when the
/// address did not change.
/// </remarks>
/// <param name="DeviceId">The device.</param>
/// <param name="Hostname">The name it is now known by.</param>
/// <param name="PrimaryIpAddress">The address it is now reached on.</param>
/// <param name="PreviousPrimaryIpAddress">The address it was reached on before this change.</param>
public sealed record DeviceUpdated(
    Guid DeviceId,
    string Hostname,
    string PrimaryIpAddress,
    string PreviousPrimaryIpAddress) : IIntegrationEvent;
