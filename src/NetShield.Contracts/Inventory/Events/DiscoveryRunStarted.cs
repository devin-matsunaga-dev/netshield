using NetShield.Contracts.Messaging;

namespace NetShield.Contracts.Inventory.Events;

/// <summary>A discovery run has been queued, and its sweep jobs are in the collector queue.</summary>
/// <remarks>
/// It carries the seed's name as well as its id, for the reason <c>DeviceStateChanged</c>
/// carries a hostname: the subscribers this exists for are in other modules and none of them can
/// read the inventory tables to find out what to call the thing they are reporting on.
/// </remarks>
/// <param name="RunId">The run.</param>
/// <param name="SeedId">The seed it sweeps.</param>
/// <param name="SeedName">What that seed is called.</param>
/// <param name="Trigger">Whether the schedule started it or a person did.</param>
/// <param name="JobCount">How many sweep jobs it was split into.</param>
/// <param name="AddressCount">How many addresses those jobs will probe, after exclusions.</param>
/// <param name="StartedAt">When it was queued. UTC.</param>
public sealed record DiscoveryRunStarted(
    Guid RunId,
    Guid SeedId,
    string SeedName,
    DiscoveryRunTrigger Trigger,
    int JobCount,
    long AddressCount,
    DateTimeOffset StartedAt) : IIntegrationEvent;
