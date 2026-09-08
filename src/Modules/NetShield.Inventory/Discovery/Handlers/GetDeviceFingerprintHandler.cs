using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Devices;
using NetShield.Inventory.Devices.Handlers;
using NetShield.Inventory.Persistence;

using NetShield.Platform.Authorization;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Discovery.Handlers;

/// <summary>
/// Reads what the last walk established about one device.
/// </summary>
/// <remarks>
/// Two absences, told apart. A device that does not exist is <c>device.not-found</c>; a device
/// that exists and has never been walked is <c>discovery.fingerprint-not-found</c>. Only the
/// second is something an operator can act on, and the action is to walk it.
/// </remarks>
internal sealed class GetDeviceFingerprintHandler(InventoryDbContext context, IResourceGuard guard)
{
    public async Task<Result<DeviceFingerprintDetail>> HandleAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        Result permitted = guard.Require(
            Permission.InventoryRead,
            GetDeviceListHandler.ResourceType,
            deviceId.ToString());

        if (!permitted.IsSuccess)
        {
            return Result<DeviceFingerprintDetail>.Failure(permitted.Error);
        }

        bool exists = await context.Devices.AsNoTracking()
            .AnyAsync(device => device.Id == deviceId && device.DeletedAt == null, cancellationToken);

        if (!exists)
        {
            return DeviceErrors.NotFound(deviceId);
        }

        DeviceFingerprint? fingerprint = await context.DeviceFingerprints.AsNoTracking()
            .SingleOrDefaultAsync(row => row.DeviceId == deviceId, cancellationToken);

        return fingerprint is null
            ? DiscoveryErrors.FingerprintNotFound(deviceId)
            : fingerprint.ToDetail();
    }
}
