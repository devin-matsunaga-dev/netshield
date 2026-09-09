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
/// Serves one page of the VLANs one device is configured with, and the ports it carries each on.
/// </summary>
/// <remarks>
/// The observation beside <see cref="GetVlanListHandler"/>'s conclusion, and the same split
/// WP-2.1 drew between a device's edges and the graph: this is what a switch said about itself,
/// unmerged, which is where an operator looks when the estate-wide row says something they do not
/// believe.
///
/// A device that has never been walked has no VLANs, and that is an empty page rather than a 404 —
/// the topology-scan route is where "nothing has read this device" is said. The same split WP-1.7
/// drew between the interface list and the fingerprint.
/// </remarks>
internal sealed class GetDeviceVlanListHandler(
    InventoryDbContext context,
    VlanReadJoins joins,
    IResourceGuard guard)
{
    public async Task<Result<CursorPage<DeviceVlanSummary>>> HandleAsync(
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
            return Result<CursorPage<DeviceVlanSummary>>.Failure(permitted.Error);
        }

        bool exists = await context.Devices.AsNoTracking()
            .AnyAsync(device => device.Id == deviceId && device.DeletedAt == null, cancellationToken);

        if (!exists)
        {
            return DeviceErrors.NotFound(deviceId);
        }

        IQueryable<DeviceVlan> rows = context.DeviceVlans.AsNoTracking()
            .Where(row => row.DeviceId == deviceId && row.WithdrawnAt == null);

        long totalCount = await rows.LongCountAsync(cancellationToken);

        if (page.Cursor is { } cursor)
        {
            Result<VlanCursor> position = VlanCursor.Decode(cursor);

            if (!position.IsSuccess)
            {
                return Result<CursorPage<DeviceVlanSummary>>.Failure(position.Error);
            }

            int from = position.Value.VlanId;

            rows = rows.Where(row => row.VlanId > from);
        }

        List<DeviceVlan> fetched = await rows
            .OrderBy(row => row.VlanId)
            .Take(page.FetchLimit)
            .ToListAsync(cancellationToken);

        CursorPage<DeviceVlan> paged = fetched.ToCursorPage(
            page,
            row => VlanCursor.Compose(row.VlanId),
            totalCount);

        IReadOnlyDictionary<(Guid, int), string> portNames = await joins.PortNamesAsync(
            [deviceId],
            cancellationToken);

        IReadOnlyDictionary<(Guid, int), int> clients = await joins.ClientCountsByDeviceAsync(
            [deviceId],
            [.. paged.Items.Select(row => row.VlanId)],
            cancellationToken);

        return new CursorPage<DeviceVlanSummary>(
            [.. paged.Items.Select(row => new DeviceVlanSummary(
                row.DeviceId,
                row.VlanId,
                row.Name,
                VlanReadJoins.Ports(row, portNames),
                row.PortCount,
                row.UnresolvedPortCount,
                clients.TryGetValue((row.DeviceId, row.VlanId), out int count) ? count : 0,
                row.FirstDiscoveredAt,
                row.LastSeenAt))],
            paged.NextCursor,
            paged.TotalCount);
    }
}
