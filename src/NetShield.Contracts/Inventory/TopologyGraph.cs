namespace NetShield.Contracts.Inventory;

/// <summary>
/// One page of the topology graph: the nodes to draw, the edges between them, the components they
/// fall into, and the geometry the coordinates were computed with.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Not a <c>CursorPage</c>, and still cursor-paginated.</strong> A graph is not a list —
/// it has three collections that have to be read together, and a page of nodes is meaningless
/// without the edges incident to it. So the shape is its own, and it keeps
/// <see cref="NextCursor"/> with exactly the semantics <c>CONVENTIONS.md</c> §4 fixes: opaque,
/// passed back verbatim, <see langword="null"/> on the last page, and bounded by the same
/// <c>limit</c> — counted here in nodes.
/// </para>
/// <para>
/// <strong>The paging is by subgraph.</strong> Nodes are ordered by component, then by rank
/// within the component, then by device id, so a page is a contiguous slice of one or more
/// components rather than an arbitrary set of devices: reading page one gives the estate's main
/// body from its root outwards, and the islands arrive last. The ordering is computed from the
/// whole filtered graph on every request and is therefore stable across pages.
/// </para>
/// <para>
/// <strong>Nothing here traverses further than drawing needs.</strong> The walk over
/// <c>device_adjacencies</c> selects which devices to draw and how far apart to put them. It
/// concludes nothing about reachability, blast radius, or what a failure would take with it —
/// all of which are on the <c>SPEC.md</c> §3 Defer list and none of which this endpoint is a
/// small version of.
/// </para>
/// </remarks>
/// <param name="Nodes">The devices on this page, in the graph's node ordering.</param>
/// <param name="Edges">Every edge incident to a node on this page, each returned on exactly one page.</param>
/// <param name="Components">Every component of the whole filtered graph, not only those on this page.</param>
/// <param name="Layout">The geometry the coordinates were computed with.</param>
/// <param name="NextCursor">The cursor that fetches the following page, or <see langword="null"/> on the last.</param>
/// <param name="TotalNodeCount">How many nodes the filter matched, across every page.</param>
/// <param name="TotalEdgeCount">How many edges it matched, across every page.</param>
/// <param name="Truncated">Whether the filter matched more nodes than one graph may hold, and the graph was cut.</param>
public sealed record TopologyGraph(
    IReadOnlyList<TopologyGraphNode> Nodes,
    IReadOnlyList<TopologyGraphEdge> Edges,
    IReadOnlyList<TopologyGraphComponent> Components,
    TopologyGraphLayout Layout,
    string? NextCursor,
    long TotalNodeCount,
    long TotalEdgeCount,
    bool Truncated);
