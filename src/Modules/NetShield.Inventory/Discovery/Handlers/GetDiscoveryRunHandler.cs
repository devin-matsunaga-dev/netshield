using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Authorization;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Discovery.Handlers;

/// <summary>Serves one discovery run.</summary>
internal sealed class GetDiscoveryRunHandler(InventoryDbContext context, IResourceGuard guard)
{
    public async Task<Result<DiscoveryRunDetail>> HandleAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        Result permitted = guard.Require(
            Permission.InventoryRead,
            StartDiscoveryRunHandler.ResourceType);

        if (!permitted.IsSuccess)
        {
            return Result<DiscoveryRunDetail>.Failure(permitted.Error);
        }

        DiscoveryRun? run = await context.DiscoveryRuns.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == runId, cancellationToken);

        return run is null ? DiscoveryErrors.RunNotFound(runId) : run.ToDetail();
    }
}
