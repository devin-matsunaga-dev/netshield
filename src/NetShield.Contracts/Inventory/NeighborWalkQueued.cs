namespace NetShield.Contracts.Inventory;

/// <summary>
/// The acknowledgement of an on-demand topology read: a job has been queued, and nothing has been
/// read yet.
/// </summary>
/// <remarks>
/// <c>202</c> rather than <c>200</c>, for the reason <see cref="DeviceWalkQueued"/> and
/// <see cref="ClientWalkQueued"/> are: the API schedules and the collector performs
/// (ARCHITECTURE.md §7). One shape serves both topology walks, because the two differ in what
/// they read and not in what queueing one means.
/// </remarks>
/// <param name="JobId">The queued collector job.</param>
/// <param name="DeviceId">The device that will be read.</param>
/// <param name="Walk">Which read was queued — the neighbour protocols, or the routing table.</param>
/// <param name="QueuedAt">When it was queued. UTC.</param>
public sealed record NeighborWalkQueued(
    Guid JobId,
    Guid DeviceId,
    TopologyWalkKind Walk,
    DateTimeOffset QueuedAt);
