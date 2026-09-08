using NetShield.Contracts.Messaging;

namespace NetShield.Contracts.Inventory.Events;

/// <summary>
/// A device's set of edges changed: something appeared, something was withdrawn, or an edge's
/// confidence moved.
/// </summary>
/// <remarks>
/// <para>
/// Published only when the edge set actually changed, for the reason
/// <c>DeviceFingerprinted</c> and <c>DeviceCredentialProfilesChanged</c> are conditional: a
/// subscriber rebuilding a graph should not be woken every time the schedule confirms what it
/// already knew, and a topology walk runs on every device on an interval.
/// </para>
/// <para>
/// It carries counts and identifiers and no edges. An outbox row is readable by every module, and
/// a payload wide enough to save a query would put one device's whole neighbour table in a column
/// all of them can read — the same reasoning that kept the walk result off
/// <c>CollectorJobCompleted</c>.
/// </para>
/// <para>
/// Phase 6's topology-aware suppression is what this is for: "a core switch failed, so do not
/// raise an alert for each of the forty devices behind it" needs to know when the graph moved.
/// </para>
/// </remarks>
/// <param name="DeviceId">The device whose edges changed.</param>
/// <param name="Hostname">Its hostname, so a subscriber in another module need not query for one.</param>
/// <param name="Source">Which walk established the change.</param>
/// <param name="EdgeCount">How many live edges the device has now.</param>
/// <param name="Added">How many appeared in this walk.</param>
/// <param name="Withdrawn">How many were withdrawn by it.</param>
/// <param name="ObservedAt">When the walk was applied. UTC.</param>
public sealed record DeviceAdjacencyChanged(
    Guid DeviceId,
    string Hostname,
    TopologyWalkKind Source,
    int EdgeCount,
    int Added,
    int Withdrawn,
    DateTimeOffset ObservedAt) : IIntegrationEvent;
