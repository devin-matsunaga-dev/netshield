using System.Net;

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
/// Serves one page of the client list, most recently seen first.
/// </summary>
/// <remarks>
/// <para>
/// The current address and port are joined from the open intervals rather than read off the
/// client row, because nothing current is stored on the client — the open interval <em>is</em>
/// the current binding, and a denormalised copy beside it would be a second answer to a question
/// that already has one.
/// </para>
/// <para>
/// A client with several open port bindings — which is every client, once its access switch and
/// the switches above it have all been walked — shows the port that has learned the fewest
/// addresses. That is not a guess at the topology: it is the evidence ordered by how much of it
/// there is, and the access port is the one where the switch learned one MAC while the uplink
/// learned every MAC behind it. The client's own detail shows all of them.
/// </para>
/// </remarks>
internal sealed class GetClientListHandler(InventoryDbContext context, IResourceGuard guard)
{
    /// <summary>What an authorization refusal names as the resource being read.</summary>
    internal const string ResourceType = "client";

    public async Task<Result<CursorPage<ClientSummary>>> HandleAsync(
        ClientListQuery query,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);

        // SPEC.md §2 and the permission's own definition put clients beside devices and sites
        // under InventoryRead, so nothing new is being granted here.
        Result permitted = guard.Require(Permission.InventoryRead, ResourceType);

        if (!permitted.IsSuccess)
        {
            return Result<CursorPage<ClientSummary>>.Failure(permitted.Error);
        }

        IQueryable<Client> clients = Filter(context.Clients.AsNoTracking(), query);


        long totalCount = await clients.LongCountAsync(cancellationToken);

        if (page.Cursor is { } cursor)
        {
            Result<ClientCursor> position = ClientCursor.Decode(cursor);

            if (!position.IsSuccess)
            {
                return Result<CursorPage<ClientSummary>>.Failure(position.Error);
            }

            DateTimeOffset from = position.Value.LastSeenAt;
            Guid id = position.Value.Id;

            // Descending, so the page continues with rows *older* than the cursor — and the id
            // breaks the tie inside one walk's worth of identical timestamps.
            clients = clients.Where(client =>
                client.LastSeenAt < from || (client.LastSeenAt == from && client.Id.CompareTo(id) < 0));
        }

        List<Client> rows = await clients
            .OrderByDescending(client => client.LastSeenAt)
            .ThenByDescending(client => client.Id)
            .Take(page.FetchLimit)
            .ToListAsync(cancellationToken);

        CursorPage<Client> result = rows.ToCursorPage(
            page,
            client => ClientCursor.Compose(client.LastSeenAt, client.Id),
            totalCount);

        IReadOnlyList<ClientSummary> items =
            await SummariseAsync([.. result.Items], cancellationToken);

        return new CursorPage<ClientSummary>(items, result.NextCursor, result.TotalCount);
    }

    /// <summary>Joins each client on the page to its current address and port.</summary>
    /// <remarks>
    /// Two queries for the page rather than two per row. At the 5,000-client target scale a page
    /// is at most 200 rows, and an <c>IN</c> over that is one index scan against however many
    /// round trips the obvious loop would have been.
    /// </remarks>
    private async Task<IReadOnlyList<ClientSummary>> SummariseAsync(
        IReadOnlyList<Client> clients,
        CancellationToken cancellationToken)
    {
        if (clients.Count == 0)
        {
            return [];
        }

        List<Guid> ids = [.. clients.Select(client => client.Id)];

        Dictionary<Guid, ClientIpBinding> addresses = await context.ClientIpBindings.AsNoTracking()
            .Where(binding => binding.ObservedTo == null && ids.Contains(binding.ClientId))
            .OrderByDescending(binding => binding.ObservedFrom)
            .GroupBy(binding => binding.ClientId)
            .Select(group => group.First())
            .ToDictionaryAsync(binding => binding.ClientId, cancellationToken);

        var ports = await (
            from binding in context.ClientPortBindings.AsNoTracking()
            join device in context.Devices.AsNoTracking() on binding.DeviceId equals device.Id
            where binding.ObservedTo == null && ids.Contains(binding.ClientId) && device.DeletedAt == null
            select new { binding, device.Hostname })
            .ToListAsync(cancellationToken);

        // The fewest-addresses port first, which is the access port wherever a switch has told
        // us how many it learned; ties fall back to the oldest binding, so the answer is stable
        // between two walks that changed nothing.
        Dictionary<Guid, (ClientPortBinding Binding, string Hostname)> byClient = ports
            .GroupBy(row => row.binding.ClientId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var chosen = group
                        .OrderBy(row => row.binding.MacCountOnPort ?? int.MaxValue)
                        .ThenBy(row => row.binding.ObservedFrom)
                        .First();

                    return (chosen.binding, chosen.Hostname);
                });

        return
        [
            .. clients.Select(client =>
            {
                addresses.TryGetValue(client.Id, out ClientIpBinding? address);

                return byClient.TryGetValue(client.Id, out (ClientPortBinding Binding, string Hostname) port)
                    ? client.ToSummary(address, port.Binding, port.Hostname)
                    : client.ToSummary(address, null, null);
            })
        ];
    }

    /// <summary>
    /// Applies the filters. Each is a separate <c>Where</c> so that composing several narrows
    /// rather than replacing, which is what the URL-state filters on the screen depend on.
    /// </summary>
    /// <remarks>
    /// The binding filters are correlated subqueries rather than joins over navigation
    /// properties, because there are none: <c>Client</c> declares no collection of its bindings.
    /// That is deliberate — an entity with a navigation is one somebody eventually loads whole,
    /// and a client's history is unbounded where its row is not.
    /// </remarks>
    private IQueryable<Client> Filter(IQueryable<Client> clients, ClientListQuery query)
    {
        if (query.SeenSince is { } since)
        {
            clients = clients.Where(client => client.LastSeenAt >= since);
        }

        if (query.OnlyActive)
        {
            clients = clients.Where(client => context.ClientIpBindings.Any(binding =>
                binding.ClientId == client.Id && binding.ObservedTo == null));
        }

        if (query.DeviceId is { } deviceId)
        {
            clients = clients.Where(client => context.ClientPortBindings.Any(binding =>
                binding.ClientId == client.Id
                && binding.DeviceId == deviceId
                && binding.ObservedTo == null));
        }

        if (query.VlanId is { } vlanId)
        {
            clients = clients.Where(client => context.ClientPortBindings.Any(binding =>
                binding.ClientId == client.Id
                && binding.VlanId == vlanId
                && binding.ObservedTo == null));
        }

        return string.IsNullOrWhiteSpace(query.Search) ? clients : Search(clients, query.Search.Trim());
    }

    /// <summary>
    /// One box over three kinds of value: a MAC however it was spelled, an address, or a
    /// hostname prefix.
    /// </summary>
    /// <remarks>
    /// The kind is decided from the value rather than from a second control the reader has to
    /// set. A value that normalises as a MAC is matched exactly, because a partial hardware
    /// address is not a thing anybody searches for; an address is matched exactly against the
    /// current bindings, because <c>inet</c> equality is what the index supports and a substring
    /// match on an address is how <c>10.0.0.1</c> comes to find <c>110.0.0.10</c>.
    /// </remarks>
    private IQueryable<Client> Search(IQueryable<Client> clients, string search)
    {
        if (MacAddress.Normalize(search) is { } mac)
        {
            return clients.Where(client => client.MacAddress == mac);
        }

        if (IPAddress.TryParse(search, out IPAddress? address))
        {
            return clients.Where(client => context.ClientIpBindings.Any(binding =>
                binding.ClientId == client.Id
                && binding.ObservedTo == null
                && binding.IpAddress.Equals(address)));
        }

        return clients.Where(client =>
            client.Hostname != null && client.Hostname.StartsWith(search));
    }
}
