namespace NetShield.Contracts.Inventory;

/// <summary>
/// The geometry the node coordinates in a <see cref="TopologyGraph"/> were computed with.
/// </summary>
/// <remarks>
/// <para>
/// Returned rather than assumed, so the canvas and the server cannot come to different pictures
/// of the same graph. Every value is in the same abstract units as
/// <see cref="TopologyGraphNode.X"/> and <see cref="TopologyGraphNode.Y"/>, and they are the
/// dimensions <c>DESIGN.md</c> §6 fixes for a topology node: a 48px icon tile with a 12px label
/// and a 10px sub-label beneath it.
/// </para>
/// <para>
/// A client that would rather run <c>dagre</c> itself can ignore the coordinates and read
/// <see cref="TopologyGraphNode.Rank"/> and the edge list instead. Neither reading needs the
/// contract to change, which is the reason both are here.
/// </para>
/// </remarks>
/// <param name="NodeWidth">The width one node occupies, label included.</param>
/// <param name="NodeHeight">The height one node occupies, label and sub-label included.</param>
/// <param name="RankSeparation">The gap between one rank of nodes and the next.</param>
/// <param name="NodeSeparation">The gap between two nodes within one rank.</param>
/// <param name="ComponentSeparation">The gap between one connected component and the next.</param>
public sealed record TopologyGraphLayout(
    double NodeWidth,
    double NodeHeight,
    double RankSeparation,
    double NodeSeparation,
    double ComponentSeparation);
