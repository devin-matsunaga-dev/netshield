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
/// Serves one page of the candidate review list, most recently seen first.
/// </summary>
internal sealed class GetDiscoveryCandidateListHandler(InventoryDbContext context, IResourceGuard guard)
{
    /// <summary>What an audit row and a refusal call this kind of thing.</summary>
    internal const string ResourceType = "discovery-candidate";

    public async Task<Result<CursorPage<DiscoveryCandidateSummary>>> HandleAsync(
        DiscoveryCandidateListQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = guard.Require(Permission.InventoryRead, ResourceType);

        if (!permitted.IsSuccess)
        {
            return Result<CursorPage<DiscoveryCandidateSummary>>.Failure(permitted.Error);
        }

        IQueryable<DiscoveryCandidate> candidates = context.DiscoveryCandidates.AsNoTracking();

        if (query.Status is { } status)
        {
            candidates = candidates.Where(candidate => candidate.Status == status);
        }

        long totalCount = await candidates.LongCountAsync(cancellationToken);

        if (query.Page.Cursor is { } cursor)
        {
            Result<DiscoveryCursor> position = DiscoveryCursor.Decode(cursor);

            if (!position.IsSuccess)
            {
                return Result<CursorPage<DiscoveryCandidateSummary>>.Failure(position.Error);
            }

            DiscoveryCursor from = position.Value;

            candidates = candidates.Where(candidate =>
                candidate.LastSeenAt < from.Timestamp
                || (candidate.LastSeenAt == from.Timestamp && candidate.Id < from.Id));
        }

        List<DiscoveryCandidate> rows = await candidates
            .OrderByDescending(candidate => candidate.LastSeenAt)
            .ThenByDescending(candidate => candidate.Id)
            .Take(query.Page.FetchLimit)
            .ToListAsync(cancellationToken);

        CursorPage<DiscoveryCandidate> page = rows.ToCursorPage(
            query.Page,
            candidate => DiscoveryCursor.Compose(candidate.LastSeenAt, candidate.Id),
            totalCount);

        return new CursorPage<DiscoveryCandidateSummary>(
            [.. page.Items.Select(candidate => candidate.ToSummary())],
            page.NextCursor,
            page.TotalCount);
    }
}
