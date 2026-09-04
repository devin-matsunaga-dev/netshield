namespace NetShield.Inventory.Collector.Contract;

/// <summary>
/// The answer to <c>GET /internal/collector/jobs</c>: what was leased, and how long the lease is.
/// </summary>
/// <remarks>
/// The batch carries the lease duration rather than leaving the collector to work it out from
/// the expiry timestamps, because a batch may be empty and the collector still needs to know how
/// long a lease will be before it decides what to start. The API owns scheduling
/// (ARCHITECTURE.md §7), so every number the collector paces itself by comes from the API.
/// </remarks>
/// <param name="Jobs">The leased jobs, oldest due first. Empty when there is nothing to do.</param>
/// <param name="LeaseSeconds">How long a lease lasts.</param>
internal sealed record CollectorJobBatch(IReadOnlyList<CollectorJobLease> Jobs, int LeaseSeconds);
