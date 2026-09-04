using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Discovery.Handlers;

/// <summary>Removes a discovery seed.</summary>
/// <remarks>
/// <para>
/// Soft delete, because a seed is the one part of discovery an operator maintains and the runs
/// that name it have to stay readable. The schedule stops seeing it immediately.
/// </para>
/// <para>
/// A run already in flight is left alone: its sweep jobs are queued, they will be leased, and
/// their results will be recorded against a run that still exists. Cancelling them would mean a
/// cancellation path in <c>collector_jobs</c>, which WP-1.3 deliberately did not build.
/// </para>
/// </remarks>
internal sealed class DeleteDiscoverySeedHandler(
    InventoryDbContext context,
    IResourceGuard guard,
    IAuditContext audit,
    IClock clock)
{
    public async Task<Result> HandleAsync(Guid seedId, CancellationToken cancellationToken)
    {
        Result permitted = guard.Require(
            Permission.PoliciesWrite,
            GetDiscoverySeedListHandler.ResourceType);

        if (!permitted.IsSuccess)
        {
            return permitted;
        }

        DiscoverySeed? seed = await context.DiscoverySeeds.SingleOrDefaultAsync(
            candidate => candidate.Id == seedId && candidate.DeletedAt == null,
            cancellationToken);

        if (seed is null)
        {
            return DiscoveryErrors.SeedNotFound(seedId);
        }

        IReadOnlyDictionary<string, object?> before = seed.ToAuditSnapshot();
        DateTimeOffset now = clock.UtcNow;

        seed.DeletedAt = now;
        seed.UpdatedAt = now;

        // Nothing will schedule it again, whatever it said before it was removed.
        seed.NextRunAt = null;

        await context.SaveChangesAsync(cancellationToken);

        audit.Target(GetDiscoverySeedListHandler.ResourceType, seed.Id.ToString());
        audit.Snapshot(before, after: null);

        return Result.Success;
    }
}
