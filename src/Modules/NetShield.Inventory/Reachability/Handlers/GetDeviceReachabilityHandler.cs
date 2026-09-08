using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Devices;
using NetShield.Inventory.Devices.Handlers;
using NetShield.Inventory.Persistence;

using NetShield.Platform.Authorization;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Reachability.Handlers;

/// <summary>
/// Reads the evidence behind a device's published state.
/// </summary>
/// <remarks>
/// The state itself is on the device and moves with the transition; what is here is the last
/// round trip, the last loss, when the last probe landed and — the member that matters — why the
/// last one could not be performed. A device nothing has been able to probe since Tuesday
/// otherwise presents as one that is confidently Online with a stale timestamp.
/// </remarks>
internal sealed class GetDeviceReachabilityHandler(InventoryDbContext context, IResourceGuard guard)
{
    public async Task<Result<DeviceReachabilityDetail>> HandleAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        Result permitted = guard.Require(
            Permission.InventoryRead,
            GetDeviceListHandler.ResourceType,
            deviceId.ToString());

        if (!permitted.IsSuccess)
        {
            return Result<DeviceReachabilityDetail>.Failure(permitted.Error);
        }

        // The state is read from the device rather than recomputed here: the device row is what
        // every list, filter and report already reads, and two places answering "what state is
        // this device in" is one place too many.
        var device = await context.Devices.AsNoTracking()
            .Where(candidate => candidate.Id == deviceId && candidate.DeletedAt == null)
            .Select(candidate => new { candidate.State })
            .SingleOrDefaultAsync(cancellationToken);

        if (device is null)
        {
            return DeviceErrors.NotFound(deviceId);
        }

        DeviceReachability? reachability = await context.DeviceReachabilities.AsNoTracking()
            .SingleOrDefaultAsync(row => row.DeviceId == deviceId, cancellationToken);

        return reachability is null
            ? ReachabilityErrors.NotFound(deviceId)
            : new DeviceReachabilityDetail(
                deviceId,
                device.State,
                reachability.LastRttMilliseconds,
                reachability.LastLossPercent,
                reachability.LastProbeAt,
                reachability.LastChangedAt,
                reachability.NextProbeAt,
                reachability.LastError);
    }
}
