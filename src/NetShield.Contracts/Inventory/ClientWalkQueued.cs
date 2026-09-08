namespace NetShield.Contracts.Inventory;

/// <summary>
/// The acknowledgement of an on-demand client walk: a job has been queued, and nothing has been
/// read yet.
/// </summary>
/// <remarks>
/// <c>202</c> rather than <c>200</c>, for the reason <see cref="DeviceWalkQueued"/> is: the API
/// schedules and the collector performs (ARCHITECTURE.md §7), and a route that blocked until a
/// collector had leased and finished the job would be the API talking to a device through a
/// proxy. The credential the job will be run with is on the job row and in the log, and is
/// deliberately not here — this route is gated on <c>DiscoveryRun</c>, which says nothing about
/// credentials.
/// </remarks>
/// <param name="JobId">The queued collector job.</param>
/// <param name="DeviceId">The device whose tables will be read.</param>
/// <param name="QueuedAt">When it was queued. UTC.</param>
public sealed record ClientWalkQueued(Guid JobId, Guid DeviceId, DateTimeOffset QueuedAt);
