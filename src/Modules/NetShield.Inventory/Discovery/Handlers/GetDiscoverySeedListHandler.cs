using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Paging;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Authorization;
using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Discovery.Handlers;

/// <summary>Serves one page of the discovery seed list, paged by keyset (CONVENTIONS.md §4).</summary>
/// <remarks>
/// Reading a seed is <see cref="Permission.InventoryRead"/> rather than
/// <see cref="Permission.PoliciesWrite"/>: a seed says which address ranges NetShield believes
/// are its estate, which is inventory information, and everyone who can see the device list can
/// already see the addresses in it. Changing one is the privileged half.
/// </remarks>
internal sealed class GetDiscoverySeedListHandler(InventoryDbContext context, IResourceGuard guard)
{
    /// <summary>What an audit row and a refusal call this kind of thing.</summary>
    internal const string ResourceType = "discovery-seed";

    public async Task<Result<CursorPage<DiscoverySeedSummary>>> HandleAsync(
        DiscoverySeedListQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = guard.Require(Permission.InventoryRead, ResourceType);

        if (!permitted.IsSuccess)
        {
            return Result<CursorPage<DiscoverySeedSummary>>.Failure(permitted.Error);
        }

        IQueryable<DiscoverySeed> seeds = context.DiscoverySeeds.AsNoTracking()
            .Where(seed => seed.DeletedAt == null);

        if (query.Enabled is { } enabled)
        {
            seeds = seeds.Where(seed => seed.Enabled == enabled);
        }

        long totalCount = await seeds.LongCountAsync(cancellationToken);

        if (query.Page.Cursor is { } cursor)
        {
            Result<DiscoveryCursor> position = DiscoveryCursor.Decode(cursor);

            if (!position.IsSuccess)
            {
                return Result<CursorPage<DiscoverySeedSummary>>.Failure(position.Error);
            }

            DiscoveryCursor from = position.Value;

            seeds = seeds.Where(seed =>
                seed.CreatedAt > from.Timestamp
                || (seed.CreatedAt == from.Timestamp && seed.Id > from.Id));
        }

        List<DiscoverySeed> rows = await seeds
            .OrderBy(seed => seed.CreatedAt)
            .ThenBy(seed => seed.Id)
            .Take(query.Page.FetchLimit)
            .ToListAsync(cancellationToken);

        CursorPage<DiscoverySeed> page = rows.ToCursorPage(
            query.Page,
            seed => DiscoveryCursor.Compose(seed.CreatedAt, seed.Id),
            totalCount);

        return new CursorPage<DiscoverySeedSummary>(
            [.. page.Items.Select(seed => seed.ToSummary())],
            page.NextCursor,
            page.TotalCount);
    }
}
