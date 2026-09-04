using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NetShield.Contracts.Collector;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Inventory.Events;

using NetShield.Inventory.Collector;
using NetShield.Inventory.Persistence;

using NetShield.Platform.Messaging;
using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Discovery;

/// <summary>
/// Turns one seed into one run: the row, the sweep jobs it fans out into, and the link between
/// them.
/// </summary>
/// <remarks>
/// <para>
/// One implementation with two callers — the schedule and the on-demand route — because "what a
/// run is" must not depend on who asked for it. The only difference the two make is
/// <see cref="DiscoveryRunTrigger"/>, which is recorded rather than acted on.
/// </para>
/// <para>
/// It stages everything on the caller's context and saves nothing, the way
/// <c>ICollectorJobQueue.EnlistAsync</c> does and for the same reason: the run, its jobs, the
/// event announcing it and the seed's new <c>next_run_at</c> have to commit together or a seed
/// is left either marked as run with nothing queued, or queued twice.
/// </para>
/// </remarks>
internal sealed class DiscoveryRunLauncher(
    ICollectorJobQueue queue,
    OutboxEnlistment outbox,
    IOptions<DiscoveryOptions> options,
    IClock clock,
    ILogger<DiscoveryRunLauncher> logger)
{
    /// <summary>
    /// Stages a run of <paramref name="seed"/> and the sweep jobs it needs.
    /// </summary>
    /// <returns>
    /// The staged run, or a refusal when the seed already has one in flight or has nothing left
    /// to sweep.
    /// </returns>
    public async Task<Result<DiscoveryRun>> EnlistAsync(
        InventoryDbContext context,
        DiscoverySeed seed,
        DiscoveryRunTrigger trigger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(seed);

        if (await HasRunInFlightAsync(context, seed.Id, cancellationToken))
        {
            return DiscoveryErrors.RunInFlight(seed.Id);
        }

        Result<SweepPlan> planned = SweepPlan.Create(seed.Ranges, seed.Exclusions);

        if (!planned.IsSuccess)
        {
            return Result<DiscoveryRun>.Failure(planned.Error);
        }

        SweepPlan plan = planned.Value;
        DiscoveryOptions settings = options.Value;

        List<AddressSpan> spans = [.. plan.Spans(settings.MaxAddressesPerJob).Take(settings.MaxJobsPerRun)];

        if (spans.Count == 0)
        {
            return DiscoveryErrors.NothingToSweep(seed.Id);
        }

        DateTimeOffset now = clock.UtcNow;

        DiscoveryRun run = new()
        {
            Id = Guid.CreateVersion7(now),
            SeedId = seed.Id,
            SeedName = seed.Name,
            Trigger = trigger,
            Status = DiscoveryRunStatus.Pending,

            // The run's own copy of what it swept. The seed is editable and this history is not.
            Ranges = [.. seed.Ranges],
            Exclusions = [.. seed.Exclusions],
            AddressCount = spans.Sum(plan.Probeable),
            StartedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.DiscoveryRuns.Add(run);

        IReadOnlyList<string> exclusions = [.. plan.Exclusions.Select(exclusion => exclusion.ToString())];

        foreach (AddressSpan span in spans)
        {
            Result<Guid> job = await queue.EnlistAsync(
                context,
                new NewCollectorJob(
                    CollectorJobKind.Discover,

                    // No device and no credential. A sweep is looking for hosts that are not
                    // devices yet, and an echo request authenticates to nothing.
                    DeviceId: null,
                    CredentialProfileId: null,
                    Parameters(settings, span, exclusions),
                    DueAt: now),
                cancellationToken);

            if (!job.IsSuccess)
            {
                // Nothing a sweep job names can have been removed — it names no device and no
                // credential — so the only way here is a parameters document over the ceiling,
                // which is a bug in the settings rather than a race.
                return Result<DiscoveryRun>.Failure(job.Error);
            }

            context.DiscoveryRunJobs.Add(new DiscoveryRunJob
            {
                Id = Guid.CreateVersion7(now),
                RunId = run.Id,
                CollectorJobId = job.Value,
                Sequence = run.JobCount + 1,
                FirstAddress = span.FirstAddress.ToString(),
                LastAddress = span.LastAddress.ToString(),
                AddressCount = (int)Math.Min(span.Count, int.MaxValue),
                CreatedAt = now,
                UpdatedAt = now
            });

            run.JobCount++;
        }

        seed.LastRunAt = now;
        seed.NextRunAt = now.AddMinutes(seed.IntervalMinutes);
        seed.UpdatedAt = now;

        outbox.Enlist(context, new DiscoveryRunStarted(
            run.Id,
            seed.Id,
            seed.Name,
            trigger,
            run.JobCount,
            run.AddressCount,
            now));

        logger.LogInformation(
            "Queued discovery run {RunId} for seed {SeedId} as {JobCount} sweep jobs over {AddressCount} addresses",
            run.Id,
            seed.Id,
            run.JobCount,
            run.AddressCount);

        return run;
    }

    /// <summary>Whether the seed already has a run that has not finished.</summary>
    internal static Task<bool> HasRunInFlightAsync(
        InventoryDbContext context,
        Guid seedId,
        CancellationToken cancellationToken) =>
        context.DiscoveryRuns.AnyAsync(
            run => run.SeedId == seedId
                && (run.Status == DiscoveryRunStatus.Pending || run.Status == DiscoveryRunStatus.Running),
            cancellationToken);

    private static JsonElement Parameters(
        DiscoveryOptions settings,
        AddressSpan span,
        IReadOnlyList<string> exclusions)
    {
        using JsonDocument document = JsonSerializer.SerializeToDocument(
            RangeSweepParameters.From(settings, span, exclusions),
            DiscoverySerializerContext.Default.RangeSweepParameters);

        return document.RootElement.Clone();
    }
}
