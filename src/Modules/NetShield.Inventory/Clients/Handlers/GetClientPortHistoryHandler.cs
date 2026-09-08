using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Paging;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Authorization;
using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Clients.Handlers;

/// <summary>
/// Serves one page of a client's port history, newest interval first.
/// </summary>
/// <remarks>
/// The same shape as the address history, over the other table. A reader walking this is usually
/// answering "where was this thing plugged in on Tuesday", which is the question the port
/// interval exists to make answerable at all.
/// </remarks>
internal sealed class GetClientPortHistoryHandler(InventoryDbContext context, IResourceGuard guard)
{
    public async Task<Result<CursorPage<ClientPortBindingSummary>>> HandleAsync(
        Guid clientId,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        Result permitted = guard.Require(
            Permission.InventoryRead,
            GetClientListHandler.ResourceType,
            clientId.ToString());

        if (!permitted.IsSuccess)
        {
            return Result<CursorPage<ClientPortBindingSummary>>.Failure(permitted.Error);
        }

        if (!await context.Clients.AsNoTracking().AnyAsync(client => client.Id == clientId, cancellationToken))
        {
            return ClientErrors.NotFound(clientId);
        }

        IQueryable<ClientPortBinding> bindings = context.ClientPortBindings.AsNoTracking()
            .Where(binding => binding.ClientId == clientId);

        long totalCount = await bindings.LongCountAsync(cancellationToken);

        if (page.Cursor is { } cursor)
        {
            Result<ClientBindingCursor> position = ClientBindingCursor.Decode(cursor);

            if (!position.IsSuccess)
            {
                return Result<CursorPage<ClientPortBindingSummary>>.Failure(position.Error);
            }

            DateTimeOffset from = position.Value.ObservedFrom;
            Guid id = position.Value.Id;

            bindings = bindings.Where(binding =>
                binding.ObservedFrom < from
                || (binding.ObservedFrom == from && binding.Id.CompareTo(id) < 0));
        }

        var rows = await (
            from binding in bindings
            join device in context.Devices.AsNoTracking().Where(row => row.DeletedAt == null)
                on binding.DeviceId equals device.Id into matchedDevices
            from device in matchedDevices.DefaultIfEmpty()
            join port in context.DeviceInterfaces.AsNoTracking()
                on new { binding.DeviceId, binding.IfIndex } equals new { port.DeviceId, port.IfIndex }
                into matchedPorts
            from port in matchedPorts.DefaultIfEmpty()
            orderby binding.ObservedFrom descending, binding.Id descending
            select new { binding, device, port })
            .Take(page.FetchLimit)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => row.binding.ToSummary(
                row.device?.Hostname,
                row.port?.Name ?? row.port?.Description))
            .ToList()
            .ToCursorPage(
                page,
                summary => ClientBindingCursor.Compose(summary.ObservedFrom, summary.Id),
                totalCount);
    }
}
