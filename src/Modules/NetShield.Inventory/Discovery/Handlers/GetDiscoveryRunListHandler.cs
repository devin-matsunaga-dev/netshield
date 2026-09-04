using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Paging;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Authorization;
using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Discovery.Handlers;

/// <summary>
/// Serves one page of the discovery run history, newest first.
/// </summary>
/// <remarks>
/// Newest first, unlike every other list in this module, because a history is read from the end:
/// the question is almost always "what did the last run find". The keyset comparison is reversed
/// to match — a cursor that reads the order differently from the ORDER BY silently skips rows.
/// </remarks>
internal sealed class GetDiscoveryRunListHandler(InventoryDbContext context, IResourceGuard guard)
{
    public async Task<Result<CursorPage<DiscoveryRunSummary>>> HandleAsync(
        DiscoveryRunListQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = guard.Require(
            Permission.InventoryRead,
            StartDiscoveryRunHandler.ResourceType);

        if (!permitted.IsSuccess)
        {
            return Result<CursorPage<DiscoveryRunSummary>>.Failure(permitted.Error);
        }

        IQueryable<DiscoveryRun> runs = context.DiscoveryRuns.AsNoTracking();

        if (query.SeedId is { } seedId)
        {
            runs = runs.Where(run => run.SeedId == seedId);
        }

        if (query.Status is { } status)
        {
            runs = runs.Where(run => run.Status == status);
        }

        long totalCount = await runs.LongCountAsync(cancellationToken);

        if (query.Page.Cursor is { } cursor)
        {
            Result<DiscoveryCursor> position = DiscoveryCursor.Decode(cursor);

            if (!position.IsSuccess)
            {
                return Result<CursorPage<DiscoveryRunSummary>>.Failure(position.Error);
            }

            DiscoveryCursor from = position.Value;

            runs = runs.Where(run =>
                run.StartedAt < from.Timestamp
                || (run.StartedAt == from.Timestamp && run.Id < from.Id));
        }

        List<DiscoveryRun> rows = await runs
            .OrderByDescending(run => run.StartedAt)
            .ThenByDescending(run => run.Id)
            .Take(query.Page.FetchLimit)
            .ToListAsync(cancellationToken);

        CursorPage<DiscoveryRun> page = rows.ToCursorPage(
            query.Page,
            run => DiscoveryCursor.Compose(run.StartedAt, run.Id),
            totalCount);

        return new CursorPage<DiscoveryRunSummary>(
            [.. page.Items.Select(run => run.ToSummary())],
            page.NextCursor,
            page.TotalCount);
    }
}
