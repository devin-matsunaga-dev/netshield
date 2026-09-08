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
/// Serves one page of a client's address history, newest interval first.
/// </summary>
/// <remarks>
/// Paginated, which matters rather than being ceremony: a device on a short DHCP lease in a
/// guest VLAN accumulates an interval every time it reconnects, and a year of that is thousands
/// of rows for one client (CONVENTIONS.md §4 lets no endpoint return an unbounded collection).
/// </remarks>
internal sealed class GetClientIpHistoryHandler(InventoryDbContext context, IResourceGuard guard)
{
    public async Task<Result<CursorPage<ClientIpBindingSummary>>> HandleAsync(
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
            return Result<CursorPage<ClientIpBindingSummary>>.Failure(permitted.Error);
        }

        // A page of nothing and a page of a client that does not exist are different answers,
        // and only one of them tells the caller they asked the wrong question.
        if (!await context.Clients.AsNoTracking().AnyAsync(client => client.Id == clientId, cancellationToken))
        {
            return ClientErrors.NotFound(clientId);
        }

        IQueryable<ClientIpBinding> bindings = context.ClientIpBindings.AsNoTracking()
            .Where(binding => binding.ClientId == clientId);

        long totalCount = await bindings.LongCountAsync(cancellationToken);

        if (page.Cursor is { } cursor)
        {
            Result<ClientBindingCursor> position = ClientBindingCursor.Decode(cursor);

            if (!position.IsSuccess)
            {
                return Result<CursorPage<ClientIpBindingSummary>>.Failure(position.Error);
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
                on binding.DeviceId equals device.Id into matched
            from device in matched.DefaultIfEmpty()
            orderby binding.ObservedFrom descending, binding.Id descending
            select new { binding, device })
            .Take(page.FetchLimit)
            .ToListAsync(cancellationToken);

        CursorPage<ClientIpBindingSummary> result = rows
            .Select(row => row.binding.ToSummary(row.device?.Hostname))
            .ToList()
            .ToCursorPage(
                page,
                summary => ClientBindingCursor.Compose(summary.ObservedFrom, summary.Id),
                totalCount);

        return result;
    }
}
