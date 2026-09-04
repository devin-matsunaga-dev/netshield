namespace NetShield.Contracts.Inventory;

/// <summary>
/// The answer to an on-demand discovery request: a run has been queued, not performed.
/// </summary>
/// <remarks>
/// The route answers <c>202</c> and this shape for the reason <see cref="DeviceWalkQueued"/>
/// does: the API schedules and a collector performs (ARCHITECTURE.md §7). A caller watches the
/// run, which is what <see cref="RunId"/> is for.
/// </remarks>
/// <param name="RunId">The run that was created.</param>
/// <param name="SeedId">The seed it will sweep.</param>
/// <param name="JobCount">How many sweep jobs it was split into.</param>
/// <param name="AddressCount">How many addresses those jobs will probe, after exclusions.</param>
/// <param name="QueuedAt">When the run was queued. UTC.</param>
public sealed record DiscoveryRunQueued(
    Guid RunId,
    Guid SeedId,
    int JobCount,
    long AddressCount,
    DateTimeOffset QueuedAt);
