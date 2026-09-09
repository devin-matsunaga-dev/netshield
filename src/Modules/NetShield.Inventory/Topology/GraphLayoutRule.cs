namespace NetShield.Inventory.Topology;

/// <summary>
/// The decision WP-2.3 turns on, as one pure function over values: given a set of devices and the
/// edges between them, which islands they fall into, how far each device is from its island's
/// root, and where to draw it.
/// </summary>
/// <remarks>
/// <para>
/// The shape WP-1.8 gave <c>AssetResolutionRule</c>, WP-2.1 gave <c>AdjacencyRule</c> and WP-2.2
/// gave <c>VlanAggregationRule</c>, for the same reason: <em>"filters reduce the result
/// correctly, and an unreachable segment is returned as a disconnected component rather than
/// dropped"</em> is what this package is measured against, and a decomposition and a layout are
/// arithmetic. Tested as arithmetic, they can be shown to be deterministic; tested only through a
/// round trip, they can be shown to work once.
/// </para>
/// <para>
/// <strong>The traversal is for drawing and for nothing else.</strong> It reads
/// <c>device_adjacencies</c>, it is bounded by an explicit depth and an explicit node ceiling,
/// and its whole output is a component index, a rank and a pair of coordinates. It does not
/// decide what is reachable from what, what a failure would take down with it, or what path
/// traffic would take — reachability analysis, blast-radius calculation and what-if simulation
/// are all on the <c>SPEC.md</c> §3 Defer list, and none of them is this with a different name.
/// </para>
/// <para>
/// <strong>Layered, in the shape <c>dagre</c> lays a graph out.</strong> Rank by breadth-first
/// distance from a root, order within a rank by repeated barycentre passes over the neighbouring
/// ranks, then coordinates from the geometry <c>DESIGN.md</c> §6 fixes for a node tile. No
/// dependency: this is a hundred lines of arithmetic, and there is no <c>dagre</c> for .NET to
/// take instead. A client that would rather run the real one still can — the ranks and the edges
/// are in the response beside the coordinates.
/// </para>
/// <para>
/// Nothing here reads a database, a clock or a configuration value. Given the same nodes and
/// links it returns the same answer on any machine, whatever order they arrive in — which is what
/// makes the coordinates safe to page, since every page recomputes them.
/// </para>
/// </remarks>
internal static class GraphLayoutRule
{
    /// <summary>How many barycentre passes to make over the ranks.</summary>
    /// <remarks>
    /// Four is where the crossing count stops improving on the estates this was measured against,
    /// and the cost of a pass is one sort per rank. It is a constant rather than an option
    /// because a layout that differed between two deployments would be a layout nobody could
    /// reproduce from a screenshot.
    /// </remarks>
    private const int OrderingPasses = 4;

    /// <summary>One edge, reduced to what the layout depends on.</summary>
    /// <param name="Id">The adjacency row.</param>
    /// <param name="ADeviceId">One endpoint.</param>
    /// <param name="BDeviceId">The other.</param>
    internal readonly record struct Link(Guid Id, Guid ADeviceId, Guid BDeviceId);

    /// <summary>Where one node ended up.</summary>
    /// <param name="DeviceId">The device.</param>
    /// <param name="ComponentIndex">Which island it is on.</param>
    /// <param name="Rank">How many hops from that island's root. The root is zero.</param>
    /// <param name="OrderInRank">Its position left to right within its rank.</param>
    /// <param name="Degree">How many links of this graph are incident to it.</param>
    /// <param name="X">Horizontal position, in layout units.</param>
    /// <param name="Y">Vertical position. Rank zero is at the top.</param>
    internal readonly record struct Placement(
        Guid DeviceId,
        int ComponentIndex,
        int Rank,
        int OrderInRank,
        int Degree,
        double X,
        double Y);

    /// <summary>One island of the graph.</summary>
    /// <param name="Index">Its position in the component ordering.</param>
    /// <param name="RootDeviceId">The device its ranks are measured from.</param>
    /// <param name="NodeCount">How many nodes it holds.</param>
    /// <param name="EdgeCount">How many links it holds.</param>
    /// <param name="Depth">Its greatest rank.</param>
    internal readonly record struct Component(
        int Index,
        Guid RootDeviceId,
        int NodeCount,
        int EdgeCount,
        int Depth);

    /// <summary>The whole laid-out graph.</summary>
    /// <param name="Nodes">Every node, in the graph's node ordering: component, then rank, then device id.</param>
    /// <param name="Components">Every island, ordered largest first.</param>
    internal sealed record Laid(
        IReadOnlyList<Placement> Nodes,
        IReadOnlyList<Component> Components);

    /// <summary>
    /// Decomposes a set of devices and the links between them into islands, ranks each island
    /// from a root, and places every node.
    /// </summary>
    /// <param name="deviceIds">Every node of the graph. A device with no link is an island of one.</param>
    /// <param name="links">The edges. A link naming a device not in <paramref name="deviceIds"/> is ignored.</param>
    /// <param name="requestedRoot">
    /// The device the caller asked to centre on, where they asked. Its own island ranks from it;
    /// every other island still ranks from its own most-connected device, because a rank has to
    /// mean something on an island the requested root is not on.
    /// </param>
    /// <param name="geometry">The node and gap sizes to place with.</param>
    internal static Laid Lay(
        IReadOnlyCollection<Guid> deviceIds,
        IReadOnlyCollection<Link> links,
        Guid? requestedRoot,
        LayoutGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(deviceIds);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(geometry);

        if (deviceIds.Count == 0)
        {
            return new Laid([], []);
        }

        HashSet<Guid> present = [.. deviceIds];

        // Distinct neighbours for the traversal, and a separate count for the degree: two cables
        // between one pair of switches are two edges (WP-2.1 canonicalises on the port for that
        // reason) and they are still one adjacency for the purpose of ranking.
        Dictionary<Guid, SortedSet<Guid>> neighbours = [];
        Dictionary<Guid, int> degrees = [];

        foreach (Guid deviceId in present)
        {
            neighbours[deviceId] = [];
            degrees[deviceId] = 0;
        }

        foreach (Link link in links)
        {
            if (link.ADeviceId == link.BDeviceId
                || !present.Contains(link.ADeviceId)
                || !present.Contains(link.BDeviceId))
            {
                continue;
            }

            neighbours[link.ADeviceId].Add(link.BDeviceId);
            neighbours[link.BDeviceId].Add(link.ADeviceId);
            degrees[link.ADeviceId]++;
            degrees[link.BDeviceId]++;
        }

        List<Island> islands = Decompose(present, neighbours, degrees, requestedRoot);

        // Largest first, so the estate's main body is component 0 on every request and an island
        // of one sorts to the end. The root breaks the tie because two islands of equal size have
        // to be ordered by something that cannot move between requests.
        islands.Sort(static (left, right) => right.Nodes.Count != left.Nodes.Count
            ? right.Nodes.Count.CompareTo(left.Nodes.Count)
            : left.Root.CompareTo(right.Root));

        return Place(islands, links, present, degrees, geometry);
    }

    /// <summary>An island, before it has been ordered against the others.</summary>
    private sealed record Island(Guid Root, List<Guid> Nodes, Dictionary<Guid, int> Ranks)
    {
        internal int Depth => Ranks.Count == 0 ? 0 : Ranks.Values.Max();
    }

    /// <summary>
    /// Splits the graph into islands and ranks each from its own root.
    /// </summary>
    /// <remarks>
    /// The outer loop walks the devices in id order so that two runs over the same estate
    /// discover the same islands in the same order, whatever order the database returned the
    /// rows in.
    /// </remarks>
    private static List<Island> Decompose(
        HashSet<Guid> present,
        Dictionary<Guid, SortedSet<Guid>> neighbours,
        Dictionary<Guid, int> degrees,
        Guid? requestedRoot)
    {
        List<Island> islands = [];
        HashSet<Guid> seen = [];

        foreach (Guid start in present.Order())
        {
            if (!seen.Add(start))
            {
                continue;
            }

            // Everything reachable from this device, which is this island. Collected before it is
            // ranked, because the root is chosen from the whole island rather than from wherever
            // the sweep happened to enter it.
            List<Guid> members = [start];
            Queue<Guid> pending = new();
            pending.Enqueue(start);

            while (pending.Count > 0)
            {
                foreach (Guid next in neighbours[pending.Dequeue()])
                {
                    if (seen.Add(next))
                    {
                        members.Add(next);
                        pending.Enqueue(next);
                    }
                }
            }

            Guid root = ChooseRoot(members, degrees, requestedRoot);

            islands.Add(new Island(root, members, Rank(root, neighbours)));
        }

        return islands;
    }

    /// <summary>
    /// The device an island's ranks are measured from.
    /// </summary>
    /// <remarks>
    /// The caller's root where the caller named one on this island; otherwise the most-connected
    /// device, which on a real estate is the core switch and is what puts the core at the top.
    /// The lowest device id breaks the tie, because "whichever the query returned first" is not a
    /// rule and would move the whole picture between two requests.
    /// </remarks>
    private static Guid ChooseRoot(
        List<Guid> members,
        Dictionary<Guid, int> degrees,
        Guid? requestedRoot)
    {
        if (requestedRoot is { } requested && members.Contains(requested))
        {
            return requested;
        }

        Guid best = members[0];

        foreach (Guid candidate in members)
        {
            if (degrees[candidate] > degrees[best]
                || (degrees[candidate] == degrees[best] && candidate < best))
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>Breadth-first distance from the root to every device on its island.</summary>
    private static Dictionary<Guid, int> Rank(Guid root, Dictionary<Guid, SortedSet<Guid>> neighbours)
    {
        Dictionary<Guid, int> ranks = new() { [root] = 0 };
        Queue<Guid> pending = new();
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            Guid current = pending.Dequeue();

            foreach (Guid next in neighbours[current])
            {
                if (ranks.TryAdd(next, ranks[current] + 1))
                {
                    pending.Enqueue(next);
                }
            }
        }

        return ranks;
    }

    /// <summary>Orders every island's ranks and turns the result into coordinates.</summary>
    private static Laid Place(
        List<Island> islands,
        IReadOnlyCollection<Link> links,
        HashSet<Guid> present,
        Dictionary<Guid, int> degrees,
        LayoutGeometry geometry)
    {
        Dictionary<Guid, int> componentOf = [];

        for (int index = 0; index < islands.Count; index++)
        {
            foreach (Guid deviceId in islands[index].Nodes)
            {
                componentOf[deviceId] = index;
            }
        }

        int[] edgeCounts = new int[islands.Count];

        foreach (Link link in links)
        {
            if (present.Contains(link.ADeviceId)
                && present.Contains(link.BDeviceId)
                && componentOf.TryGetValue(link.ADeviceId, out int component))
            {
                edgeCounts[component]++;
            }
        }

        List<Placement> placements = [];
        List<Component> components = new(islands.Count);

        double top = 0;

        for (int index = 0; index < islands.Count; index++)
        {
            Island island = islands[index];

            List<List<Guid>> ranks = Order(island, componentOf, links, present);

            double widest = ranks.Max(rank => Width(rank.Count, geometry));

            for (int rank = 0; rank < ranks.Count; rank++)
            {
                List<Guid> row = ranks[rank];

                // Each rank is centred against the island's widest, so a three-node rank under a
                // seven-node one sits under its middle rather than against its left edge.
                double left = (widest - Width(row.Count, geometry)) / 2;
                double y = top + (rank * (geometry.NodeHeight + geometry.RankSeparation));

                for (int position = 0; position < row.Count; position++)
                {
                    placements.Add(new Placement(
                        row[position],
                        index,
                        rank,
                        position,
                        degrees[row[position]],
                        left + (position * (geometry.NodeWidth + geometry.NodeSeparation)),
                        y));
                }
            }

            components.Add(new Component(
                index,
                island.Root,
                island.Nodes.Count,
                edgeCounts[index],
                island.Depth));

            top += Height(ranks.Count, geometry) + geometry.ComponentSeparation;
        }

        // The node ordering a cursor resumes from, and the order the nodes are returned in:
        // component, then rank, then device id. Not the drawing order within a rank, because a
        // barycentre position is not stable enough to be a keyset — it changes when a node
        // somewhere else in the graph does, and a cursor has to name a row that will still be in
        // the same place on the next request.
        placements.Sort(static (left, right) =>
        {
            int byComponent = left.ComponentIndex.CompareTo(right.ComponentIndex);

            if (byComponent != 0)
            {
                return byComponent;
            }

            int byRank = left.Rank.CompareTo(right.Rank);

            return byRank != 0 ? byRank : left.DeviceId.CompareTo(right.DeviceId);
        });

        return new Laid(placements, components);
    }

    /// <summary>
    /// Orders each rank of one island so that edges between ranks cross as little as possible.
    /// </summary>
    /// <remarks>
    /// The barycentre heuristic, which is what <c>dagre</c> does: a node is drawn near the mean
    /// position of the nodes it connects to in the rank above, then in the rank below, repeatedly.
    /// A node with no neighbour in the rank being swept against keeps its place rather than being
    /// pushed to one end. The starting order is by device id, and every tie is broken by the
    /// previous position, so the result depends on nothing but the graph.
    /// </remarks>
    private static List<List<Guid>> Order(
        Island island,
        Dictionary<Guid, int> componentOf,
        IReadOnlyCollection<Link> links,
        HashSet<Guid> present)
    {
        int depth = island.Depth;
        List<List<Guid>> ranks = [];

        for (int rank = 0; rank <= depth; rank++)
        {
            ranks.Add([.. island.Nodes.Where(node => island.Ranks[node] == rank).Order()]);
        }

        Dictionary<Guid, List<Guid>> adjacent = [];

        foreach (Guid node in island.Nodes)
        {
            adjacent[node] = [];
        }

        foreach (Link link in links)
        {
            if (!present.Contains(link.ADeviceId)
                || !present.Contains(link.BDeviceId)
                || link.ADeviceId == link.BDeviceId
                || componentOf[link.ADeviceId] != componentOf[link.BDeviceId]
                || !adjacent.ContainsKey(link.ADeviceId))
            {
                continue;
            }

            adjacent[link.ADeviceId].Add(link.BDeviceId);
            adjacent[link.BDeviceId].Add(link.ADeviceId);
        }

        for (int pass = 0; pass < OrderingPasses; pass++)
        {
            for (int rank = 1; rank <= depth; rank++)
            {
                Sweep(ranks, rank, rank - 1, adjacent, island.Ranks);
            }

            for (int rank = depth - 1; rank >= 0; rank--)
            {
                Sweep(ranks, rank, rank + 1, adjacent, island.Ranks);
            }
        }

        return ranks;
    }

    /// <summary>Re-orders one rank against a neighbouring one.</summary>
    private static void Sweep(
        List<List<Guid>> ranks,
        int rank,
        int against,
        Dictionary<Guid, List<Guid>> adjacent,
        Dictionary<Guid, int> rankOf)
    {
        List<Guid> row = ranks[rank];

        if (row.Count < 2)
        {
            return;
        }

        Dictionary<Guid, int> positions = [];

        for (int index = 0; index < ranks[against].Count; index++)
        {
            positions[ranks[against][index]] = index;
        }

        Dictionary<Guid, (double Barycentre, int Position)> keys = [];

        for (int index = 0; index < row.Count; index++)
        {
            Guid node = row[index];

            List<int> anchors =
            [
                .. adjacent[node]
                    .Where(other => rankOf[other] == against)
                    .Select(other => positions[other])
            ];

            // Its own position when nothing in the neighbouring rank anchors it, scaled into the
            // same space: a node that is not being pulled anywhere should not be moved.
            double barycentre = anchors.Count > 0
                ? anchors.Average()
                : (ranks[against].Count == 0 || row.Count < 2
                    ? index
                    : index * (ranks[against].Count - 1) / (double)(row.Count - 1));

            keys[node] = (barycentre, index);
        }

        row.Sort((left, right) =>
        {
            (double leftBarycentre, int leftPosition) = keys[left];
            (double rightBarycentre, int rightPosition) = keys[right];

            int byBarycentre = leftBarycentre.CompareTo(rightBarycentre);

            if (byBarycentre != 0)
            {
                return byBarycentre;
            }

            return leftPosition != rightPosition
                ? leftPosition.CompareTo(rightPosition)
                : left.CompareTo(right);
        });
    }

    /// <summary>How wide a rank of <paramref name="count"/> nodes is.</summary>
    private static double Width(int count, LayoutGeometry geometry) => count == 0
        ? 0
        : (count * geometry.NodeWidth) + ((count - 1) * geometry.NodeSeparation);

    /// <summary>How tall an island of <paramref name="count"/> ranks is.</summary>
    private static double Height(int count, LayoutGeometry geometry) => count == 0
        ? 0
        : (count * geometry.NodeHeight) + ((count - 1) * geometry.RankSeparation);
}
