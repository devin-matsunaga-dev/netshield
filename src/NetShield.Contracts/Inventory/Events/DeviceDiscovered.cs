using NetShield.Contracts.Messaging;

namespace NetShield.Contracts.Inventory.Events;

/// <summary>
/// A discovery run found an address nobody had seen before, and it is now a candidate.
/// </summary>
/// <remarks>
/// <para>
/// Named in ARCHITECTURE.md §5 as one of the events that travel without module coupling, and
/// unbuilt until this package because until this package nothing discovered anything.
/// </para>
/// <para>
/// <strong>It is not a device.</strong> A candidate is an address that answered a ping; SPEC.md
/// §2 puts it in front of an operator before it becomes inventory, and the criterion WP-1.6 is
/// held to is that results appear as reviewable candidates rather than auto-created devices. A
/// subscriber that wants to know when a device exists wants <c>DeviceCreated</c>, which
/// promotion publishes; this one says something answered where nothing was expected, which is a
/// different fact and arguably a more interesting one.
/// </para>
/// <para>
/// Published once per candidate, not once per sighting. A re-run that sees the same address
/// again refreshes the candidate and publishes nothing, for the reason
/// <c>DeviceCredentialProfilesChanged</c> is conditional: a subscriber should not be woken every
/// time the schedule confirms what it already knew.
/// </para>
/// </remarks>
/// <param name="CandidateId">The candidate that was created.</param>
/// <param name="Address">The address that answered.</param>
/// <param name="RunId">The discovery run that found it.</param>
/// <param name="SeedId">The seed that run was sweeping.</param>
/// <param name="DiscoveredAt">When the sweep's result was applied. UTC.</param>
public sealed record DeviceDiscovered(
    Guid CandidateId,
    string Address,
    Guid RunId,
    Guid SeedId,
    DateTimeOffset DiscoveredAt) : IIntegrationEvent;
