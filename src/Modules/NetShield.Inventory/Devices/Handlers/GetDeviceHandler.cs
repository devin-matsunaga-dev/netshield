using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Authorization;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Devices.Handlers;

/// <summary>Reads one device.</summary>
internal sealed class GetDeviceHandler(InventoryDbContext context, IResourceGuard guard)
{
    public async Task<Result<DeviceDetail>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        Result permitted = guard.Require(
            Permission.InventoryRead,
            GetDeviceListHandler.ResourceType,
            id.ToString());

        if (!permitted.IsSuccess)
        {
            return Result<DeviceDetail>.Failure(permitted.Error);
        }

        // A soft-deleted device is 404, not 410: whether it once existed is not something a
        // read endpoint should be disclosing.
        Device? device = await context.Devices.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id && candidate.DeletedAt == null, cancellationToken);

        return device is null ? DeviceErrors.NotFound(id) : device.ToDetail();
    }
}
