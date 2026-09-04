using NetShield.Contracts.Messaging;

namespace NetShield.Contracts.Inventory.Events;

/// <summary>
/// An SNMP walk established what a device is, and something about it had changed.
/// </summary>
/// <remarks>
/// <para>
/// Published when a walk changed one of the identity facts below, or when the set of interfaces
/// on the device changed. A walk that found the device exactly as it was recorded publishes
/// nothing — the same rule <c>DeviceCredentialProfilesChanged</c> follows, and for the same
/// reason: a subscriber rebuilding a cache from this event should not rebuild it every time
/// somebody re-walks an unchanged switch. An interface merely changing operational status is
/// deliberately not a change here; that is telemetry, and Phase 3 owns it.
/// </para>
/// <para>
/// It carries the hostname as well as the id, for the reason <c>DeviceStateChanged</c> does: the
/// subscribers this exists for are in other modules — Phase 2's topology, Phase 6's alerting —
/// and none of them can read the inventory table to find out what to call the device.
/// </para>
/// <para>
/// It carries no interface list. An outbox row is readable by every module, and a payload wide
/// enough to save a query would put a device's whole interface table in a column all of them can
/// read — the reasoning WP-1.3 settled for <c>CollectorJobCompleted</c>.
/// </para>
/// </remarks>
/// <param name="DeviceId">The device.</param>
/// <param name="Hostname">What it is called, as the inventory holds it.</param>
/// <param name="Vendor">The platform the walk resolved.</param>
/// <param name="ReducedCapability">
/// Whether it landed on the generic-SNMP fallback, and so has the reduced feature set SPEC.md §4
/// requires to be labelled.
/// </param>
/// <param name="Model">The hardware model, when known.</param>
/// <param name="OsVersion">The running software version, when known.</param>
/// <param name="SerialNumber">The chassis serial, when known.</param>
/// <param name="InterfaceCount">How many interfaces the walk found.</param>
/// <param name="ObservedAt">When the walk's result was applied. UTC.</param>
public sealed record DeviceFingerprinted(
    Guid DeviceId,
    string Hostname,
    DeviceVendor Vendor,
    bool ReducedCapability,
    string? Model,
    string? OsVersion,
    string? SerialNumber,
    int InterfaceCount,
    DateTimeOffset ObservedAt) : IIntegrationEvent;
