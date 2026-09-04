using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Discovery;

/// <summary>
/// One pass of the discovery schedule: find the seeds whose next run has fallen due, start one
/// for each, and record when the next is expected.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as <c>ReachabilitySchedulePass</c>, at a coarser grain. A reachability pass
/// queues one job per device; a discovery pass starts one <em>run</em> per seed, and the run is
/// what fans out into jobs — so the ceiling worth having per pass is
/// <c>DiscoveryOptions.MaxRunsPerScan</c> rather than a job count.
/// </para>
/// <para>
/// A seed with a run already in flight is skipped, which is the same rule the on-demand route
/// refuses with a <c>409</c> and the same rule the reachability schedule applies to a device
/// with an outstanding probe. Without it, a collector outage would leave one run per seed per
/// interval accumulating, each one a fan-out of hundreds of jobs.
/// </para>
/// <para>
/// A seed that cannot be started is rescheduled anyway. Leaving <c>next_run_at</c> in the past
/// would have the pass find it again on every scan and log the same refusal every minute, which
/// is how a misconfigured seed comes to bury everything else in the log.
/// </para>
/// </remarks>
internal sealed class DiscoverySchedulePass(
    InventoryDbContext context,
    DiscoveryRunLauncher launcher,
    IOptions<DiscoveryOptions> options,
    IClock clock,
    ILogger<DiscoverySchedulePass> logger)
{
    /// <summary>Starts a run for every seed that is due, up to the configured ceiling.</summary>
    /// <returns>How many runs were started.</returns>
    public async Task<int> ScheduleDueAsync(CancellationToken cancellationToken)
    {
        DiscoveryOptions settings = options.Value;

        if (!settings.ScheduleEnabled)
        {
            return 0;
        }

        DateTimeOffset now = clock.UtcNow;

        // Tracked, because the pass is about to change each of them and the change has to commit
        // with the runs it starts.
        List<DiscoverySeed> due = await context.DiscoverySeeds
            .Where(seed => seed.DeletedAt == null
                && seed.Enabled
                && (seed.NextRunAt == null || seed.NextRunAt <= now)

                // Bounds the queue at one run per seed, so a collector outage cannot leave a
                // backlog of runs nobody will ever finish.
                && !context.DiscoveryRuns.Any(run =>
                    run.SeedId == seed.Id
                    && (run.Status == DiscoveryRunStatus.Pending
                        || run.Status == DiscoveryRunStatus.Running)))

            // A seed nobody has swept sorts ahead of every seed that has been: nothing at all is
            // known about what is in its ranges, which makes it the most urgent thing to ask.
            .OrderBy(seed => seed.NextRunAt == null ? DateTimeOffset.MinValue : seed.NextRunAt)
            .ThenBy(seed => seed.Id)
            .Take(settings.MaxRunsPerScan)
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
        {
            return 0;
        }

        int started = 0;

        foreach (DiscoverySeed seed in due)
        {
            Result<DiscoveryRun> run = await launcher.EnlistAsync(
                context,
                seed,
                DiscoveryRunTrigger.Scheduled,
                cancellationToken);

            if (!run.IsSuccess)
            {
                logger.LogWarning(
                    "Discovery seed {SeedId} was not run: {Reason}",
                    seed.Id,
                    run.Error.Message);

                // Pushed out anyway, so a seed that cannot run does not fill the log with the
                // same sentence on every scan. The launcher does this itself on success.
                seed.NextRunAt = now.AddMinutes(seed.IntervalMinutes);
                seed.UpdatedAt = now;

                continue;
            }

            started++;
        }

        // One save for the pass, so every run, every sweep job and every seed's new next-run
        // stamp lands together or none of them does.
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Started {Count} discovery runs", started);

        return started;
    }
}
