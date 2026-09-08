using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Authorization;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Clients.Handlers;

/// <summary>
/// Serves one client, with every binding that is currently open.
/// </summary>
/// <remarks>
/// Every open binding, not one of each. A client legitimately holds an IPv4 and an IPv6 address
/// at once, and is legitimately reported by its access switch and by every switch above it — so
/// the detail shows all of them and lets the reader see the shape, where the list has to pick
/// one to put in a column.
/// </remarks>
internal sealed class GetClientHandler(InventoryDbContext context, IResourceGuard guard)
{
    public async Task<Result<ClientDetail>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        Result permitted = guard.Require(
            Permission.InventoryRead,
            GetClientListHandler.ResourceType,
            id.ToString());

        if (!permitted.IsSuccess)
        {
            return Result<ClientDetail>.Failure(permitted.Error);
        }

        Client? client = await context.Clients.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (client is null)
        {
            return ClientErrors.NotFound(id);
        }

        // A left join on live devices: a binding whose device has since been removed keeps
        // saying what it observed, and answers with no hostname rather than disappearing.
        var addresses = await (
            from binding in context.ClientIpBindings.AsNoTracking()
            join device in context.Devices.AsNoTracking().Where(row => row.DeletedAt == null)
                on binding.DeviceId equals device.Id into matched
            from device in matched.DefaultIfEmpty()
            where binding.ClientId == id && binding.ObservedTo == null
            orderby binding.ObservedFrom
            select new { binding, device })
            .ToListAsync(cancellationToken);

        // The interface name comes from device_interfaces, which a fingerprint walk fills in.
        // A device nothing has walked answers with an ifIndex and no name, which is the honest
        // state rather than a reason to hide the binding.
        var ports = await (
            from binding in context.ClientPortBindings.AsNoTracking()
            join device in context.Devices.AsNoTracking().Where(row => row.DeletedAt == null)
                on binding.DeviceId equals device.Id into matchedDevices
            from device in matchedDevices.DefaultIfEmpty()
            join port in context.DeviceInterfaces.AsNoTracking()
                on new { binding.DeviceId, binding.IfIndex } equals new { port.DeviceId, port.IfIndex }
                into matchedPorts
            from port in matchedPorts.DefaultIfEmpty()
            where binding.ClientId == id && binding.ObservedTo == null
            orderby binding.ObservedFrom
            select new { binding, device, port })
            .ToListAsync(cancellationToken);

        return client.ToDetail(
            [.. addresses.Select(row => row.binding.ToSummary(row.device?.Hostname))],
            [.. ports.Select(row => row.binding.ToSummary(
                row.device?.Hostname,
                row.port?.Name ?? row.port?.Description))]);
    }
}
