using NetShield.Contracts.Messaging;

namespace NetShield.Contracts.Inventory.Events;

/// <summary>
/// A device was removed from the inventory. The row is soft-deleted rather than dropped
/// (CONVENTIONS.md §3), so telemetry already written against it keeps its reference.
/// </summary>
/// <param name="DeviceId">The device.</param>
/// <param name="Hostname">The name it was known by.</param>
/// <param name="PrimaryIpAddress">The address it was reached on, now free to be reused.</param>
public sealed record DeviceRemoved(Guid DeviceId, string Hostname, string PrimaryIpAddress)
    : IIntegrationEvent;
