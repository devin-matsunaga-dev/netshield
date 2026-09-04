using System.Text.Json;

using NetShield.Contracts.Collector;

namespace NetShield.Inventory.Collector.Contract;

/// <summary>What a collector says about one job it was leased.</summary>
/// <param name="JobId">The job being reported on.</param>
/// <param name="LeaseToken">The token the job was leased under.</param>
/// <param name="Outcome">Whether the work was done.</param>
/// <param name="Detail">
/// A sentence about how it ended, for a person reading the queue. Redacted on the way in: it is
/// written by the collector and is not trusted to be free of a credential (SPEC.md §5).
/// </param>
/// <param name="Data">
/// What was found, as JSON. WP-1.3 stores it and interprets none of it — the package that owns
/// each job kind reads it.
/// </param>
internal sealed record CollectorResultReport(
    Guid JobId,
    string LeaseToken,
    CollectorJobOutcome Outcome,
    string? Detail,
    JsonElement? Data);
