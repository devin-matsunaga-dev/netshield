using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Authorization;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Discovery.Handlers;

/// <summary>Serves one discovery seed.</summary>
internal sealed class GetDiscoverySeedHandler(InventoryDbContext context, IResourceGuard guard)
{
    public async Task<Result<DiscoverySeedDetail>> HandleAsync(
        Guid seedId,
        CancellationToken cancellationToken)
    {
        Result permitted = guard.Require(
            Permission.InventoryRead,
            GetDiscoverySeedListHandler.ResourceType);

        if (!permitted.IsSuccess)
        {
            return Result<DiscoverySeedDetail>.Failure(permitted.Error);
        }

        DiscoverySeed? seed = await context.DiscoverySeeds.AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == seedId && candidate.DeletedAt == null,
                cancellationToken);

        return seed is null ? DiscoveryErrors.SeedNotFound(seedId) : seed.ToDetail();
    }
}
