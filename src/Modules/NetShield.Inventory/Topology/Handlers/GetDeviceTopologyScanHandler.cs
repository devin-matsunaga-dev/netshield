using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Devices;
using NetShield.Inventory.Devices.Handlers;
using NetShield.Inventory.Persistence;

using NetShield.Platform.Authorization;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Topology.Handlers;

/// <summary>
/// Serves what NetShield knows about reading one device's topology.
/// </summary>
/// <remarks>
/// The route that answers "why does this switch show no edges?". A device with no adjacencies is
/// ambiguous on its own — it may implement neither protocol, it may have answered and had
/// nothing to say, or nothing may have asked it yet — and the three <c>Supported</c> flags and
/// the two errors here are what tell those apart.
/// </remarks>
internal sealed class GetDeviceTopologyScanHandler(InventoryDbContext context, IResourceGuard guard)
{
    public async Task<Result<DeviceTopologyScanDetail>> HandleAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        Result permitted = guard.Require(
            Permission.TopologyRead,
            GetDeviceListHandler.ResourceType,
            deviceId.ToString());

        if (!permitted.IsSuccess)
        {
            return Result<DeviceTopologyScanDetail>.Failure(permitted.Error);
        }

        bool exists = await context.Devices.AsNoTracking()
            .AnyAsync(device => device.Id == deviceId && device.DeletedAt == null, cancellationToken);

        if (!exists)
        {
            return DeviceErrors.NotFound(deviceId);
        }

        DeviceTopologyScan? scan = await context.DeviceTopologyScans.AsNoTracking()
            .SingleOrDefaultAsync(row => row.DeviceId == deviceId, cancellationToken);

        // Distinct from device.not-found, the way WP-1.7 made the fingerprint's absence distinct:
        // only one of the two is something an operator can act on, and the action is to walk it.
        return scan is null
            ? TopologyErrors.ScanNotFound(deviceId)
            : scan.ToDetail();
    }
}
