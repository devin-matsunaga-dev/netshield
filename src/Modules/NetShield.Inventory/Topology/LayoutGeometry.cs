using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Topology;

/// <summary>
/// The node and gap sizes <see cref="GraphLayoutRule"/> places with, taken from
/// <c>DESIGN.md</c> §6.
/// </summary>
/// <remarks>
/// <para>
/// §6 fixes a topology node as a 48px icon tile with a 12px label and a 10px <c>text-muted</c>
/// sub-label beneath it, which is the 80 units of height here; the width is the cell a hostname
/// fits in rather than the tile, because two tiles 48 apart put their labels on top of each
/// other. The separations are the same document's spacing scale.
/// </para>
/// <para>
/// A record of constants rather than an options section, deliberately. A layout that differed
/// between two deployments would be one nobody could reproduce from a screenshot, and the values
/// belong to <c>DESIGN.md</c> — which <c>WORK_PACKAGES.md</c> forbids this package from changing.
/// It is a type at all so that the layout rule takes its geometry as an argument and can be
/// tested at any scale.
/// </para>
/// </remarks>
/// <param name="NodeWidth">The width one node occupies, label included.</param>
/// <param name="NodeHeight">The height one node occupies, label and sub-label included.</param>
/// <param name="RankSeparation">The gap between one rank and the next.</param>
/// <param name="NodeSeparation">The gap between two nodes within one rank.</param>
/// <param name="ComponentSeparation">The gap between one island and the next.</param>
internal sealed record LayoutGeometry(
    double NodeWidth,
    double NodeHeight,
    double RankSeparation,
    double NodeSeparation,
    double ComponentSeparation)
{
    /// <summary>The geometry every graph this endpoint serves is laid out with.</summary>
    internal static LayoutGeometry Default { get; } = new(
        NodeWidth: 96,
        NodeHeight: 80,
        RankSeparation: 96,
        NodeSeparation: 48,
        ComponentSeparation: 128);

    /// <summary>The same values, as the caller reads them.</summary>
    internal TopologyGraphLayout ToContract() => new(
        NodeWidth,
        NodeHeight,
        RankSeparation,
        NodeSeparation,
        ComponentSeparation);
}
