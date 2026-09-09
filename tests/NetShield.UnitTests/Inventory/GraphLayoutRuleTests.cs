using FluentAssertions;

using NetShield.Inventory.Topology;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The WP-2.3 decomposition and layout as arithmetic: which islands an estate falls into, how far
/// each device is from its island's root, and where each one is drawn.
/// </summary>
/// <remarks>
/// <em>"An unreachable segment is returned as a disconnected component rather than dropped"</em>
/// is the criterion, and this is where it is proved as a function. The integration suite proves
/// the same thing end to end; this proves it is deterministic and that the coordinates are a
/// layout rather than a pile, neither of which a round trip through PostgreSQL can show.
/// </remarks>
public sealed class GraphLayoutRuleTests
{
    private static readonly LayoutGeometry Geometry = new(
        NodeWidth: 100,
        NodeHeight: 50,
        RankSeparation: 50,
        NodeSeparation: 20,
        ComponentSeparation: 200);

    /// <summary>Device ids that sort in a knowable order, so a tie-break can be asserted.</summary>
    private static Guid Device(int index) =>
        new($"00000000-0000-0000-0000-{index:D12}");

    private static GraphLayoutRule.Link Link(int a, int b) =>
        new(Guid.NewGuid(), Device(a), Device(b));

    private static GraphLayoutRule.Placement Node(GraphLayoutRule.Laid laid, int index) =>
        laid.Nodes.Single(node => node.DeviceId == Device(index));

    // --- islands ------------------------------------------------------------------------------

    [Fact]
    public void Lay_WithNoDevices_IsAnEmptyGraph()
    {
        GraphLayoutRule.Laid laid = GraphLayoutRule.Lay([], [], requestedRoot: null, Geometry);

        laid.Nodes.Should().BeEmpty();
        laid.Components.Should().BeEmpty();
    }

    [Fact]
    public void Lay_WithOneDeviceAndNoLinks_IsOneComponentOfOne()
    {
        // A switch nothing has walked yet, and a segment the moment its last edge is withdrawn.
        GraphLayoutRule.Laid laid = GraphLayoutRule.Lay(
            [Device(1)],
            [],
            requestedRoot: null,
            Geometry);

        laid.Components.Should().ContainSingle();
        laid.Components[0].NodeCount.Should().Be(1);
        laid.Components[0].EdgeCount.Should().Be(0);
        laid.Components[0].Depth.Should().Be(0);
        laid.Components[0].RootDeviceId.Should().Be(Device(1));
        Node(laid, 1).Rank.Should().Be(0);
    }

    [Fact]
    public void Lay_WithAnUnreachableSegment_KeepsItAsItsOwnComponent()
    {
        // The WP-2.3 criterion. Two switches cabled together and a third island of two that
        // nothing connects to them: four nodes, two components, nothing dropped.
        GraphLayoutRule.Laid laid = GraphLayoutRule.Lay(
            [Device(1), Device(2), Device(3), Device(4)],
            [Link(1, 2), Link(3, 4)],
            requestedRoot: null,
            Geometry);

        laid.Nodes.Should().HaveCount(4);
        laid.Components.Should().HaveCount(2);
        laid.Components.Sum(component => component.NodeCount).Should().Be(4);

        Node(laid, 1).ComponentIndex.Should().Be(Node(laid, 2).ComponentIndex);
        Node(laid, 3).ComponentIndex.Should().Be(Node(laid, 4).ComponentIndex);
        Node(laid, 1).ComponentIndex.Should().NotBe(Node(laid, 3).ComponentIndex);
    }

    [Fact]
    public void Lay_OrdersComponents_LargestFirst()
    {
        GraphLayoutRule.Laid laid = GraphLayoutRule.Lay(
            [Device(1), Device(2), Device(3), Device(4)],
            [Link(2, 3), Link(3, 4)],
            requestedRoot: null,
            Geometry);

        laid.Components[0].NodeCount.Should().Be(3);
        laid.Components[1].NodeCount.Should().Be(1);
        Node(laid, 1).ComponentIndex.Should().Be(1);
    }

    [Fact]
    public void Lay_WithTwoIslandsOfEqualSize_OrdersThemByRoot()
    {
        // Nothing about node count separates them, so the order has to come from something that
        // cannot move between two requests.
        GraphLayoutRule.Laid laid = GraphLayoutRule.Lay(
            [Device(1), Device(2), Device(3), Device(4)],
            [Link(3, 4), Link(1, 2)],
            requestedRoot: null,
            Geometry);

        laid.Components[0].RootDeviceId.Should().Be(Device(1));
        laid.Components[1].RootDeviceId.Should().Be(Device(3));
    }

    [Fact]
    public void Lay_CountsEachComponentsEdges()
    {
        GraphLayoutRule.Laid laid = GraphLayoutRule.Lay(
            [Device(1), Device(2), Device(3), Device(4), Device(5)],
            [Link(1, 2), Link(2, 3), Link(1, 3), Link(4, 5)],
            requestedRoot: null,
            Geometry);

        laid.Components[0].EdgeCount.Should().Be(3);
        laid.Components[1].EdgeCount.Should().Be(1);
    }

    // --- ranks --------------------------------------------------------------------------------

    [Fact]
    public void Lay_RanksEachDevice_ByHopsFromTheRoot()
    {
        // A line: 1 — 2 — 3 — 4, rooted at 1 because the caller asked for it.
        GraphLayoutRule.Laid laid = GraphLayoutRule.Lay(
            [Device(1), Device(2), Device(3), Device(4)],
            [Link(1, 2), Link(2, 3), Link(3, 4)],
            requestedRoot: Device(1),
            Geometry);

        Node(laid, 1).Rank.Should().Be(0);
        Node(laid, 2).Rank.Should().Be(1);
        Node(laid, 3).Rank.Should().Be(2);
        Node(laid, 4).Rank.Should().Be(3);
        laid.Components[0].Depth.Should().Be(3);
    }

    [Fact]
    public void Lay_WithNoRequestedRoot_RootsOnTheMostConnectedDevice()
    {
        // Which on a real estate is the core switch, and is what puts the core at the top.
        GraphLayoutRule.Laid laid = GraphLayoutRule.Lay(
            [Device(1), Device(2), Device(3), Device(4)],
            [Link(2, 1), Link(2, 3), Link(2, 4)],
            requestedRoot: null,
            Geometry);

        laid.Components[0].RootDeviceId.Should().Be(Device(2));
        Node(laid, 2).Rank.Should().Be(0);
        Node(laid, 1).Rank.Should().Be(1);
    }

    [Fact]
    public void Lay_WithEquallyConnectedDevices_RootsOnTheLowestId()
    {
        // "Whichever the query returned first" is not a rule, and would move the whole picture
        // between two identical requests.
        GraphLayoutRule.Laid laid = GraphLayoutRule.Lay(
            [Device(9), Device(4)],
            [Link(9, 4)],
            requestedRoot: null,
            Geometry);

        laid.Components[0].RootDeviceId.Should().Be(Device(4));
    }

    [Fact]
    public void Lay_WithARootOnOneIsland_StillRanksTheOthersFromTheirOwn()
    {
        // A rank has to mean something on an island the requested root is not on.
        GraphLayoutRule.Laid laid = GraphLayoutRule.Lay(
            [Device(1), Device(2), Device(3), Device(4)],
            [Link(1, 2), Link(3, 4)],
            requestedRoot: Device(1),
            Geometry);

        Node(laid, 3).Rank.Should().Be(0);
        Node(laid, 4).Rank.Should().Be(1);
    }

    [Fact]
    public void Lay_TakesTheShortestPath_WhenTwoReachTheSameDevice()
    {
        // A diamond. 4 is two hops from 1 by either side, and 1 is not three hops from itself.
        GraphLayoutRule.Laid laid = GraphLayoutRule.Lay(
            [Device(1), Device(2), Device(3), Device(4)],
            [Link(1, 2), Link(1, 3), Link(2, 4), Link(3, 4)],
            requestedRoot: Device(1),
            Geometry);

        Node(laid, 4).Rank.Should().Be(2);
        laid.Components[0].Depth.Should().Be(2);
    }

    [Fact]
    public void Lay_CountsDegree_IncludingASecondCableBetweenOnePair()
    {
        // WP-2.1 canonicalises on the port so that an aggregate is two edges rather than one.
        // Two cables are one adjacency for ranking and two links for degree.
        GraphLayoutRule.Laid laid = GraphLayoutRule.Lay(
            [Device(1), Device(2)],
            [Link(1, 2), Link(1, 2)],
            requestedRoot: null,
            Geometry);

        Node(laid, 1).Degree.Should().Be(2);
        Node(laid, 2).Degree.Should().Be(2);
        laid.Components[0].Depth.Should().Be(1);
    }

    [Fact]
    public void Lay_IgnoresALinkNamingADeviceThatIsNotANode()
    {
        // What a filter leaves behind: an edge to a device the site or VLAN excluded.
        GraphLayoutRule.Laid laid = GraphLayoutRule.Lay(
            [Device(1), Device(2)],
            [Link(1, 2), Link(2, 7)],
            requestedRoot: null,
            Geometry);

        laid.Nodes.Should().HaveCount(2);
        Node(laid, 2).Degree.Should().Be(1);
    }

    [Fact]
    public void Lay_IgnoresASelfLink()
    {
        GraphLayoutRule.Laid laid = GraphLayoutRule.Lay(
            [Device(1)],
            [new GraphLayoutRule.Link(Guid.NewGuid(), Device(1), Device(1))],
            requestedRoot: null,
            Geometry);

        Node(laid, 1).Degree.Should().Be(0);
        laid.Components[0].Depth.Should().Be(0);
    }

    // --- placement ----------------------------------------------------------------------------

    [Fact]
    public void Lay_PutsEachRankBelowTheLast()
    {
        GraphLayoutRule.Laid laid = GraphLayoutRule.Lay(
            [Device(1), Device(2), Device(3)],
            [Link(1, 2), Link(2, 3)],
            requestedRoot: Device(1),
            Geometry);

        Node(laid, 1).Y.Should().Be(0);
        Node(laid, 2).Y.Should().Be(Geometry.NodeHeight + Geometry.RankSeparation);
        Node(laid, 3).Y.Should().Be(2 * (Geometry.NodeHeight + Geometry.RankSeparation));
    }

    [Fact]
    public void Lay_SpacesTwoNodesOfOneRank_ByTheNodeSeparation()
    {
        GraphLayoutRule.Laid laid = GraphLayoutRule.Lay(
            [Device(1), Device(2), Device(3)],
            [Link(1, 2), Link(1, 3)],
            requestedRoot: Device(1),
            Geometry);

        double[] xs = [.. laid.Nodes.Where(node => node.Rank == 1).Select(node => node.X).Order()];

        (xs[1] - xs[0]).Should().Be(Geometry.NodeWidth + Geometry.NodeSeparation);
    }

    [Fact]
    public void Lay_CentresANarrowRank_UnderAWiderOne()
    {
        // A three-node rank under a one-node rank puts the root over the middle of them.
        GraphLayoutRule.Laid laid = GraphLayoutRule.Lay(
            [Device(1), Device(2), Device(3), Device(4)],
            [Link(1, 2), Link(1, 3), Link(1, 4)],
            requestedRoot: Device(1),
            Geometry);

        double[] children = [.. laid.Nodes.Where(node => node.Rank == 1).Select(node => node.X).Order()];

        Node(laid, 1).X.Should().Be(children[1]);
    }

    [Fact]
    public void Lay_PutsASecondComponent_BelowTheFirst()
    {
        GraphLayoutRule.Laid laid = GraphLayoutRule.Lay(
            [Device(1), Device(2), Device(3)],
            [Link(1, 2)],
            requestedRoot: null,
            Geometry);

        double firstBottom = laid.Nodes
            .Where(node => node.ComponentIndex == 0)
            .Max(node => node.Y);

        Node(laid, 3).Y.Should().BeGreaterThan(firstBottom);
    }

    [Fact]
    public void Lay_GivesTwoNodesOfOneRank_DifferentPositions()
    {
        GraphLayoutRule.Laid laid = GraphLayoutRule.Lay(
            [Device(1), Device(2), Device(3), Device(4), Device(5)],
            [Link(1, 2), Link(1, 3), Link(1, 4), Link(1, 5)],
            requestedRoot: Device(1),
            Geometry);

        laid.Nodes.Select(node => (node.X, node.Y)).Should().OnlyHaveUniqueItems();
        laid.Nodes.Where(node => node.Rank == 1).Select(node => node.OrderInRank)
            .Should().BeEquivalentTo([0, 1, 2, 3]);
    }

    [Fact]
    public void Lay_OrdersARank_SoThatEdgesToTheRankAboveDoNotCross()
    {
        // Two parents, two children each, declared interleaved. The barycentre passes are what
        // put each child under its own parent instead of leaving them in id order.
        GraphLayoutRule.Laid laid = GraphLayoutRule.Lay(
            [Device(1), Device(2), Device(3), Device(4), Device(5), Device(6), Device(7)],
            [
                Link(1, 2),
                Link(1, 3),
                Link(2, 4),
                Link(2, 6),
                Link(3, 5),
                Link(3, 7)
            ],
            requestedRoot: Device(1),
            Geometry);

        int leftParent = Node(laid, 2).OrderInRank;
        int rightParent = Node(laid, 3).OrderInRank;

        bool twoIsLeft = leftParent < rightParent;

        int[] twosChildren = [Node(laid, 4).OrderInRank, Node(laid, 6).OrderInRank];
        int[] threesChildren = [Node(laid, 5).OrderInRank, Node(laid, 7).OrderInRank];

        // Whichever parent is on the left, its children are the left pair of the rank below.
        (twoIsLeft ? twosChildren : threesChildren).Max()
            .Should().BeLessThan((twoIsLeft ? threesChildren : twosChildren).Min());
    }

    // --- the node ordering a cursor resumes from ----------------------------------------------

    [Fact]
    public void Lay_ReturnsNodes_OrderedByComponentThenRankThenId()
    {
        GraphLayoutRule.Laid laid = GraphLayoutRule.Lay(
            [Device(1), Device(2), Device(3), Device(4), Device(5)],
            [Link(1, 3), Link(1, 2), Link(4, 5)],
            requestedRoot: Device(1),
            Geometry);

        laid.Nodes.Select(node => (node.ComponentIndex, node.Rank, node.DeviceId))
            .Should().BeInAscendingOrder();

        laid.Nodes[0].DeviceId.Should().Be(Device(1));
        laid.Nodes[1].DeviceId.Should().Be(Device(2));
        laid.Nodes[2].DeviceId.Should().Be(Device(3));
    }

    [Fact]
    public void Lay_IsDeterministic_WhateverOrderTheRowsArriveIn()
    {
        // The reason the whole graph can be recomputed on every page: two requests over the same
        // estate have to produce the same coordinates, whatever order PostgreSQL returned.
        Guid[] devices = [.. Enumerable.Range(1, 12).Select(Device)];

        List<GraphLayoutRule.Link> links =
        [
            Link(1, 2), Link(1, 3), Link(2, 4), Link(2, 5), Link(3, 6), Link(3, 7),
            Link(4, 8), Link(5, 9), Link(6, 10), Link(7, 11), Link(7, 12)
        ];

        GraphLayoutRule.Laid first = GraphLayoutRule.Lay(devices, links, null, Geometry);

        GraphLayoutRule.Laid second = GraphLayoutRule.Lay(
            [.. devices.Reverse()],
            [.. Enumerable.Reverse(links)],
            null,
            Geometry);

        second.Nodes.Should().BeEquivalentTo(first.Nodes, options => options.WithStrictOrdering());
        second.Components.Should().BeEquivalentTo(first.Components, options => options.WithStrictOrdering());
    }
}
