using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory.Events;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Messaging;
using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Devices.Handlers;

/// <summary>
/// Removes a device. Soft delete (CONVENTIONS.md §3): the row stays so that telemetry, audit
/// rows and events that named this device still resolve, and it stops holding its address.
/// </summary>
internal sealed class DeleteDeviceHandler(
    InventoryDbContext context,
    OutboxEnlistment outbox,
    IResourceGuard guard,
    IAuditContext audit,
    IClock clock)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        Result permitted = guard.Require(
            Permission.InventoryWrite,
            GetDeviceListHandler.ResourceType,
            id.ToString());

        if (!permitted.IsSuccess)
        {
            return permitted;
        }

        Device? device = await context.Devices
            .SingleOrDefaultAsync(candidate => candidate.Id == id && candidate.DeletedAt == null, cancellationToken);

        if (device is null)
        {
            // Deleting a device that is already deleted is 404 rather than 204. A caller who
            // believes they removed something they did not is worse served by silence.
            return DeviceErrors.NotFound(id);
        }

        IReadOnlyDictionary<string, object?> before = device.ToAuditSnapshot();

        DateTimeOffset now = clock.UtcNow;

        device.DeletedAt = now;
        device.UpdatedAt = now;

        outbox.Enlist(
            context,
            new DeviceRemoved(device.Id, device.Hostname, device.PrimaryIpAddress.ToString()));

        await context.SaveChangesAsync(cancellationToken);

        audit.Target(GetDeviceListHandler.ResourceType, device.Id.ToString());
        audit.Snapshot(before, after: null);

        return Result.Success;
    }
}
