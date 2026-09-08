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

namespace NetShield.Inventory.Discovery.Handlers;

/// <summary>
/// Serves one page of a device's interfaces, in the device's own <c>ifIndex</c> order.
/// </summary>
/// <remarks>
/// Paginated like every other list (CONVENTIONS.md §4), which matters here rather than being
/// ceremony: a chassis switch answers with several hundred rows and a stacked one with over a
/// thousand.
/// </remarks>
internal sealed class GetDeviceInterfaceListHandler(InventoryDbContext context, IResourceGuard guard)
{
    public async Task<Result<CursorPage<DeviceInterfaceSummary>>> HandleAsync(
        Guid deviceId,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        Result permitted = guard.Require(
            Permission.InventoryRead,
            GetDeviceListHandler.ResourceType,
            deviceId.ToString());

        if (!permitted.IsSuccess)
        {
            return Result<CursorPage<DeviceInterfaceSummary>>.Failure(permitted.Error);
        }

        // A page of nothing and a page of a device that does not exist are different answers,
        // and only one of them tells the caller they asked the wrong question. A device that has
        // never been walked has no interfaces and that is an empty page, not a 404 — the
        // fingerprint route is where "nothing has walked this" is said.
        bool exists = await context.Devices.AsNoTracking()
            .AnyAsync(device => device.Id == deviceId && device.DeletedAt == null, cancellationToken);

        if (!exists)
        {
            return DeviceErrors.NotFound(deviceId);
        }

        IQueryable<DeviceInterface> interfaces = context.DeviceInterfaces.AsNoTracking()
            .Where(row => row.DeviceId == deviceId);

        long totalCount = await interfaces.LongCountAsync(cancellationToken);

        if (page.Cursor is { } cursor)
        {
            Result<DeviceInterfaceCursor> position = DeviceInterfaceCursor.Decode(cursor);

            if (!position.IsSuccess)
            {
                return Result<CursorPage<DeviceInterfaceSummary>>.Failure(position.Error);
            }

            int from = position.Value.IfIndex;

            interfaces = interfaces.Where(row => row.IfIndex > from);
        }

        List<DeviceInterface> rows = await interfaces
            .OrderBy(row => row.IfIndex)
            .Take(page.FetchLimit)
            .ToListAsync(cancellationToken);

        CursorPage<DeviceInterface> result = rows.ToCursorPage(
            page,
            row => DeviceInterfaceCursor.Compose(row.IfIndex),
            totalCount);

        return new CursorPage<DeviceInterfaceSummary>(
            [.. result.Items.Select(row => row.ToSummary())],
            result.NextCursor,
            result.TotalCount);
    }
}
