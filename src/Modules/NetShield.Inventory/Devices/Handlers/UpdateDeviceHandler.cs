using System.Net;

using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Inventory.Events;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Messaging;
using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Devices.Handlers;

/// <summary>
/// Replaces a device's attributes. A PUT describes the device as it should now be, so an omitted
/// optional member clears the stored value rather than leaving it alone.
/// </summary>
internal sealed class UpdateDeviceHandler(
    InventoryDbContext context,
    OutboxEnlistment outbox,
    IResourceGuard guard,
    IAuditContext audit,
    IClock clock)
{
    public async Task<Result<DeviceDetail>> HandleAsync(
        Guid id,
        UpdateDeviceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result permitted = guard.Require(
            Permission.InventoryWrite,
            GetDeviceListHandler.ResourceType,
            id.ToString());

        if (!permitted.IsSuccess)
        {
            return Result<DeviceDetail>.Failure(permitted.Error);
        }

        Device? device = await context.Devices
            .SingleOrDefaultAsync(candidate => candidate.Id == id && candidate.DeletedAt == null, cancellationToken);

        if (device is null)
        {
            return DeviceErrors.NotFound(id);
        }

        IPAddress address = IPAddress.Parse(request.PrimaryIpAddress);

        if (!device.PrimaryIpAddress.Equals(address)
            && await IsAddressTakenAsync(address, id, cancellationToken))
        {
            return DeviceErrors.DuplicateAddress(address.ToString());
        }

        // Taken before anything is written: the audit row's "before" has to be the row as it was,
        // and the entity is about to stop being that.
        IReadOnlyDictionary<string, object?> before = device.ToAuditSnapshot();
        string previousAddress = device.PrimaryIpAddress.ToString();

        device.Hostname = request.Hostname.Trim();
        device.PrimaryIpAddress = address;
        device.Vendor = request.Vendor;
        device.Model = CreateDeviceHandler.Clean(request.Model);
        device.OsVersion = CreateDeviceHandler.Clean(request.OsVersion);
        device.SerialNumber = CreateDeviceHandler.Clean(request.SerialNumber);
        device.Site = CreateDeviceHandler.Clean(request.Site);
        device.Role = request.Role;
        device.Criticality = request.Criticality;
        device.Environment = request.Environment;
        device.Owner = CreateDeviceHandler.Clean(request.Owner);
        device.Tags = DeviceTags.Normalize(request.Tags);
        device.Notes = CreateDeviceHandler.Clean(request.Notes);
        device.UpdatedAt = clock.UtcNow;

        // State is untouched on purpose. It is not on the request, and an edit to a device's
        // description is not evidence about whether it is answering.
        outbox.Enlist(
            context,
            new DeviceUpdated(
                device.Id,
                device.Hostname,
                device.PrimaryIpAddress.ToString(),
                previousAddress));

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException failure) when (CreateDeviceHandler.IsDuplicateAddress(failure))
        {
            return DeviceErrors.DuplicateAddress(address.ToString());
        }

        audit.Target(GetDeviceListHandler.ResourceType, device.Id.ToString());
        audit.Snapshot(before, device.ToAuditSnapshot());

        return device.ToDetail();
    }

    private Task<bool> IsAddressTakenAsync(IPAddress address, Guid excluding, CancellationToken cancellationToken) =>
        context.Devices.AnyAsync(
            device => device.DeletedAt == null
                && device.Id != excluding
                && device.PrimaryIpAddress.Equals(address),
            cancellationToken);
}
