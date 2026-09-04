namespace NetShield.Inventory.Collector.Contract;

/// <summary>The body of <c>POST /internal/collector/results</c>.</summary>
/// <remarks>
/// Batched, because a collector running many jobs at once should report them in one round trip,
/// and because a batch that is retried whole is the simplest thing for the collector to get
/// right — every report in it is idempotent by job id and lease token, so a replay changes
/// nothing.
/// </remarks>
/// <param name="Collector">Which collector is reporting, by the name it heartbeats under.</param>
/// <param name="Results">The reports.</param>
internal sealed record CollectorResultsRequest(string Collector, IReadOnlyList<CollectorResultReport> Results);
