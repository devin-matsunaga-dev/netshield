namespace NetShield.Inventory.Collector.Contract;

/// <summary>One report the API could not apply, and the reason.</summary>
/// <param name="JobId">The job the report named.</param>
/// <param name="Reason">
/// A stable identifier the collector can branch on — <c>unknown-job</c>, <c>stale-lease</c> — not
/// a sentence to parse.
/// </param>
internal sealed record CollectorRejectedResult(Guid JobId, string Reason);
