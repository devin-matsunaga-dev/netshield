using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Paging;

using NetShield.Inventory.Devices;
using NetShield.Inventory.Devices.Handlers;
using NetShield.Inventory.Persistence;

using NetShield.Platform.Authorization;
using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Topology.Handlers;

/// <summary>
/// Serves one page of a device's edges — from either end of each — with the evidence behind them.
/// </summary>
/// <remarks>
/// <para>
/// A device appears at the <c>A</c> end of some of its edges and the <c>B</c> end of others,
/// because the endpoints are canonically ordered rather than ordered by who asked. Both are
/// returned, which is the point: "what is this switch connected to" is one question and the
/// caller should not have to ask it twice.
/// </para>
/// <para>
/// Withdrawn edges are not returned. The rows survive so that "this link existed until Tuesday"
/// is answerable, and it takes an endpoint neither this route nor WP-2.3's graph asks for; the
/// package that wants topology history is the one to expose them.
/// </para>
/// </remarks>
internal sealed class GetDeviceAdjacencyListHandler(InventoryDbContext context, IResourceGuard guard)
{
    public async Task<Result<CursorPage<DeviceAdjacencySummary>>> HandleAsync(
        Guid deviceId,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        Result permitted = guard.Require(
            Permission.TopologyRead,
            GetDeviceListHandler.ResourceType,
            deviceId.ToString());

        if (!permitted.IsSuccess)
        {
            return Result<CursorPage<DeviceAdjacencySummary>>.Failure(permitted.Error);
        }

        // A device that has never been walked has no edges, and that is an empty page rather than
        // a 404 — the scan route is where "nothing has read this device's topology" is said. The
        // same split WP-1.7 drew between the interface list and the fingerprint.
        bool exists = await context.Devices.AsNoTracking()
            .AnyAsync(device => device.Id == deviceId && device.DeletedAt == null, cancellationToken);

        if (!exists)
        {
            return DeviceErrors.NotFound(deviceId);
        }

        IQueryable<DeviceAdjacency> edges = context.DeviceAdjacencies.AsNoTracking()
            .Where(edge => edge.WithdrawnAt == null
                && (edge.ADeviceId == deviceId || edge.BDeviceId == deviceId));

        long totalCount = await edges.LongCountAsync(cancellationToken);

        if (page.Cursor is { } cursor)
        {
            Result<AdjacencyCursor> position = AdjacencyCursor.Decode(cursor);

            if (!position.IsSuccess)
            {
                return Result<CursorPage<DeviceAdjacencySummary>>.Failure(position.Error);
            }

            Guid from = position.Value.Id;

            edges = edges.Where(edge => edge.Id > from);
        }

        List<DeviceAdjacency> rows = await edges
            .OrderBy(edge => edge.Id)
            .Take(page.FetchLimit)
            .ToListAsync(cancellationToken);

        CursorPage<DeviceAdjacency> result = rows.ToCursorPage(
            page,
            edge => AdjacencyCursor.Compose(edge.Id),
            totalCount);

        IReadOnlyDictionary<Guid, string> hostnames = await HostnamesAsync(
            result.Items,
            cancellationToken);

        IReadOnlyDictionary<Guid, List<AdjacencyEvidence>> evidence = await EvidenceAsync(
            result.Items,
            cancellationToken);

        return new CursorPage<DeviceAdjacencySummary>(
            [.. result.Items.Select(edge => edge.ToSummary(
                Hostname(hostnames, edge.ADeviceId),
                Hostname(hostnames, edge.BDeviceId),
                evidence.TryGetValue(edge.Id, out List<AdjacencyEvidence>? found) ? found : []))],
            result.NextCursor,
            result.TotalCount);
    }

    /// <summary>
    /// The hostnames of every live device either end of this page names.
    /// </summary>
    /// <remarks>
    /// A join rather than a stored copy, and live devices only: a removed device's edges survive
    /// as history, and naming it would put a device an operator deleted back on a screen.
    /// </remarks>
    private async Task<IReadOnlyDictionary<Guid, string>> HostnamesAsync(
        IReadOnlyList<DeviceAdjacency> edges,
        CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. edges
            .SelectMany(edge => new[] { (Guid?)edge.ADeviceId, edge.BDeviceId })
            .OfType<Guid>()
            .Distinct()];

        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var rows = await context.Devices.AsNoTracking()
            .Where(device => ids.Contains(device.Id) && device.DeletedAt == null)
            .Select(device => new { device.Id, device.Hostname })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.Id, row => row.Hostname);
    }

    /// <summary>The live observations supporting each edge on this page, from both ends.</summary>
    private async Task<IReadOnlyDictionary<Guid, List<AdjacencyEvidence>>> EvidenceAsync(
        IReadOnlyList<DeviceAdjacency> edges,
        CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. edges.Select(edge => edge.Id)];

        if (ids.Count == 0)
        {
            return new Dictionary<Guid, List<AdjacencyEvidence>>();
        }

        List<DeviceNeighbor> rows = await context.DeviceNeighbors.AsNoTracking()
            .Where(row => row.AdjacencyId != null
                && ids.Contains(row.AdjacencyId.Value)
                && row.WithdrawnAt == null)
            .OrderBy(row => row.Source)
            .ThenBy(row => row.DeviceId)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, List<AdjacencyEvidence>> grouped = [];

        foreach (DeviceNeighbor row in rows)
        {
            Guid key = row.AdjacencyId!.Value;

            if (!grouped.TryGetValue(key, out List<AdjacencyEvidence>? list))
            {
                list = [];
                grouped[key] = list;
            }

            list.Add(row.ToEvidence());
        }

        return grouped;
    }

    private static string? Hostname(IReadOnlyDictionary<Guid, string> hostnames, Guid? deviceId) =>
        deviceId is { } id && hostnames.TryGetValue(id, out string? hostname) ? hostname : null;
}
