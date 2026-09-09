using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Paging;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Authorization;
using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Topology.Handlers;

/// <summary>
/// Serves one page of the estate's VLAN inventory: one row per VLAN, however many switches carry
/// it.
/// </summary>
/// <remarks>
/// <para>
/// This is the "appears once with aggregated membership" half of the WP-2.2 criterion, and the
/// shape the reference dashboard's VLAN tiles read — a name, a device count and a client count
/// per VLAN.
/// </para>
/// <para>
/// <strong>Aggregated on read, from <c>device_vlans</c>.</strong> There is no estate-wide VLAN
/// table to keep in step, because there is nothing in one that cannot be re-derived from the
/// device rows plus a join, and one of the two counts changes every time a client walk lands. The
/// merge itself is <see cref="VlanAggregationRule"/>, a pure function over the memberships, so
/// what the estate concludes from six switches can be tested as arithmetic rather than through a
/// database.
/// </para>
/// <para>
/// Withdrawn rows are not read. A VLAN every switch has stopped carrying is gone from the
/// inventory, and its rows survive so that "VLAN 40 was on the core until Tuesday" stays
/// answerable by the package that decides to ask.
/// </para>
/// </remarks>
internal sealed class GetVlanListHandler(InventoryDbContext context, VlanReadJoins joins, IResourceGuard guard)
{
    /// <summary>What an audit or authorization refusal from this handler names.</summary>
    internal const string ResourceType = "vlan";

    public async Task<Result<CursorPage<VlanSummary>>> HandleAsync(
        PageRequest page,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        Result permitted = guard.Require(Permission.TopologyRead, ResourceType);

        if (!permitted.IsSuccess)
        {
            return Result<CursorPage<VlanSummary>>.Failure(permitted.Error);
        }

        IQueryable<DeviceVlan> live = LiveRows(context);

        long totalCount = await live
            .Select(row => row.VlanId)
            .Distinct()
            .LongCountAsync(cancellationToken);

        IQueryable<int> ids = live.Select(row => row.VlanId).Distinct();

        if (page.Cursor is { } cursor)
        {
            Result<VlanCursor> position = VlanCursor.Decode(cursor);

            if (!position.IsSuccess)
            {
                return Result<CursorPage<VlanSummary>>.Failure(position.Error);
            }

            int from = position.Value.VlanId;

            ids = ids.Where(vlanId => vlanId > from);
        }

        List<int> vlanIds = await ids
            .OrderBy(vlanId => vlanId)
            .Take(page.FetchLimit)
            .ToListAsync(cancellationToken);

        CursorPage<int> paged = vlanIds.ToCursorPage(page, VlanCursor.Compose, totalCount);

        IReadOnlyList<VlanSummary> summaries = await SummariesAsync(paged.Items, cancellationToken);

        return new CursorPage<VlanSummary>(summaries, paged.NextCursor, paged.TotalCount);
    }

    /// <summary>The VLANs on this page, merged from every live device carrying each.</summary>
    private async Task<IReadOnlyList<VlanSummary>> SummariesAsync(
        IReadOnlyList<int> vlanIds,
        CancellationToken cancellationToken)
    {
        if (vlanIds.Count == 0)
        {
            return [];
        }

        var rows = await LiveRows(context)
            .Where(row => vlanIds.Contains(row.VlanId))
            .Select(row => new
            {
                row.VlanId,
                row.DeviceId,
                row.Name,
                PortCount = row.IfIndexes.Length,
                row.FirstDiscoveredAt,
                row.LastSeenAt
            })
            .ToListAsync(cancellationToken);

        IReadOnlyDictionary<int, int> clients = await joins.ClientCountsAsync(
            vlanIds,
            cancellationToken);

        List<VlanSummary> summaries = new(vlanIds.Count);

        foreach (int vlanId in vlanIds)
        {
            List<VlanAggregationRule.Membership> memberships =
            [
                .. rows.Where(row => row.VlanId == vlanId)
                    .Select(row => new VlanAggregationRule.Membership(
                        row.DeviceId,
                        row.Name,
                        row.PortCount,
                        row.FirstDiscoveredAt,
                        row.LastSeenAt))
            ];

            if (memberships.Count == 0)
            {
                // The page's ids came from this same query a moment ago, so this can only happen
                // if a walk withdrew the last membership in between. Skipping is right: the VLAN
                // is gone, and inventing a row with a zero device count would be worse.
                continue;
            }

            VlanAggregationRule.Aggregated aggregated = VlanAggregationRule.Aggregate(memberships);

            summaries.Add(new VlanSummary(
                vlanId,
                aggregated.Name,
                aggregated.NameDisputed,
                aggregated.DeviceCount,
                clients.TryGetValue(vlanId, out int count) ? count : 0,
                aggregated.PortCount,
                aggregated.FirstDiscoveredAt,
                aggregated.LastSeenAt));
        }

        return summaries;
    }

    /// <summary>
    /// The live VLAN rows of live devices. The one place the two filters are written, so a read
    /// cannot accidentally count a withdrawn VLAN or a removed switch.
    /// </summary>
    internal static IQueryable<DeviceVlan> LiveRows(InventoryDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.DeviceVlans.AsNoTracking()
            .Where(row => row.WithdrawnAt == null
                && context.Devices.Any(device =>
                    device.Id == row.DeviceId && device.DeletedAt == null));
    }
}
