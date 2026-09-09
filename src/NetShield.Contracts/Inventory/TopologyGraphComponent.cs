namespace NetShield.Contracts.Inventory;

/// <summary>
/// One connected component of the topology graph — an island of devices with no edge leaving it.
/// </summary>
/// <remarks>
/// <para>
/// <em>"An unreachable segment is returned as a disconnected component rather than dropped"</em>
/// is the WP-2.3 criterion this shape exists for. A device with no edges at all is a component of
/// one, which is what a switch nothing has walked yet looks like, and what a segment that has
/// gone quiet looks like the moment its last edge is withdrawn.
/// </para>
/// <para>
/// Components are ordered by node count descending, then by their root's device id, so the
/// estate's main body is component 0 on every request and the ordering does not move under
/// paging. That order is also the major key of the node ordering a cursor resumes from.
/// </para>
/// </remarks>
/// <param name="Index">Its position in this graph's component ordering.</param>
/// <param name="RootDeviceId">The node ranks are measured from — the requested root where it is in this component, otherwise its most-connected device.</param>
/// <param name="NodeCount">How many nodes it holds, across every page.</param>
/// <param name="EdgeCount">How many edges it holds, across every page.</param>
/// <param name="Depth">The greatest rank in it. Zero for a component of one.</param>
public sealed record TopologyGraphComponent(
    int Index,
    Guid RootDeviceId,
    int NodeCount,
    int EdgeCount,
    int Depth);
