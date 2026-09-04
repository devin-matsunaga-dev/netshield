using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NetShield.Contracts.Collector;
using NetShield.Contracts.Collector.Events;

using NetShield.Inventory.Collector.Contract;
using NetShield.Inventory.Persistence;

using NetShield.Platform.Logging;
using NetShield.Platform.Messaging;
using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Collector.Handlers;

/// <summary>
/// Records what a collector found, once per job, however many times it is told.
/// </summary>
/// <remarks>
/// <para>
/// Idempotency is by job id <em>and</em> lease token, not by job id alone. A collector that
/// retries a submission it never saw the answer to must change nothing the second time, and a
/// collector whose lease expired while it was working must not be able to overwrite the result
/// of whoever picked the job up next. The lease token is what tells those two cases apart: the
/// first presents the token the job still carries, the second presents one it no longer does.
/// </para>
/// <para>
/// The whole batch is one transaction, and each completed job enlists its
/// <c>CollectorJobCompleted</c> row into it (ARCHITECTURE.md §5). A subscriber therefore never
/// sees an event for a result that was not stored, and a stored result always has an event to
/// go with it.
/// </para>
/// <para>
/// Nothing here reads the payload. WP-1.3 defines no job kind, so interpreting a result would be
/// interpreting a shape that does not exist yet; the packages that own each kind subscribe to
/// the event and read the row.
/// </para>
/// </remarks>
internal sealed class SubmitCollectorResultsHandler(
    InventoryDbContext context,
    OutboxEnlistment outbox,
    SecretRedactor redactor,
    IClock clock,
    ILogger<SubmitCollectorResultsHandler> logger)
{
    public async Task<Result<CollectorResultsAck>> HandleAsync(
        CollectorResultsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Results.Count > CollectorLimits.MaxResultsPerSubmission)
        {
            return Result<CollectorResultsAck>.Failure(
                CollectorErrors.TooManyResults(CollectorLimits.MaxResultsPerSubmission));
        }

        List<Guid> accepted = [];
        List<Guid> duplicates = [];
        List<CollectorRejectedResult> rejected = [];

        // One read for the batch. A collector reporting twenty jobs should not cost twenty round
        // trips, and the rows are about to be changed so they are tracked rather than not.
        List<Guid> jobIds = [.. request.Results.Select(result => result.JobId).Distinct()];

        Dictionary<Guid, CollectorJob> jobs = await context.CollectorJobs
            .Where(job => jobIds.Contains(job.Id))
            .ToDictionaryAsync(job => job.Id, cancellationToken);

        DateTimeOffset now = clock.UtcNow;

        foreach (CollectorResultReport report in request.Results)
        {
            if (!jobs.TryGetValue(report.JobId, out CollectorJob? job))
            {
                rejected.Add(new CollectorRejectedResult(report.JobId, CollectorErrors.UnknownJobReason));
                continue;
            }

            if (!string.Equals(job.LeaseToken, report.LeaseToken, StringComparison.Ordinal))
            {
                // Either the lease expired and the job has since been re-leased, or the token is
                // wrong. Both mean this collector no longer holds the job, and both are the
                // collector's cue to stop working on it.
                logger.LogInformation(
                    "A result for collector job {JobId} was refused: the lease token is not the current one.",
                    report.JobId);

                rejected.Add(new CollectorRejectedResult(report.JobId, CollectorErrors.StaleLeaseReason));
                continue;
            }

            if (job.IsComplete)
            {
                // The token matches and the job is already finished, so this is the same report
                // arriving again. Nothing is written and it is not an error — a retry that had
                // to be told it had failed would be a retry no collector could safely make.
                duplicates.Add(report.JobId);
                continue;
            }

            string? data = report.Data?.GetRawText();

            if (data is { Length: > CollectorLimits.ResultLength })
            {
                // Refused rather than truncated. A payload cut in half is a payload that parses
                // into something wrong, and the packages that read these have no way to tell.
                rejected.Add(new CollectorRejectedResult(report.JobId, CollectorErrors.ResultTooLargeReason));
                continue;
            }

            Complete(job, report, data, now);
            accepted.Add(report.JobId);
        }

        if (accepted.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return new CollectorResultsAck(accepted, duplicates, rejected);
    }

    private void Complete(CollectorJob job, CollectorResultReport report, string? data, DateTimeOffset now)
    {
        job.Status = report.Outcome == CollectorJobOutcome.Succeeded
            ? CollectorJobStatus.Succeeded
            : CollectorJobStatus.Failed;

        job.Outcome = report.Outcome;

        // The detail is free text a remote process wrote, and a device error message is exactly
        // where a community string or a URL with a password in it ends up. It goes through the
        // same redactor as a log line and an audit snapshot before it is stored (SPEC.md §5).
        job.Detail = Truncate(Redact(report.Detail));

        job.Result = data;
        job.CompletedAt = now;

        // The lease token stays on the row. It is what makes the next submission recognisable as
        // a duplicate rather than as a stale lease.
        job.LeasedUntil = null;
        job.UpdatedAt = now;

        outbox.Enlist(
            context,
            new CollectorJobCompleted(job.Id, job.Kind, job.DeviceId, report.Outcome, now));
    }

    private string? Redact(string? detail) =>
        string.IsNullOrWhiteSpace(detail) ? null : redactor.RedactText(detail);

    private static string? Truncate(string? detail) =>
        detail is { Length: > CollectorLimits.DetailLength }
            ? detail[..CollectorLimits.DetailLength]
            : detail;
}
