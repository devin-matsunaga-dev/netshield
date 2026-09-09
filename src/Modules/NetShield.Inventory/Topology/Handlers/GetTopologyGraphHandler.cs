using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Authorization;
using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Topology.Handlers;

/// <summary>
/// Serves one page of the topology graph: the devices to draw, the edges between them, and where
/// to put them.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The traversal is bounded twice and is for rendering only.</strong> A root is walked to
/// an explicit depth, at most <see cref="TopologyLimits.MaxGraphDepth"/>, and no read may produce
/// more than <see cref="TopologyLimits.MaxGraphNodes"/> nodes however the filters are set. What
/// comes out is a set of devices, a set of edges and a layout. Nothing here decides what is
/// reachable from what, what a device's failure would take with it, or what path traffic would
/// take: reachability analysis, blast-radius calculation and what-if simulation are on the
/// <c>SPEC.md</c> §3 Defer list, and this is not a small version of any of them
/// (WP-2.3, at the human's instruction).
/// </para>
/// <para>
/// <strong>A filter restricts the walk, not just the answer.</strong> Depth from a root is
/// measured through devices the filters admit, so <c>?vlanId=20&amp;rootDeviceId=…&amp;depth=1</c>
/// is "one hop through VLAN 20" rather than "one hop through anything, then discard". A node
/// reachable only through a device the filter excluded is on an island of its own, which is the
/// honest picture of what the filter asked for.
/// </para>
/// <para>
/// <strong>The whole graph is computed on every page.</strong> A component index and a rank are
/// properties of the whole graph rather than of a row, so they cannot be read a page at a time —
/// and a cursor that resumed from them would otherwise be resuming from something that had
/// changed meaning. At <c>SPEC.md</c> §1's 500 devices this is two indexed queries and some
/// arithmetic, and the WP-2.3 timing criterion measures exactly that repeated across every page
/// of the estate.
/// </para>
/// </remarks>
internal sealed class GetTopologyGraphHandler(InventoryDbContext context, IResourceGuard guard)
{
    /// <summary>What an audit or authorization refusal from this handler names.</summary>
    internal const string ResourceType = "topology-graph";

    public async Task<Result<TopologyGraph>> HandleAsync(
        TopologyGraphQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = guard.Require(Permission.TopologyRead, ResourceType);

        if (!permitted.IsSuccess)
        {
            return Result<TopologyGraph>.Failure(permitted.Error);
        }

        TopologyGraphCursor? position = null;

        if (query.Page.Cursor is { } cursor)
        {
            Result<TopologyGraphCursor> decoded = TopologyGraphCursor.Decode(cursor);

            if (!decoded.IsSuccess)
            {
                return Result<TopologyGraph>.Failure(decoded.Error);
            }

            position = decoded.Value;
        }

        IReadOnlyList<Guid>? vlanDevices = await VlanDevicesAsync(query, cancellationToken);

        if (query.RootDeviceId is { } root)
        {
            bool exists = await context.Devices.AsNoTracking()
                .AnyAsync(device => device.Id == root && device.DeletedAt == null, cancellationToken);

            if (!exists)
            {
                return TopologyErrors.GraphRootNotFound(root);
            }
        }

        (List<Guid> deviceIds, bool truncated) = query.RootDeviceId is { } from
            ? await WalkAsync(from, query, vlanDevices, cancellationToken)
            : await AdmittedAsync(query, vlanDevices, cancellationToken);

        List<GraphLayoutRule.Link> links = await LinksAsync(deviceIds, cancellationToken);

        GraphLayoutRule.Laid laid = GraphLayoutRule.Lay(
            deviceIds,
            links,
            query.RootDeviceId,
            LayoutGeometry.Default);

        return await PageAsync(query, position, laid, links, truncated, cancellationToken);
    }

    /// <summary>
    /// The devices carrying the VLAN the caller filtered on, or <see langword="null"/> when they
    /// did not filter on one.
    /// </summary>
    /// <remarks>
    /// The VLAN filter is a join and not a new read: a VLAN's devices are its nodes and the edges
    /// between them are the subgraph, so this reads <see cref="GetVlanListHandler.LiveRows"/> —
    /// the one place "a live VLAN row of a live device" is written — rather than growing a third
    /// copy of it (WP-2.2).
    /// </remarks>
    private async Task<IReadOnlyList<Guid>?> VlanDevicesAsync(
        TopologyGraphQuery query,
        CancellationToken cancellationToken)
    {
        if (query.VlanId is not { } vlanId)
        {
            return null;
        }

        return await GetVlanListHandler.LiveRows(context)
            .Where(row => row.VlanId == vlanId)
            .Select(row => row.DeviceId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    /// <summary>Every device the filters admit, bounded by the node ceiling.</summary>
    private async Task<(List<Guid> DeviceIds, bool Truncated)> AdmittedAsync(
        TopologyGraphQuery query,
        IReadOnlyList<Guid>? vlanDevices,
        CancellationToken cancellationToken)
    {
        List<Guid> ids = await Admitted(query, vlanDevices)
            .Select(device => device.Id)
            .OrderBy(id => id)
            .Take(TopologyLimits.MaxGraphNodes + 1)
            .ToListAsync(cancellationToken);

        if (ids.Count <= TopologyLimits.MaxGraphNodes)
        {
            return (ids, false);
        }

        return (ids.GetRange(0, TopologyLimits.MaxGraphNodes), true);
    }

    /// <summary>
    /// Walks out from a root, a hop at a time, to the depth the caller asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A loop of at most <see cref="TopologyLimits.MaxGraphDepth"/> bounded queries rather than
    /// one recursive one, so that every step is a plain indexed read of
    /// <c>device_adjacencies</c> and the walk can be stopped the moment the node budget is spent.
    /// A recursive CTE would be shorter and would put the bound somewhere a reader has to trust
    /// rather than somewhere they can see.
    /// </para>
    /// <para>
    /// Each hop's discoveries are filtered before they become the next frontier, so a device the
    /// site or VLAN filter excludes is not walked <em>through</em>. Depth therefore means hops
    /// within the filtered graph, which is the only reading under which a depth and a filter
    /// compose.
    /// </para>
    /// </remarks>
    private async Task<(List<Guid> DeviceIds, bool Truncated)> WalkAsync(
        Guid root,
        TopologyGraphQuery query,
        IReadOnlyList<Guid>? vlanDevices,
        CancellationToken cancellationToken)
    {
        // The root itself has to pass the filters. A caller who centres on a device that is not
        // in the VLAN they filtered on has asked for the intersection of two things that do not
        // meet, and an empty graph is what that is.
        bool admitted = await Admitted(query, vlanDevices)
            .AnyAsync(device => device.Id == root, cancellationToken);

        if (!admitted)
        {
            return ([], false);
        }

        HashSet<Guid> visited = [root];
        List<Guid> frontier = [root];

        // Set only where the budget actually stopped a walk that had somewhere left to go.
        // Running out of the caller's own depth is not truncation, and a graph that happens to
        // end on exactly the ceiling has not been cut.
        bool cut = false;

        for (int hop = 0; hop < (query.Depth ?? TopologyLimits.DefaultGraphDepth); hop++)
        {
            if (frontier.Count == 0)
            {
                break;
            }

            if (visited.Count >= TopologyLimits.MaxGraphNodes)
            {
                cut = true;
                break;
            }

            List<Guid> reached = await Neighbours(frontier)
                .Where(id => !visited.Contains(id))
                .Distinct()
                .ToListAsync(cancellationToken);

            if (reached.Count == 0)
            {
                break;
            }

            List<Guid> admittedNext = await Admitted(query, vlanDevices)
                .Where(device => reached.Contains(device.Id))
                .Select(device => device.Id)
                .OrderBy(id => id)
                .ToListAsync(cancellationToken);

            frontier = [];

            foreach (Guid id in admittedNext)
            {
                if (visited.Count >= TopologyLimits.MaxGraphNodes)
                {
                    cut = true;
                    break;
                }

                if (visited.Add(id))
                {
                    frontier.Add(id);
                }
            }

            if (cut)
            {
                break;
            }
        }

        return ([.. visited.Order()], cut);
    }

    /// <summary>The devices on the far side of a live edge from any of these.</summary>
    private IQueryable<Guid> Neighbours(List<Guid> frontier) =>
        context.DeviceAdjacencies.AsNoTracking()
            .Where(edge => edge.WithdrawnAt == null && edge.BDeviceId != null)
            .Where(edge => frontier.Contains(edge.ADeviceId) || frontier.Contains(edge.BDeviceId!.Value))
            .Select(edge => frontier.Contains(edge.ADeviceId) ? edge.BDeviceId!.Value : edge.ADeviceId);

    /// <summary>
    /// The live devices the filters admit. The one place the graph's node predicate is written.
    /// </summary>
    /// <remarks>
    /// The site match is <c>ILike</c> against the whole value, which is exactly what the device
    /// list does — so the two screens agree about which devices are at a site, and the note in
    /// <c>STATUS.md</c> about one site having two spellings has one answer rather than two.
    /// </remarks>
    private IQueryable<Devices.Device> Admitted(
        TopologyGraphQuery query,
        IReadOnlyList<Guid>? vlanDevices)
    {
        IQueryable<Devices.Device> devices = context.Devices.AsNoTracking()
            .Where(device => device.DeletedAt == null);

        if (query.Site is { } site)
        {
            devices = devices.Where(device => device.Site != null && EF.Functions.ILike(device.Site, site));
        }

        if (vlanDevices is not null)
        {
            devices = devices.Where(device => vlanDevices.Contains(device.Id));
        }

        return devices;
    }

    /// <summary>Every live edge with both endpoints among these devices.</summary>
    private async Task<List<GraphLayoutRule.Link>> LinksAsync(
        List<Guid> deviceIds,
        CancellationToken cancellationToken)
    {
        if (deviceIds.Count == 0)
        {
            return [];
        }

        var rows = await context.DeviceAdjacencies.AsNoTracking()
            .Where(edge => edge.WithdrawnAt == null
                && edge.BDeviceId != null
                && deviceIds.Contains(edge.ADeviceId)
                && deviceIds.Contains(edge.BDeviceId!.Value))
            .Select(edge => new { edge.Id, edge.ADeviceId, BDeviceId = edge.BDeviceId!.Value })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new GraphLayoutRule.Link(row.Id, row.ADeviceId, row.BDeviceId))];
    }

    /// <summary>Cuts the laid-out graph to one page and fills in what a caller reads.</summary>
    private async Task<Result<TopologyGraph>> PageAsync(
        TopologyGraphQuery query,
        TopologyGraphCursor? position,
        GraphLayoutRule.Laid laid,
        List<GraphLayoutRule.Link> links,
        bool truncated,
        CancellationToken cancellationToken)
    {
        List<GraphLayoutRule.Placement> after =
        [
            .. position is null
                ? laid.Nodes
                : laid.Nodes.Where(node => position.Precedes(node.ComponentIndex, node.Rank, node.DeviceId))
        ];

        bool more = after.Count > query.Page.Limit;
        List<GraphLayoutRule.Placement> page = more ? after.GetRange(0, query.Page.Limit) : after;

        string? nextCursor = more
            ? Cursor.Encode(TopologyGraphCursor.Compose(
                page[^1].ComponentIndex,
                page[^1].Rank,
                page[^1].DeviceId))
            : null;

        List<Guid> ids = [.. page.Select(node => node.DeviceId)];

        IReadOnlyDictionary<Guid, DeviceFacts> facts = await FactsAsync(ids, cancellationToken);
        IReadOnlyDictionary<Guid, int> external = await ExternalEdgeCountsAsync(ids, cancellationToken);

        List<TopologyGraphNode> nodes = [];

        foreach (GraphLayoutRule.Placement placement in page)
        {
            if (!facts.TryGetValue(placement.DeviceId, out DeviceFacts? device))
            {
                // Removed between the layout and this read. Dropping it is right: a node with no
                // hostname and no state is not something a canvas can draw.
                continue;
            }

            nodes.Add(new TopologyGraphNode(
                placement.DeviceId,
                device.Hostname,
                device.Vendor,
                device.Role,
                device.Site,
                device.State,
                placement.ComponentIndex,
                placement.Rank,
                placement.Degree,
                external.TryGetValue(placement.DeviceId, out int count) ? count : 0,
                placement.X,
                placement.Y));
        }

        IReadOnlyList<TopologyGraphEdge> edges = await EdgesAsync(
            laid,
            links,
            page,
            [.. nodes.Select(node => node.DeviceId)],
            cancellationToken);

        return new TopologyGraph(
            nodes,
            edges,
            [.. laid.Components.Select(component => new TopologyGraphComponent(
                component.Index,
                component.RootDeviceId,
                component.NodeCount,
                component.EdgeCount,
                component.Depth))],
            LayoutGeometry.Default.ToContract(),
            nextCursor,
            laid.Nodes.Count,
            links.Count,
            truncated);
    }

    /// <summary>
    /// The edges belonging to this page: those whose earlier endpoint is on it.
    /// </summary>
    /// <remarks>
    /// "Earlier" is in the graph's own node ordering, so an edge is returned exactly once across
    /// every page and is never dropped for straddling a boundary. The two <c>included</c> flags
    /// say which of its endpoints this page actually carries, which is what lets a client draw
    /// what it has and hold the rest until the next page arrives.
    /// </remarks>
    private async Task<IReadOnlyList<TopologyGraphEdge>> EdgesAsync(
        GraphLayoutRule.Laid laid,
        List<GraphLayoutRule.Link> links,
        List<GraphLayoutRule.Placement> page,
        IReadOnlyCollection<Guid> returned,
        CancellationToken cancellationToken)
    {
        if (page.Count == 0 || links.Count == 0)
        {
            return [];
        }

        Dictionary<Guid, int> ordinals = [];

        for (int index = 0; index < laid.Nodes.Count; index++)
        {
            ordinals[laid.Nodes[index].DeviceId] = index;
        }

        // The nodes actually returned rather than the ones the layout placed. The two differ
        // only where a device was removed between the two reads, and a flag saying "you have this
        // one" about a node the caller was not given would be the one thing it must not say.
        HashSet<Guid> onPage = [.. returned];
        Dictionary<Guid, int> components = page.ToDictionary(
            node => node.DeviceId,
            node => node.ComponentIndex);

        int first = ordinals[page[0].DeviceId];
        int last = ordinals[page[^1].DeviceId];

        List<Guid> wanted =
        [
            .. links
                .Where(link => ordinals.TryGetValue(link.ADeviceId, out int a)
                    && ordinals.TryGetValue(link.BDeviceId, out int b)
                    && Math.Min(a, b) >= first
                    && Math.Min(a, b) <= last)
                .Select(link => link.Id)
        ];

        if (wanted.Count == 0)
        {
            return [];
        }

        List<DeviceAdjacency> rows = await context.DeviceAdjacencies.AsNoTracking()
            .Where(edge => wanted.Contains(edge.Id))
            .OrderBy(edge => edge.Id)
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(edge => edge.ToGraphEdge(
                components.TryGetValue(edge.ADeviceId, out int component)
                    ? component
                    : components[edge.BDeviceId!.Value],
                onPage.Contains(edge.ADeviceId),
                onPage.Contains(edge.BDeviceId!.Value)))
        ];
    }

    /// <summary>What the nodes on this page are called, and what state they are in.</summary>
    private async Task<IReadOnlyDictionary<Guid, DeviceFacts>> FactsAsync(
        List<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, DeviceFacts>();
        }

        List<DeviceFacts> rows = await context.Devices.AsNoTracking()
            .Where(device => ids.Contains(device.Id) && device.DeletedAt == null)
            .Select(device => new DeviceFacts(
                device.Id,
                device.Hostname,
                device.Vendor,
                device.Role,
                device.Site,
                device.State))
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.Id);
    }

    /// <summary>
    /// How many live edges each of these devices has to something that is not a live monitored
    /// device.
    /// </summary>
    /// <remarks>
    /// A count rather than a node, because there is nothing to draw at the far end: an unmanaged
    /// switch or a server that speaks LLDP has no state, no site and no VLAN, so none of this
    /// endpoint's filters could match it and a tile for it would be a shape with no facts behind
    /// it. The edge itself is still on <c>GET /api/v1/devices/{id}/adjacencies</c> in full.
    /// </remarks>
    private async Task<IReadOnlyDictionary<Guid, int>> ExternalEdgeCountsAsync(
        List<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var fromA = await context.DeviceAdjacencies.AsNoTracking()
            .Where(edge => edge.WithdrawnAt == null
                && ids.Contains(edge.ADeviceId)
                && (edge.BDeviceId == null
                    || !context.Devices.Any(device =>
                        device.Id == edge.BDeviceId && device.DeletedAt == null)))
            .GroupBy(edge => edge.ADeviceId)
            .Select(group => new { DeviceId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        // The mirror case: an edge whose A end is a device an operator has since removed. Its
        // rows survive as history and the live device at the other end still has a link to
        // something the estate no longer monitors.
        var fromB = await context.DeviceAdjacencies.AsNoTracking()
            .Where(edge => edge.WithdrawnAt == null
                && edge.BDeviceId != null
                && ids.Contains(edge.BDeviceId!.Value)
                && !context.Devices.Any(device =>
                    device.Id == edge.ADeviceId && device.DeletedAt == null))
            .GroupBy(edge => edge.BDeviceId!.Value)
            .Select(group => new { DeviceId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        Dictionary<Guid, int> counts = fromA.ToDictionary(row => row.DeviceId, row => row.Count);

        foreach (var row in fromB)
        {
            counts[row.DeviceId] = counts.TryGetValue(row.DeviceId, out int existing)
                ? existing + row.Count
                : row.Count;
        }

        return counts;
    }

    /// <summary>What a node needs from its device row, and nothing else.</summary>
    private sealed record DeviceFacts(
        Guid Id,
        string Hostname,
        DeviceVendor Vendor,
        DeviceRole Role,
        string? Site,
        DeviceState State);
}
