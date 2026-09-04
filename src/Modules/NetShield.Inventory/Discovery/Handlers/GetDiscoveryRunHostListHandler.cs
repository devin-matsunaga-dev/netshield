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
/// Serves one page of a run's per-host outcomes.
/// </summary>
/// <remarks>
/// Only the addresses that answered are here. A run keeps the ranges it swept and how many
/// addresses that was, so "was this address in scope" is answerable from
/// <c>GET /api/v1/discovery/runs/{id}</c> — see <see cref="DiscoveryHostOutcome"/> for why the
/// silent ones are not rows.
/// </remarks>
internal sealed class GetDiscoveryRunHostListHandler(InventoryDbContext context, IResourceGuard guard)
{
    public async Task<Result<CursorPage<DiscoveryRunHostResult>>> HandleAsync(
        Guid runId,
        PageRequest page,
        DiscoveryHostOutcome? outcome,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        Result permitted = guard.Require(
            Permission.InventoryRead,
            StartDiscoveryRunHandler.ResourceType);

        if (!permitted.IsSuccess)
        {
            return Result<CursorPage<DiscoveryRunHostResult>>.Failure(permitted.Error);
        }

        // A page of nothing and a page of a run that does not exist are different answers, and
        // only one of them tells the caller they asked the wrong question.
        if (!await context.DiscoveryRuns.AnyAsync(run => run.Id == runId, cancellationToken))
        {
            return DiscoveryErrors.RunNotFound(runId);
        }

        IQueryable<DiscoveryRunHost> hosts = context.DiscoveryRunHosts.AsNoTracking()
            .Where(host => host.RunId == runId);

        if (outcome is { } wanted)
        {
            hosts = hosts.Where(host => host.Outcome == wanted);
        }

        long totalCount = await hosts.LongCountAsync(cancellationToken);

        if (page.Cursor is { } cursor)
        {
            Result<DiscoveryCursor> position = DiscoveryCursor.Decode(cursor);

            if (!position.IsSuccess)
            {
                return Result<CursorPage<DiscoveryRunHostResult>>.Failure(position.Error);
            }

            DiscoveryCursor from = position.Value;

            hosts = hosts.Where(host =>
                host.ObservedAt > from.Timestamp
                || (host.ObservedAt == from.Timestamp && host.Id > from.Id));
        }

        List<DiscoveryRunHost> rows = await hosts
            .OrderBy(host => host.ObservedAt)
            .ThenBy(host => host.Id)
            .Take(page.FetchLimit)
            .ToListAsync(cancellationToken);

        CursorPage<DiscoveryRunHost> result = rows.ToCursorPage(
            page,
            host => DiscoveryCursor.Compose(host.ObservedAt, host.Id),
            totalCount);

        return new CursorPage<DiscoveryRunHostResult>(
            [.. result.Items.Select(host => host.ToContract())],
            result.NextCursor,
            result.TotalCount);
    }
}
