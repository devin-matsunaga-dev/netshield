using NetShield.Contracts.Messaging;

namespace NetShield.Contracts.Inventory.Events;

/// <summary>
/// The set of credential profiles assigned to a device was replaced.
/// </summary>
/// <remarks>
/// This is the event scheduling waits for. A device with no credential of a kind cannot be
/// polled or walked with it, and WP-1.6 decides what to schedule from what a device can be
/// reached with — so it needs to know the moment the answer changes, without polling Inventory
/// or reaching through its persistence boundary (ARCHITECTURE.md §4).
/// </remarks>
/// <param name="DeviceId">The device.</param>
/// <param name="CredentialProfileIds">The profiles it is assigned now, in id order. May be empty.</param>
public sealed record DeviceCredentialProfilesChanged(
    Guid DeviceId,
    IReadOnlyList<Guid> CredentialProfileIds) : IIntegrationEvent;
