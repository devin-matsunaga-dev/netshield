using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Discovery.Handlers;

/// <summary>
/// Removes an entry from the permanent ignore list.
/// </summary>
/// <remarks>
/// <para>
/// A hard delete. There is nothing to keep: the entry is a standing instruction rather than a
/// record of something that happened, and a soft-deleted one would have to be filtered out of
/// every containment test that reads the list.
/// </para>
/// <para>
/// The candidates it settled are not revived here. The next sweep that sees one of those
/// addresses answer finds it neither ignored nor a device, and puts it back on the review
/// list — which is the same path an address takes the first time, and means one rule decides
/// what is reviewable rather than two.
/// </para>
/// </remarks>
internal sealed class DeleteDiscoveryIgnoreHandler(
    InventoryDbContext context,
    IResourceGuard guard,
    IAuditContext audit)
{
    public async Task<Result> HandleAsync(Guid ignoreId, CancellationToken cancellationToken)
    {
        Result permitted = guard.Require(
            Permission.InventoryWrite,
            GetDiscoveryIgnoreListHandler.ResourceType,
            ignoreId.ToString());

        if (!permitted.IsSuccess)
        {
            return permitted;
        }

        DiscoveryIgnore? ignore = await context.DiscoveryIgnores
            .SingleOrDefaultAsync(row => row.Id == ignoreId, cancellationToken);

        if (ignore is null)
        {
            return DiscoveryErrors.IgnoreNotFound(ignoreId);
        }

        IReadOnlyDictionary<string, object?> before = ignore.ToAuditSnapshot();

        context.DiscoveryIgnores.Remove(ignore);

        await context.SaveChangesAsync(cancellationToken);

        audit.Target(GetDiscoveryIgnoreListHandler.ResourceType, ignore.Id.ToString());
        audit.Snapshot(before, after: null);

        return Result.Success;
    }
}
