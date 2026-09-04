namespace NetShield.Inventory.Collector.Contract;

/// <summary>
/// What the API did with each report in a submission.
/// </summary>
/// <remarks>
/// Three lists rather than a status code for the batch, because the three outcomes mean
/// different things to the collector: an accepted job is done, a duplicate was already done —
/// which is the answer to a retry and is not an error — and a rejected one is a job this
/// collector no longer holds and must stop working on. A batch in which everything was rejected
/// is still a <c>200</c>: the submission was understood, and what it said about each job is the
/// body.
/// </remarks>
/// <param name="Accepted">Jobs recorded by this submission.</param>
/// <param name="Duplicates">Jobs that already carried a result under this same lease token.</param>
/// <param name="Rejected">Jobs this submission could not be applied to, and why.</param>
internal sealed record CollectorResultsAck(
    IReadOnlyList<Guid> Accepted,
    IReadOnlyList<Guid> Duplicates,
    IReadOnlyList<CollectorRejectedResult> Rejected);
