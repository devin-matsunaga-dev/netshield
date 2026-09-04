using System.Globalization;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NetShield.Contracts.Collector;
using NetShield.Contracts.Collector.Events;

using NetShield.Inventory.Collector.Contract;
using NetShield.Inventory.Credentials;
using NetShield.Inventory.Devices;
using NetShield.Inventory.Persistence;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authentication;
using NetShield.Platform.Logging;
using NetShield.Platform.Messaging;
using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Collector.Handlers;

/// <summary>
/// Claims a batch of due jobs for one collector, and opens the credential each of them named.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the only production caller of the decrypt path.</strong> WP-1.2 built
/// <c>ICredentialResolver</c> internal to this module with no HTTP surface and left WP-1.3 to
/// decide how far to widen it; the answer is that it is not widened at all. The resolver, the
/// plaintext type it returns and the shape that carries one to the collector are all internal to
/// <c>NetShield.Inventory</c>, the endpoint file names this handler and never the resolver, and
/// <c>CredentialExposureTests</c> allows exactly this one file to mention it.
/// </para>
/// <para>
/// The claim is a <c>SELECT … FOR UPDATE SKIP LOCKED</c> inside a transaction. Two collectors
/// asking at the same moment skip past each other's locked rows rather than blocking or claiming
/// the same job — this is the one place in NetShield where more than one process really does
/// compete for a row, which is why it does not follow the outbox's simpler claim (WP-0.3 chose
/// that deliberately, for a table exactly one process may dispatch).
/// </para>
/// <para>
/// Three things can happen to a claimed job besides being handed over. It can have run out of
/// attempts, in which case it is failed here rather than handed out for ever. It can name a
/// device that has since been removed, or a credential profile that has since been revoked — in
/// both cases it is failed at the lease, because a collector cannot do anything with it and a
/// revoked credential must not turn into a device that quietly stops being polled with no
/// explanation in the queue.
/// </para>
/// </remarks>
internal sealed class LeaseCollectorJobsHandler(
    InventoryDbContext context,
    ICredentialResolver credentials,
    OutboxEnlistment outbox,
    IAuditLog auditLog,
    SecretRedactor redactor,
    IOptions<CollectorJobOptions> options,
    IClock clock,
    ILogger<LeaseCollectorJobsHandler> logger)
{
    /// <summary>The audit action a released credential is recorded under.</summary>
    internal const string CredentialReleasedAction = "collector.credential-released";

    /// <summary>What an audit row from this handler says it acted on.</summary>
    internal const string CredentialResourceType = "credential-profile";

    public async Task<Result<CollectorJobBatch>> HandleAsync(
        CollectorCaller caller,
        int? limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);

        CollectorJobOptions settings = options.Value;
        int take = Math.Clamp(limit ?? settings.MaxJobsPerLease, 1, settings.MaxJobsPerLease);

        DateTimeOffset now = clock.UtcNow;
        DateTimeOffset expiresAt = now.AddSeconds(settings.LeaseSeconds);

        List<CollectorJobLease> leases = [];
        List<CredentialRelease> released = [];

        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);

        IReadOnlyList<CollectorJob> claimed = await ClaimAsync(now, take, cancellationToken);

        if (claimed.Count > 0)
        {
            Dictionary<Guid, Device> devices = await LoadDevicesAsync(claimed, cancellationToken);

            foreach (CollectorJob job in claimed)
            {
                if (job.Attempts >= job.MaxAttempts)
                {
                    Abandon(job, now);
                    continue;
                }

                CollectorJobDevice? device = null;

                if (job.DeviceId is { } deviceId)
                {
                    if (!devices.TryGetValue(deviceId, out Device? found))
                    {
                        Fail(job, now, "The device this job names is no longer in the inventory.");
                        continue;
                    }

                    device = new CollectorJobDevice(
                        found.Id,
                        found.Hostname,
                        found.PrimaryIpAddress.ToString(),
                        found.Vendor);
                }

                CollectorJobCredential? credential = null;

                if (job.CredentialProfileId is { } profileId)
                {
                    Result<ResolvedCredential> resolved =
                        await credentials.ResolveAsync(profileId, cancellationToken);

                    if (!resolved.IsSuccess)
                    {
                        Fail(job, now, "The credential profile this job names is no longer available.");
                        continue;
                    }

                    credential = CollectorMapping.ToCredential(resolved.Value);
                    released.Add(new CredentialRelease(job.Id, job.DeviceId, profileId, resolved.Value.Kind));
                }

                job.Status = CollectorJobStatus.Leased;
                job.Attempts++;
                job.LeaseToken = NewLeaseToken();
                job.LeasedBy = caller.Name;
                job.LeasedUntil = expiresAt;
                job.UpdatedAt = now;

                leases.Add(new CollectorJobLease(
                    job.Id,
                    job.Kind,
                    job.LeaseToken,
                    expiresAt,
                    job.Attempts,
                    device,
                    ParseParameters(job.Parameters),
                    credential));
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        // After the commit, for the reason WP-0.5 records an audit row after the endpoint: the
        // credential is released when the response is written, and a row for a lease that was
        // rolled back would describe something that did not happen. The window in the other
        // direction — a process death between the commit and this loop — is the same trade the
        // audit middleware already makes, and the answer if it ever stops being acceptable is
        // the transactional outbox, not a hand-written append inside the transaction.
        foreach (CredentialRelease release in released)
        {
            await RecordReleaseAsync(caller, release, cancellationToken);
        }

        return new CollectorJobBatch(leases, settings.LeaseSeconds);
    }

    /// <summary>
    /// Takes up to <paramref name="take"/> due jobs out from under any other collector asking at
    /// the same moment.
    /// </summary>
    /// <remarks>
    /// A job is claimable when it is due and either has never been leased or was leased by
    /// somebody whose lease has run out. <c>SKIP LOCKED</c> is what makes a second collector's
    /// call return different rows rather than waiting for the first one's transaction.
    /// </remarks>
    private async Task<IReadOnlyList<CollectorJob>> ClaimAsync(
        DateTimeOffset now,
        int take,
        CancellationToken cancellationToken)
    {
        string pending = nameof(CollectorJobStatus.Pending);
        string leased = nameof(CollectorJobStatus.Leased);

        List<Guid> ids = await context.Database
            .SqlQuery<Guid>(
                $"""
                 SELECT id AS "Value"
                 FROM collector_jobs
                 WHERE due_at <= {now}
                   AND (status = {pending} OR (status = {leased} AND leased_until <= {now}))
                 ORDER BY due_at, id
                 LIMIT {take}
                 FOR UPDATE SKIP LOCKED
                 """)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
        {
            return [];
        }

        // Re-read through the tracker so the rows can be changed. The ordering is restated here
        // because the set above is unordered once it comes back as ids.
        return await context.CollectorJobs
            .Where(job => ids.Contains(job.Id))
            .OrderBy(job => job.DueAt)
            .ThenBy(job => job.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<Dictionary<Guid, Device>> LoadDevicesAsync(
        IReadOnlyList<CollectorJob> jobs,
        CancellationToken cancellationToken)
    {
        List<Guid> deviceIds = [.. jobs.Select(job => job.DeviceId).OfType<Guid>().Distinct()];

        if (deviceIds.Count == 0)
        {
            return [];
        }

        return await context.Devices.AsNoTracking()
            .Where(device => deviceIds.Contains(device.Id) && device.DeletedAt == null)
            .ToDictionaryAsync(device => device.Id, cancellationToken);
    }

    /// <summary>
    /// Ends a job that has been leased as many times as it is allowed to be without ever
    /// producing a result.
    /// </summary>
    private void Abandon(CollectorJob job, DateTimeOffset now)
    {
        logger.LogWarning(
            "Collector job {JobId} of kind {Kind} was abandoned after {Attempts} attempts.",
            job.Id,
            job.Kind,
            job.Attempts);

        Fail(job, now, string.Create(
            CultureInfo.InvariantCulture,
            $"Abandoned after {job.Attempts} attempts without a result."));
    }

    /// <summary>Ends a job the collector was never handed, and says why in the queue.</summary>
    private void Fail(CollectorJob job, DateTimeOffset now, string detail)
    {
        job.Status = CollectorJobStatus.Failed;
        job.Outcome = CollectorJobOutcome.Failed;
        job.Detail = detail;
        job.CompletedAt = now;
        job.LeasedUntil = null;
        job.UpdatedAt = now;

        outbox.Enlist(
            context,
            new CollectorJobCompleted(job.Id, job.Kind, job.DeviceId, CollectorJobOutcome.Failed, now));
    }

    /// <summary>
    /// Writes the one audit row the collector contract produces.
    /// </summary>
    /// <remarks>
    /// The heartbeat and the result batch are machine traffic and carry <c>[NoAudit]</c>; this is
    /// not. Handing a device credential to a process is the security-relevant act in this whole
    /// contract, and it is the one an investigation asks about afterwards — so the row names the
    /// profile as its target, the collector as the actor, and the job and device it was released
    /// for. It names no part of the material: the snapshot goes through <c>SecretRedactor</c> on
    /// the way in, like every other audit snapshot in the system.
    /// </remarks>
    private async Task RecordReleaseAsync(
        CollectorCaller caller,
        CredentialRelease release,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;

        await auditLog.AppendAsync(
            new AuditEntry
            {
                Id = Guid.CreateVersion7(now),
                CreatedAt = now,

                // No actor id and no role. The caller is a process holding a shared secret, not
                // an account, and inventing a user for it would be the audit log asserting
                // something it does not know.
                ActorUsername = CollectorIdentity.ActorLabel,
                SourceIp = caller.SourceIp?.ToString(),
                Action = CredentialReleasedAction,
                TargetType = CredentialResourceType,
                TargetId = release.CredentialProfileId.ToString(),
                Outcome = AuditOutcome.Succeeded,
                Before = null,
                After = AuditPayload.Serialize(
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["collector"] = caller.Name,
                        ["jobId"] = release.JobId,
                        ["deviceId"] = release.DeviceId,

                        // The protocol, not the secret. It says what was handed over well enough
                        // to reason about the blast radius and opens nothing.
                        ["credentialKind"] = release.Kind.ToString()
                    },
                    redactor),
                HttpMethod = "GET",
                Path = "/internal/collector/jobs",
                StatusCode = 200
            },
            cancellationToken);
    }

    /// <summary>
    /// A token identifying one lease generation. It is not a secret — the caller has already
    /// authenticated — it is a value the next lease of the same job cannot accidentally equal.
    /// </summary>
    private static string NewLeaseToken() => Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

    /// <summary>
    /// The stored parameter document as JSON, or nothing. It went into the column as JSON and
    /// the column is <c>jsonb</c>, so this cannot fail on a row this system wrote.
    /// </summary>
    private static JsonElement? ParseParameters(string? parameters)
    {
        if (string.IsNullOrEmpty(parameters))
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(parameters);

        return document.RootElement.Clone();
    }

    /// <summary>One credential handed over, for the audit row written after the commit.</summary>
    private sealed record CredentialRelease(
        Guid JobId,
        Guid? DeviceId,
        Guid CredentialProfileId,
        Contracts.Inventory.CredentialKind Kind);
}
