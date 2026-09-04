using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Paging;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Authorization;
using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Discovery.Handlers;

/// <summary>Serves one page of the permanent ignore list.</summary>
internal sealed class GetDiscoveryIgnoreListHandler(InventoryDbContext context, IResourceGuard guard)
{
    /// <summary>What an audit row and a refusal call this kind of thing.</summary>
    internal const string ResourceType = "discovery-ignore";

    public async Task<Result<CursorPage<DiscoveryIgnoreEntry>>> HandleAsync(
        PageRequest page,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        Result permitted = guard.Require(Permission.InventoryRead, ResourceType);

        if (!permitted.IsSuccess)
        {
            return Result<CursorPage<DiscoveryIgnoreEntry>>.Failure(permitted.Error);
        }

        IQueryable<DiscoveryIgnore> ignores = context.DiscoveryIgnores.AsNoTracking();

        long totalCount = await ignores.LongCountAsync(cancellationToken);

        if (page.Cursor is { } cursor)
        {
            Result<DiscoveryCursor> position = DiscoveryCursor.Decode(cursor);

            if (!position.IsSuccess)
            {
                return Result<CursorPage<DiscoveryIgnoreEntry>>.Failure(position.Error);
            }

            DiscoveryCursor from = position.Value;

            ignores = ignores.Where(ignore =>
                ignore.CreatedAt > from.Timestamp
                || (ignore.CreatedAt == from.Timestamp && ignore.Id > from.Id));
        }

        List<DiscoveryIgnore> rows = await ignores
            .OrderBy(ignore => ignore.CreatedAt)
            .ThenBy(ignore => ignore.Id)
            .Take(page.FetchLimit)
            .ToListAsync(cancellationToken);

        CursorPage<DiscoveryIgnore> result = rows.ToCursorPage(
            page,
            ignore => DiscoveryCursor.Compose(ignore.CreatedAt, ignore.Id),
            totalCount);

        return new CursorPage<DiscoveryIgnoreEntry>(
            [.. result.Items.Select(ignore => ignore.ToContract())],
            result.NextCursor,
            result.TotalCount);
    }
}
