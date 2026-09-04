using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Discovery.Handlers;

/// <summary>
/// Starts a discovery run of one seed, on demand.
/// </summary>
/// <remarks>
/// <para>
/// Gated on <see cref="Permission.DiscoveryRun"/> — "start a discovery run outside its
/// schedule", which is exactly what this is, and the same permission the on-demand fingerprint
/// walk carries. Both make NetShield reach into the estate outside its schedule, and inventing a
/// second permission to tell a sweep from a walk would draw a line neither the RBAC table nor
/// SPEC.md §2 draws.
/// </para>
/// <para>
/// It ignores whether the seed is enabled. That switch governs the schedule; a person asking for
/// a run has said what they want, and refusing them because the seed does not run on its own
/// would make "sweep this once" impossible to express.
/// </para>
/// </remarks>
internal sealed class StartDiscoveryRunHandler(
    InventoryDbContext context,
    DiscoveryRunLauncher launcher,
    IResourceGuard guard,
    IAuditContext audit,
    ILogger<StartDiscoveryRunHandler> logger)
{
    /// <summary>What an audit row and a refusal call this kind of thing.</summary>
    internal const string ResourceType = "discovery-run";

    public async Task<Result<DiscoveryRunQueued>> HandleAsync(
        Guid seedId,
        CancellationToken cancellationToken)
    {
        Result permitted = guard.Require(Permission.DiscoveryRun, ResourceType);

        if (!permitted.IsSuccess)
        {
            return Result<DiscoveryRunQueued>.Failure(permitted.Error);
        }

        // Tracked: the launcher moves the seed's next-run stamp, and that has to commit with the
        // run and its jobs.
        DiscoverySeed? seed = await context.DiscoverySeeds.SingleOrDefaultAsync(
            candidate => candidate.Id == seedId && candidate.DeletedAt == null,
            cancellationToken);

        if (seed is null)
        {
            return DiscoveryErrors.SeedNotFound(seedId);
        }

        Result<DiscoveryRun> launched = await launcher.EnlistAsync(
            context,
            seed,
            DiscoveryRunTrigger.OnDemand,
            cancellationToken);

        if (!launched.IsSuccess)
        {
            return Result<DiscoveryRunQueued>.Failure(launched.Error);
        }

        await context.SaveChangesAsync(cancellationToken);

        DiscoveryRun run = launched.Value;

        logger.LogInformation(
            "Discovery run {RunId} of seed {SeedId} was started on demand",
            run.Id,
            seedId);

        audit.Target(ResourceType, run.Id.ToString());

        return new DiscoveryRunQueued(run.Id, seed.Id, run.JobCount, run.AddressCount, run.StartedAt);
    }
}
