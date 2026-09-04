using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Discovery.Handlers;

/// <summary>Replaces a discovery seed.</summary>
/// <remarks>
/// Whole-resource replacement, like every other update in this module (WP-1.1). A seed that is
/// switched on and has never been due falls due immediately; one that was already scheduled
/// keeps its place in the queue, so saving an unrelated edit does not restart the estate's sweep.
/// </remarks>
internal sealed class UpdateDiscoverySeedHandler(
    InventoryDbContext context,
    IResourceGuard guard,
    IAuditContext audit,
    IClock clock)
{
    public async Task<Result<DiscoverySeedDetail>> HandleAsync(
        Guid seedId,
        UpdateDiscoverySeedRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result permitted = guard.Require(
            Permission.PoliciesWrite,
            GetDiscoverySeedListHandler.ResourceType);

        if (!permitted.IsSuccess)
        {
            return Result<DiscoverySeedDetail>.Failure(permitted.Error);
        }

        DiscoverySeed? seed = await context.DiscoverySeeds.SingleOrDefaultAsync(
            candidate => candidate.Id == seedId && candidate.DeletedAt == null,
            cancellationToken);

        if (seed is null)
        {
            return DiscoveryErrors.SeedNotFound(seedId);
        }

        string name = request.Name.Trim();

        if (await CreateDiscoverySeedHandler.IsNameTakenAsync(context, name, seedId, cancellationToken))
        {
            return DiscoveryErrors.SeedNameTaken(name);
        }

        Result<SweepPlan> planned = SweepPlan.Create(request.Ranges, request.Exclusions);

        if (!planned.IsSuccess)
        {
            return Result<DiscoverySeedDetail>.Failure(planned.Error);
        }

        IReadOnlyDictionary<string, object?> before = seed.ToAuditSnapshot();
        DateTimeOffset now = clock.UtcNow;

        seed.Name = name;
        seed.Description = CreateDiscoverySeedHandler.Clean(request.Description);
        seed.Enabled = request.Enabled;
        seed.Ranges = CreateDiscoverySeedHandler.Normalise(planned.Value.Ranges);
        seed.Exclusions = CreateDiscoverySeedHandler.Normalise(planned.Value.Exclusions);
        seed.IntervalMinutes = request.IntervalMinutes;
        seed.UpdatedAt = now;

        if (seed.Enabled && seed.NextRunAt is null)
        {
            seed.NextRunAt = now;
        }

        await context.SaveChangesAsync(cancellationToken);

        audit.Target(GetDiscoverySeedListHandler.ResourceType, seed.Id.ToString());
        audit.Snapshot(before, seed.ToAuditSnapshot());

        return seed.ToDetail();
    }
}
