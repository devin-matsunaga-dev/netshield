using System.Net;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NetShield.Contracts.Collector;
using NetShield.Contracts.Collector.Events;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Inventory.Events;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Messaging;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Discovery.Handlers;

/// <summary>
/// Reads a finished range sweep and records what answered.
/// </summary>
/// <remarks>
/// <para>
/// The third subscriber to <c>CollectorJobCompleted</c>, beside WP-1.4's reachability handler and
/// WP-1.5's fingerprint handler. Each reads only the jobs its own package queued: a <c>Poll</c>
/// naming the ICMP probe, a <c>Discover</c> naming the SNMP walk, and — here — a <c>Discover</c>
/// that <c>discovery_run_jobs</c> says belongs to a run. That table is the filter rather than the
/// parameters document, because "is this run finished" has to be a query over rows.
/// </para>
/// <para>
/// <strong>Nothing here creates a device.</strong> A responder becomes a candidate and waits for
/// somebody to look at it, which is the WP-1.6 criterion in one sentence. An address that already
/// belongs to a device is recorded as such and changes nothing about that device — a sweep
/// establishes that something answered at an address, which is not news about a device NetShield
/// is already polling.
/// </para>
/// <para>
/// <strong>Safe to run twice.</strong> Outbox delivery is at-least-once, and every counter this
/// handler touches is the kind a redelivery would silently corrupt. The run-job row's
/// <c>applied_at</c> is stamped before anything else is read, and a redelivery stops there.
/// </para>
/// </remarks>
internal sealed class RecordRangeSweepResultHandler(
    InventoryDbContext context,
    OutboxEnlistment outbox,
    IClock clock,
    ILogger<RecordRangeSweepResultHandler> logger) : IIntegrationEventHandler<CollectorJobCompleted>
{
    public async Task HandleAsync(
        CollectorJobCompleted integrationEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (integrationEvent.Kind != CollectorJobKind.Discover)
        {
            return;
        }

        DiscoveryRunJob? runJob = await context.DiscoveryRunJobs
            .SingleOrDefaultAsync(
                row => row.CollectorJobId == integrationEvent.JobId,
                cancellationToken);

        // Not a sweep this package queued. An on-demand fingerprint walk is a Discover too, and
        // it has no row here.
        if (runJob is null || runJob.AppliedAt is not null)
        {
            return;
        }

        DiscoveryRun? run = await context.DiscoveryRuns
            .SingleOrDefaultAsync(row => row.Id == runJob.RunId, cancellationToken);

        if (run is null)
        {
            logger.LogWarning(
                "Sweep job {JobId} names discovery run {RunId}, which does not exist.",
                integrationEvent.JobId,
                runJob.RunId);

            return;
        }

        DateTimeOffset now = clock.UtcNow;

        runJob.AppliedAt = now;
        runJob.UpdatedAt = now;
        run.JobsCompleted++;
        run.UpdatedAt = now;

        if (integrationEvent.Outcome != CollectorJobOutcome.Succeeded)
        {
            runJob.Succeeded = false;
            run.JobsFailed++;

            // The detail is already through SecretRedactor on its way into the column (WP-1.3).
            logger.LogWarning(
                "A sweep of {FirstAddress}-{LastAddress} for discovery run {RunId} failed: {Detail}",
                runJob.FirstAddress,
                runJob.LastAddress,
                run.Id,
                await DetailAsync(integrationEvent.JobId, cancellationToken));
        }
        else if (await ParseAsync(integrationEvent.JobId, cancellationToken) is { } result)
        {
            runJob.Succeeded = true;

            await ApplyAsync(run, result, now, cancellationToken);
        }
        else
        {
            // A successful job whose payload is not a sweep result is a collector reporting
            // something this package cannot read. It counts as a failed span rather than as an
            // empty one, because "nothing answered here" and "nobody looked here" are different
            // facts and only one of them is evidence.
            runJob.Succeeded = false;
            run.JobsFailed++;

            logger.LogWarning(
                "Collector job {JobId} succeeded but carried no readable sweep result.",
                integrationEvent.JobId);
        }

        await CompleteIfFinishedAsync(run, runJob.Id, now, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Folds one span's responders into the run, the candidates and the ignore rules.</summary>
    private async Task ApplyAsync(
        DiscoveryRun run,
        RangeSweepResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        List<Responder> responders = Read(result);

        if (responders.Count == 0)
        {
            return;
        }

        IReadOnlyList<IPAddress> addresses = [.. responders.Select(responder => responder.Address)];

        // Read once for the whole span rather than queried per address: the ignore list is
        // bounded by what a person typed, and the test is address-in-block, which EF cannot
        // express without raw SQL.
        IgnoreList ignores = IgnoreList.From(
            await context.DiscoveryIgnores
                .Select(ignore => ignore.Cidr)
                .ToListAsync(cancellationToken));

        Dictionary<string, Guid> devices = await context.Devices
            .Where(device => device.DeletedAt == null && addresses.Contains(device.PrimaryIpAddress))
            .ToDictionaryAsync(
                device => device.PrimaryIpAddress.ToString(),
                device => device.Id,
                StringComparer.Ordinal,
                cancellationToken);

        Dictionary<string, DiscoveryCandidate> candidates = await context.DiscoveryCandidates
            .Where(candidate => addresses.Contains(candidate.Address))
            .ToDictionaryAsync(
                candidate => candidate.Address.ToString(),
                candidate => candidate,
                StringComparer.Ordinal,
                cancellationToken);

        foreach (Responder responder in responders)
        {
            run.RespondedCount++;

            string key = responder.Address.ToString();
            candidates.TryGetValue(key, out DiscoveryCandidate? candidate);

            if (ignores.Contains(responder.Address))
            {
                run.IgnoredCount++;

                Record(run, responder, DiscoveryHostOutcome.Ignored, candidate?.Id, null, now);

                continue;
            }

            if (devices.TryGetValue(key, out Guid deviceId))
            {
                run.ExistingDeviceCount++;

                // A candidate for an address the inventory already owns is settled by that fact.
                // It is not a device this sweep created — it is one that arrived by some other
                // route, and leaving the candidate on the review list would ask somebody to
                // decide about a host that is already being polled.
                if (candidate is not null && candidate.Status != DiscoveryCandidateStatus.Promoted)
                {
                    candidate.Status = DiscoveryCandidateStatus.Promoted;
                    candidate.PromotedDeviceId = deviceId;
                    candidate.SettledAt = now;
                }

                Refresh(candidate, run, responder, now);
                Record(run, responder, DiscoveryHostOutcome.ExistingDevice, candidate?.Id, deviceId, now);

                continue;
            }

            if (candidate is not null)
            {
                run.KnownCandidateCount++;

                // Back on the review list. It can only be here if it is not ignored and no live
                // device holds the address, so whatever settled it — an ignore entry somebody
                // deleted, a device somebody removed — has been undone, and the address is once
                // again something nobody has decided about.
                candidate.Status = DiscoveryCandidateStatus.New;
                candidate.PromotedDeviceId = null;
                candidate.SettledAt = null;

                Refresh(candidate, run, responder, now);
                Record(run, responder, DiscoveryHostOutcome.KnownCandidate, candidate.Id, null, now);

                continue;
            }

            DiscoveryCandidate created = new()
            {
                Id = Guid.CreateVersion7(now),
                Address = responder.Address,
                Status = DiscoveryCandidateStatus.New,
                TimesSeen = 1,
                LastRttMilliseconds = responder.RttMilliseconds,
                FirstSeenAt = now,
                LastSeenAt = now,
                FirstSeenRunId = run.Id,
                LastSeenRunId = run.Id,
                CreatedAt = now,
                UpdatedAt = now
            };

            context.DiscoveryCandidates.Add(created);
            candidates[key] = created;

            run.NewCandidateCount++;

            Record(run, responder, DiscoveryHostOutcome.NewCandidate, created.Id, null, now);

            // Once per candidate, not once per sighting: a re-run that sees the same address
            // refreshes the row above and publishes nothing.
            outbox.Enlist(context, new DeviceDiscovered(created.Id, key, run.Id, run.SeedId, now));
        }

        if (result.Truncated)
        {
            logger.LogWarning(
                "A sweep of {FirstAddress}-{LastAddress} reported the most responders it was allowed to; "
                + "some answering addresses were not reported.",
                result.FirstAddress,
                result.LastAddress);
        }
    }

    /// <summary>Moves a candidate's last-seen forward without touching when it was first seen.</summary>
    private static void Refresh(
        DiscoveryCandidate? candidate,
        DiscoveryRun run,
        Responder responder,
        DateTimeOffset now)
    {
        if (candidate is null)
        {
            return;
        }

        candidate.TimesSeen++;
        candidate.LastRttMilliseconds = responder.RttMilliseconds;
        candidate.LastSeenAt = now;
        candidate.LastSeenRunId = run.Id;
        candidate.UpdatedAt = now;
    }

    private void Record(
        DiscoveryRun run,
        Responder responder,
        DiscoveryHostOutcome outcome,
        Guid? candidateId,
        Guid? deviceId,
        DateTimeOffset now) =>
        context.DiscoveryRunHosts.Add(new DiscoveryRunHost
        {
            Id = Guid.CreateVersion7(now),
            RunId = run.Id,
            Address = responder.Address,
            RttMilliseconds = responder.RttMilliseconds,
            Outcome = outcome,
            CandidateId = candidateId,
            DeviceId = deviceId,
            ObservedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });

    /// <summary>Closes the run once every one of its sweep jobs has been applied.</summary>
    /// <remarks>
    /// The row being applied in this delivery is excluded by id rather than trusted to be
    /// visible: it has been stamped in memory and not yet saved, so the database still shows it
    /// outstanding.
    /// </remarks>
    private async Task CompleteIfFinishedAsync(
        DiscoveryRun run,
        Guid appliedRunJobId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        bool outstanding = await context.DiscoveryRunJobs.AnyAsync(
            job => job.RunId == run.Id && job.AppliedAt == null && job.Id != appliedRunJobId,
            cancellationToken);

        if (outstanding)
        {
            run.Status = DiscoveryRunStatus.Running;

            return;
        }

        run.Status = run.JobsFailed switch
        {
            0 => DiscoveryRunStatus.Completed,
            _ when run.JobsFailed >= run.JobCount => DiscoveryRunStatus.Failed,
            _ => DiscoveryRunStatus.PartiallyFailed
        };

        run.CompletedAt = now;

        outbox.Enlist(context, new DiscoveryRunCompleted(
            run.Id,
            run.SeedId,
            run.Status,
            run.AddressCount,
            run.RespondedCount,
            run.NewCandidateCount,
            now));

        logger.LogInformation(
            "Discovery run {RunId} finished as {Status}: {RespondedCount} of {AddressCount} addresses "
            + "answered and {NewCandidateCount} are new",
            run.Id,
            run.Status,
            run.RespondedCount,
            run.AddressCount,
            run.NewCandidateCount);
    }

    /// <summary>The responders the collector reported, deduplicated and in address order.</summary>
    /// <remarks>
    /// An entry whose address will not parse is dropped rather than failing the span: it is one
    /// malformed line in a payload the rest of which is good, and losing the whole sweep over it
    /// would cost more than it saves. Duplicates are collapsed because the candidate table has
    /// one row per address and a collector that reported one twice must not create two.
    /// </remarks>
    private static List<Responder> Read(RangeSweepResult result)
    {
        Dictionary<string, Responder> unique = new(StringComparer.Ordinal);

        foreach (RangeSweepResponder reported in result.Responders ?? [])
        {
            if (!IPAddress.TryParse(reported.Address, out IPAddress? address))
            {
                continue;
            }

            unique.TryAdd(address.ToString(), new Responder(address, reported.RttMilliseconds));
        }

        return [.. unique.Values.OrderBy(responder => responder.Address.ToString(), StringComparer.Ordinal)];
    }

    /// <summary>The sweep result on the job row, or nothing if it does not read as one.</summary>
    private async Task<RangeSweepResult?> ParseAsync(Guid jobId, CancellationToken cancellationToken)
    {
        string? payload = await context.CollectorJobs.AsNoTracking()
            .Where(job => job.Id == jobId)
            .Select(job => job.Result)
            .SingleOrDefaultAsync(cancellationToken);

        if (string.IsNullOrEmpty(payload))
        {
            return null;
        }

        try
        {
            RangeSweepResult? result = JsonSerializer.Deserialize(
                payload,
                DiscoverySerializerContext.Default.RangeSweepResult);

            // The discriminator has to agree with the parameters. A payload that does not name
            // this walk is a collector answering a question nobody asked.
            return string.Equals(result?.Walk, RangeSweepParameters.WalkName, StringComparison.Ordinal)
                ? result
                : null;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "Collector job {JobId} carried a result that is not a readable sweep payload.",
                jobId);

            return null;
        }
    }

    /// <summary>The sentence the collector attached to a failed job, for the log.</summary>
    private async Task<string> DetailAsync(Guid jobId, CancellationToken cancellationToken) =>
        await context.CollectorJobs.AsNoTracking()
            .Where(job => job.Id == jobId)
            .Select(job => job.Detail)
            .SingleOrDefaultAsync(cancellationToken)
        ?? "The collector reported no detail.";

    /// <summary>One address that answered, once its address has been read.</summary>
    private sealed record Responder(IPAddress Address, double? RttMilliseconds);
}
