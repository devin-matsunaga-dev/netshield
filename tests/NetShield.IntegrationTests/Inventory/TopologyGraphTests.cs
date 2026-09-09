using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

using FluentAssertions;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.IntegrationTests.Collector;
using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// The graph endpoint over the whole round trip: walk an estate as the collector would, then read
/// back the nodes, the edges, the islands and the coordinates a canvas draws them at.
/// </summary>
/// <remarks>
/// All three WP-2.3 "Done when" criteria are here — the 500-node timing, each filter reducing the
/// result, and an unreachable segment surviving as a component of its own — beside the bounds
/// that keep the traversal a rendering traversal. Driven through the real contract against real
/// PostgreSQL, because what has to hold is that the read agrees with what a separate process in
/// another language actually reported.
/// </remarks>
public sealed class TopologyGraphTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Devices = "/api/v1/devices";
    private const string Graph = "/api/v1/topology/graph";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // --- what a walked estate draws as ---------------------------------------------------------

    [Fact]
    public async Task TheGraph_IsTheEdgeSetTheWalksConcluded()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Estate estate = await WalkedEstateAsync(host);

        TopologyGraph graph = Read<TopologyGraph>(await host.Client.GetAsync(Graph, Cancellation));

        graph.Nodes.Select(node => node.DeviceId)
            .Should().BeEquivalentTo([estate.Core, estate.AccessOne, estate.AccessTwo]);

        // Two edges and not four: both devices' walks converge on one canonically ordered row.
        graph.Edges.Should().HaveCount(2);
        graph.TotalNodeCount.Should().Be(3);
        graph.TotalEdgeCount.Should().Be(2);
        graph.Components.Should().ContainSingle();
        graph.Truncated.Should().BeFalse();
        graph.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task ANode_CarriesTheStateTheReachabilityWorkPublishes()
    {
        // DESIGN.md §6 encodes the state as the node tile's border, so it is the one field the
        // canvas cannot draw without.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Estate estate = await WalkedEstateAsync(host);

        TopologyGraphNode core = Read<TopologyGraph>(await host.Client.GetAsync(Graph, Cancellation))
            .Nodes.Single(node => node.DeviceId == estate.Core);

        core.Hostname.Should().Be("core-sw");
        core.State.Should().Be(DeviceState.Unknown);
        core.Role.Should().Be(DeviceRole.Switch);
        core.Degree.Should().Be(2);
    }

    [Fact]
    public async Task TheRootIsTheMostConnectedDevice_AndTheRanksAreMeasuredFromIt()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Estate estate = await WalkedEstateAsync(host);

        TopologyGraph graph = Read<TopologyGraph>(await host.Client.GetAsync(Graph, Cancellation));

        graph.Components[0].RootDeviceId.Should().Be(estate.Core);
        graph.Components[0].Depth.Should().Be(1);

        Node(graph, estate.Core).Rank.Should().Be(0);
        Node(graph, estate.AccessOne).Rank.Should().Be(1);
        Node(graph, estate.AccessTwo).Rank.Should().Be(1);

        // Rank 0 is at the top, and the two access switches are side by side beneath it.
        Node(graph, estate.Core).Y.Should().BeLessThan(Node(graph, estate.AccessOne).Y);
        Node(graph, estate.AccessOne).X.Should().NotBe(Node(graph, estate.AccessTwo).X);
    }

    [Fact]
    public async Task TheLayout_SaysWhatGeometryTheCoordinatesAreIn()
    {
        // Returned rather than assumed, so the canvas and the server cannot come to different
        // pictures of one graph.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await WalkedEstateAsync(host);

        TopologyGraphLayout layout =
            Read<TopologyGraph>(await host.Client.GetAsync(Graph, Cancellation)).Layout;

        layout.NodeWidth.Should().BeGreaterThan(0);
        layout.NodeHeight.Should().BeGreaterThan(0);
        layout.RankSeparation.Should().BeGreaterThan(0);
        layout.NodeSeparation.Should().BeGreaterThan(0);
        layout.ComponentSeparation.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AWithdrawnEdge_IsNotDrawn()
    {
        // A removed link ages out of the graph the moment the walk that no longer sees it lands.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Estate estate = await WalkedEstateAsync(host);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            estate.Core,
            TopologyFixtures.NeighborResult(
                localChassisId: CoreChassis,
                lldp: [TopologyFixtures.Lldp(1, AccessOneChassis, "Gi0/0", systemName: "acc-sw-1")]),
            Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            estate.AccessTwo,
            TopologyFixtures.NeighborResult(
                localChassisId: AccessTwoChassis,
                lldp: [],
                lldpSupported: true),
            Cancellation);

        TopologyGraph graph = Read<TopologyGraph>(await host.Client.GetAsync(Graph, Cancellation));

        graph.Edges.Should().ContainSingle();

        // The second access switch is still a device, and is now an island of one rather than
        // gone: nothing withdrew the switch, only the cable.
        graph.Nodes.Should().HaveCount(3);
        graph.Components.Should().HaveCount(2);
    }

    [Fact]
    public async Task AnEdgeToSomethingThatIsNotADevice_IsCountedAndNotDrawn()
    {
        // An unmanaged switch or a server that speaks LLDP has no state, no site and no VLAN, so
        // nothing this endpoint filters on could match it and a tile for it would be a shape with
        // no facts behind it. The edge is still on the device's own adjacency route.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host,
            Cancellation,
            "lone-sw",
            "10.30.0.1");

        await TopologyFixtures.NeighborWalkAsync(
            host,
            deviceId,
            TopologyFixtures.NeighborResult(
                localChassisId: CoreChassis,
                lldp: [TopologyFixtures.Lldp(1, "00:1C:73:FF:FF:FF", "eth0", systemName: "a-server")]),
            Cancellation);

        TopologyGraph graph = Read<TopologyGraph>(await host.Client.GetAsync(Graph, Cancellation));

        graph.Nodes.Should().ContainSingle();
        graph.Edges.Should().BeEmpty();
        Node(graph, deviceId).ExternalEdgeCount.Should().Be(1);
        Node(graph, deviceId).Degree.Should().Be(0);
    }

    // --- "an unreachable segment is returned as a disconnected component rather than dropped" ---

    [Fact]
    public async Task AnUnreachableSegment_IsItsOwnComponent()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Estate estate = await WalkedEstateAsync(host);

        // A pair of switches cabled to each other and to nothing the estate can reach.
        Guid islandOne = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host,
            Cancellation,
            "far-sw-1",
            "10.40.0.1");

        Guid islandTwo = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host,
            Cancellation,
            "far-sw-2",
            "10.40.0.2");

        await CableAsync(host, islandOne, "00:1C:73:00:0F:01", islandTwo, "00:1C:73:00:0F:02", "far-sw-2");

        TopologyGraph graph = Read<TopologyGraph>(await host.Client.GetAsync(Graph, Cancellation));

        graph.Nodes.Should().HaveCount(5);
        graph.Components.Should().HaveCount(2);

        // Largest first, so the estate's main body is component 0 on every request.
        graph.Components[0].NodeCount.Should().Be(3);
        graph.Components[1].NodeCount.Should().Be(2);

        int island = Node(graph, islandOne).ComponentIndex;

        island.Should().Be(Node(graph, islandTwo).ComponentIndex);
        island.Should().NotBe(Node(graph, estate.Core).ComponentIndex);
    }

    [Fact]
    public async Task ADeviceNothingHasWalked_IsAComponentOfOne()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await WalkedEstateAsync(host);

        Guid lonely = await CollectorFixtures.CreateDeviceAsync(
            host,
            "new-sw",
            "10.50.0.1",
            Cancellation);

        TopologyGraph graph = Read<TopologyGraph>(await host.Client.GetAsync(Graph, Cancellation));

        graph.Components.Should().HaveCount(2);
        graph.Components[1].NodeCount.Should().Be(1);
        graph.Components[1].RootDeviceId.Should().Be(lonely);
        Node(graph, lonely).Rank.Should().Be(0);
        Node(graph, lonely).Degree.Should().Be(0);
    }

    // --- "filters reduce the result correctly" -------------------------------------------------

    [Fact]
    public async Task TheSiteFilter_KeepsOnlyThatSitesDevices()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid hq = await SitedDeviceAsync(host, "hq-sw", "10.60.0.1", "HQ");
        Guid branch = await SitedDeviceAsync(host, "br-sw", "10.60.0.2", "Branch");

        TopologyGraph graph = Read<TopologyGraph>(
            await host.Client.GetAsync($"{Graph}?site=HQ", Cancellation));

        graph.Nodes.Select(node => node.DeviceId).Should().Equal(hq);
        graph.Nodes.Should().NotContain(node => node.DeviceId == branch);
        graph.TotalNodeCount.Should().Be(1);
    }

    [Fact]
    public async Task TheSiteFilter_IgnoresCase_AsTheDeviceListDoes()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await SitedDeviceAsync(host, "hq-sw", "10.60.0.1", "HQ");

        Read<TopologyGraph>(await host.Client.GetAsync($"{Graph}?site=hq", Cancellation))
            .Nodes.Should().ContainSingle();
    }

    [Fact]
    public async Task TheVlanFilter_KeepsTheDevicesCarryingItAndTheEdgesBetweenThem()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Estate estate = await WalkedEstateAsync(host);

        // The core and one access switch carry VLAN 20; the other carries only VLAN 30.
        await TopologyFixtures.VlanWalkAsync(
            host,
            estate.Core,
            TopologyFixtures.VlanResult([TopologyFixtures.Vlan(20, "Users VLAN", [1, 2], [1])]),
            Cancellation);

        await TopologyFixtures.VlanWalkAsync(
            host,
            estate.AccessOne,
            TopologyFixtures.VlanResult([TopologyFixtures.Vlan(20, "Users VLAN", [1], [1])]),
            Cancellation);

        await TopologyFixtures.VlanWalkAsync(
            host,
            estate.AccessTwo,
            TopologyFixtures.VlanResult([TopologyFixtures.Vlan(30, "Voice VLAN", [1], [1])]),
            Cancellation);

        TopologyGraph graph = Read<TopologyGraph>(
            await host.Client.GetAsync($"{Graph}?vlanId=20", Cancellation));

        graph.Nodes.Select(node => node.DeviceId)
            .Should().BeEquivalentTo([estate.Core, estate.AccessOne]);

        // A VLAN's devices are the nodes and the edges between them are the subgraph: the core's
        // other cable leads out of the VLAN and is not drawn.
        graph.Edges.Should().ContainSingle();
    }

    [Fact]
    public async Task AVlanNothingCarries_IsAnEmptyGraphRatherThanAnError()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await WalkedEstateAsync(host);

        TopologyGraph graph = Read<TopologyGraph>(
            await host.Client.GetAsync($"{Graph}?vlanId=999", Cancellation));

        graph.Nodes.Should().BeEmpty();
        graph.Edges.Should().BeEmpty();
        graph.Components.Should().BeEmpty();
        graph.TotalNodeCount.Should().Be(0);
    }

    [Fact]
    public async Task ARootAndADepth_KeepOnlyTheNeighbourhood()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        IReadOnlyList<Guid> estate = await host.SeedEstateAsync(31, fanout: 2, Cancellation);

        TopologyGraph whole = Read<TopologyGraph>(
            await host.Client.GetAsync($"{Graph}?limit=200", Cancellation));

        whole.TotalNodeCount.Should().Be(31);

        TopologyGraph oneHop = Read<TopologyGraph>(
            await host.Client.GetAsync(
                $"{Graph}?rootDeviceId={estate[0]}&depth=1&limit=200",
                Cancellation));

        // The root and its two children, and nothing below them.
        oneHop.TotalNodeCount.Should().Be(3);
        oneHop.Components[0].RootDeviceId.Should().Be(estate[0]);
        oneHop.Nodes.Should().OnlyContain(node => node.Rank <= 1);

        TopologyGraph twoHops = Read<TopologyGraph>(
            await host.Client.GetAsync(
                $"{Graph}?rootDeviceId={estate[0]}&depth=2&limit=200",
                Cancellation));

        twoHops.TotalNodeCount.Should().Be(7);
    }

    [Fact]
    public async Task ADepthOfZero_IsTheRootAlone()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        IReadOnlyList<Guid> estate = await host.SeedEstateAsync(7, fanout: 2, Cancellation);

        TopologyGraph graph = Read<TopologyGraph>(
            await host.Client.GetAsync(
                $"{Graph}?rootDeviceId={estate[0]}&depth=0",
                Cancellation));

        graph.Nodes.Select(node => node.DeviceId).Should().Equal(estate[0]);
        graph.Edges.Should().BeEmpty();
    }

    [Fact]
    public async Task ADepthIsMeasuredThroughTheFilter_NotAroundIt()
    {
        // Otherwise "one hop through VLAN 20" would mean "one hop through anything, then
        // discard", and the two answers differ exactly where an operator would be surprised.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Estate estate = await WalkedEstateAsync(host);

        // A fourth switch cabled to the second access switch, at the same site as the core.
        Guid edgeSwitch = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host,
            Cancellation,
            "edge-sw",
            "10.70.0.9");

        await CableAsync(host, estate.AccessTwo, AccessTwoChassis, edgeSwitch, "00:1C:73:00:0E:09", "edge-sw");

        // Everything but the second access switch is at HQ, so the path from the core to the
        // edge switch leaves the site halfway along it.
        foreach (Guid deviceId in new[] { estate.Core, estate.AccessOne, edgeSwitch })
        {
            await SetSiteAsync(host, deviceId, "HQ");
        }

        TopologyGraph graph = Read<TopologyGraph>(
            await host.Client.GetAsync(
                $"{Graph}?site=HQ&rootDeviceId={estate.Core}&depth=3",
                Cancellation));

        graph.Nodes.Select(node => node.DeviceId)
            .Should().BeEquivalentTo([estate.Core, estate.AccessOne]);

        graph.Nodes.Should().NotContain(node => node.DeviceId == edgeSwitch);
    }

    [Fact]
    public async Task ARootTheFilterExcludes_IsAnEmptyGraph()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid hq = await SitedDeviceAsync(host, "hq-sw", "10.60.0.1", "HQ");

        TopologyGraph graph = Read<TopologyGraph>(
            await host.Client.GetAsync($"{Graph}?site=Branch&rootDeviceId={hq}", Cancellation));

        graph.Nodes.Should().BeEmpty();
        graph.Components.Should().BeEmpty();
    }

    // --- paging by subgraph ---------------------------------------------------------------------

    [Fact]
    public async Task EveryNodeAndEveryEdge_ArriveExactlyOnceAcrossThePages()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        IReadOnlyList<Guid> estate = await host.SeedEstateAsync(120, fanout: 3, Cancellation);

        (List<TopologyGraphNode> nodes, List<TopologyGraphEdge> edges, int pages) =
            await ReadEveryPageAsync(host, $"{Graph}?limit=25");

        pages.Should().BeGreaterThan(1);
        nodes.Select(node => node.DeviceId).Should().OnlyHaveUniqueItems();
        nodes.Select(node => node.DeviceId).Should().BeEquivalentTo(estate);
        edges.Select(edge => edge.Id).Should().OnlyHaveUniqueItems();
        edges.Should().HaveCount(119);
    }

    [Fact]
    public async Task APageOfNodes_ArrivesComponentThenRankThenId()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await host.SeedEstateAsync(40, fanout: 2, Cancellation, islands: 4);

        (List<TopologyGraphNode> nodes, _, _) = await ReadEveryPageAsync(host, $"{Graph}?limit=7");

        nodes.Select(node => (node.ComponentIndex, node.Rank, node.DeviceId))
            .Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task AnEdgeStraddlingAPageBoundary_SaysWhichEndTheReaderHasYet()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await host.SeedEstateAsync(40, fanout: 2, Cancellation);

        TopologyGraph first = Read<TopologyGraph>(
            await host.Client.GetAsync($"{Graph}?limit=5", Cancellation));

        HashSet<Guid> onPage = [.. first.Nodes.Select(node => node.DeviceId)];

        first.Edges.Should().NotBeEmpty();

        foreach (TopologyGraphEdge edge in first.Edges)
        {
            edge.ADeviceIncluded.Should().Be(onPage.Contains(edge.ADeviceId));
            edge.BDeviceIncluded.Should().Be(onPage.Contains(edge.BDeviceId));
        }

        // The whole point of the flags: at least one edge on the first page leads somewhere the
        // reader has not been given yet.
        first.Edges.Should().Contain(edge => !edge.ADeviceIncluded || !edge.BDeviceIncluded);
    }

    [Fact]
    public async Task TheComponentList_DescribesTheWholeGraphAndNotThePage()
    {
        // A reader on page one has to be able to see that there are four islands, or they cannot
        // tell a small estate from the first twenty devices of a large one.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await host.SeedEstateAsync(40, fanout: 2, Cancellation, islands: 4);

        TopologyGraph first = Read<TopologyGraph>(
            await host.Client.GetAsync($"{Graph}?limit=5", Cancellation));

        first.Nodes.Should().HaveCount(5);
        first.Components.Should().HaveCount(4);
        first.Components.Sum(component => component.NodeCount).Should().Be(40);
        first.TotalNodeCount.Should().Be(40);
    }

    [Fact]
    public async Task ACursorThisEndpointDidNotIssue_IsRefused()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse refused = await host.Client.GetAsync($"{Graph}?cursor=not-a-cursor", Cancellation);

        refused.Status.Should().Be(400);
        refused.Member("code").Should().Be("paging.invalid-cursor");
    }

    [Fact]
    public async Task ALimitPastTheMaximum_IsRefusedRatherThanClamped()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse refused = await host.Client.GetAsync($"{Graph}?limit=5000", Cancellation);

        refused.Status.Should().Be(400);
        refused.Member("code").Should().Be("paging.invalid-limit");
    }

    // --- the bounds on the traversal --------------------------------------------------------------

    [Fact]
    public async Task ADepthWithNoRoot_IsRefused()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse refused = await host.Client.GetAsync($"{Graph}?depth=2", Cancellation);

        refused.Status.Should().Be(400);
        refused.Member("code").Should().Be("topology.graph-depth-invalid");
    }

    [Fact]
    public async Task ADepthPastTheCeiling_IsRefused()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse refused = await host.Client.GetAsync(
            $"{Graph}?rootDeviceId={Guid.NewGuid()}&depth=99",
            Cancellation);

        refused.Status.Should().Be(400);
        refused.Member("code").Should().Be("topology.graph-depth-invalid");
    }

    [Fact]
    public async Task AVlanIdOutsideWhat8021QAdmits_IsRefused()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse refused = await host.Client.GetAsync($"{Graph}?vlanId=9000", Cancellation);

        refused.Status.Should().Be(400);
        refused.Member("code").Should().Be("topology.vlan-id-out-of-range");
    }

    [Fact]
    public async Task ARootThatIsNotADevice_IsRefused()
    {
        // A 404 rather than an empty graph: a caller who asked to centre on a device and got
        // nothing cannot tell "no links" from "no such device".
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse refused = await host.Client.GetAsync(
            $"{Graph}?rootDeviceId={Guid.NewGuid()}",
            Cancellation);

        refused.Status.Should().Be(404);
        refused.Member("code").Should().Be("topology.graph-root-not-found");
    }

    [Fact]
    public async Task ARemovedDevice_IsNeitherANodeNorARoot()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Estate estate = await WalkedEstateAsync(host);

        (await host.Client.DeleteAsync($"{Devices}/{estate.AccessTwo}", Cancellation))
            .Status.Should().Be(204);

        TopologyGraph graph = Read<TopologyGraph>(await host.Client.GetAsync(Graph, Cancellation));

        graph.Nodes.Should().NotContain(node => node.DeviceId == estate.AccessTwo);
        graph.Nodes.Should().HaveCount(2);

        // Its cable now leads to something the estate no longer monitors, which is a count on
        // the core rather than a node.
        Node(graph, estate.Core).ExternalEdgeCount.Should().Be(1);

        (await host.Client.GetAsync($"{Graph}?rootDeviceId={estate.AccessTwo}", Cancellation))
            .Status.Should().Be(404);
    }

    // --- "a 500-node estate returns in under 500 ms" ----------------------------------------------

    /// <summary>
    /// The whole estate at <c>SPEC.md</c> §1's scale, fetched page by page, end to end.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>CONVENTIONS.md</c> §4 caps a page at 200, so 500 nodes is three requests rather than
    /// one — and the criterion is measured across all three, which is stricter than a single
    /// response would have been. Every one of them recomputes the whole graph, because a
    /// component index and a rank are properties of the graph rather than of a row; that repeated
    /// work is what this is really measuring.
    /// </para>
    /// <para>
    /// Measured on the machine this was written on: 77 ms for the three pages quiet, and inside
    /// the limit with the rest of the integration suite running beside it. The timing is written
    /// to the test output either way, so a failure says how far over it went rather than only
    /// that it did.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheWholeEstateAtTargetScale_IsReturnedInUnderHalfASecond()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await host.SeedEstateAsync(500, fanout: 4, Cancellation);

        // Once to warm the connection pool and the query plans, which is the state a running
        // system is in and the state the non-functional requirement is about.
        await ReadEveryPageAsync(host, $"{Graph}?limit=200");

        long started = Stopwatch.GetTimestamp();

        (List<TopologyGraphNode> nodes, List<TopologyGraphEdge> edges, int pages) =
            await ReadEveryPageAsync(host, $"{Graph}?limit=200");

        TimeSpan elapsed = Stopwatch.GetElapsedTime(started);

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"500 nodes over {pages} pages in {elapsed.TotalMilliseconds:F0} ms");

        nodes.Should().HaveCount(500);
        edges.Should().HaveCount(499);
        pages.Should().Be(3);

        elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
    }

    // --- Authorization ------------------------------------------------------------------------

    /// <summary>
    /// A topology read, on the permission whose own definition names the topology graph — checked
    /// at the endpoint and again in the module (ARCHITECTURE.md §8).
    /// </summary>
    [Theory]
    [InlineData(UserRole.Administrator)]
    [InlineData(UserRole.Operator)]
    [InlineData(UserRole.Analyst)]
    [InlineData(UserRole.ReadOnly)]
    public async Task TheGraph_IsReadableByAnyRoleHoldingTopologyRead(UserRole role)
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await WalkedEstateAsync(host);
        await host.SignInAsync(role, Cancellation);

        ApiResponse read = await host.Client.GetAsync(Graph, Cancellation);

        read.Status.Should().Be(200);
        Read<TopologyGraph>(read).Nodes.Should().HaveCount(3);
    }

    [Fact]
    public async Task TheGraph_HasNoWriteRoute()
    {
        // Every node and every edge is reconstructible by reading the estate again, and a
        // hand-drawn link would be a claim about cabling with no evidence behind it.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        (await host.Client.PostAsync(Graph, new { }, Cancellation))
            .Status.Should().Be(405);
    }

    // --- the estate these tests are written against ---------------------------------------------

    private const string CoreChassis = "00:1C:73:00:00:01";
    private const string AccessOneChassis = "00:1C:73:00:00:02";
    private const string AccessTwoChassis = "00:1C:73:00:00:03";

    private sealed record Estate(Guid Core, Guid AccessOne, Guid AccessTwo);

    /// <summary>
    /// Three switches, walked as the collector would walk them: a core with a cable to each of
    /// two access switches, and both ends of both cables reported.
    /// </summary>
    private static async Task<Estate> WalkedEstateAsync(InventoryHost host)
    {
        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host,
            Cancellation,
            "core-sw",
            "10.10.0.1");

        Guid accessOne = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host,
            Cancellation,
            "acc-sw-1",
            "10.10.0.2");

        Guid accessTwo = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host,
            Cancellation,
            "acc-sw-2",
            "10.10.0.3");

        await host.SeedInterfacesAsync(
            core,
            [(1, "Gi0/1", "GigabitEthernet0/1", null), (2, "Gi0/2", "GigabitEthernet0/2", null)],
            Cancellation);

        await host.SeedInterfacesAsync(accessOne, [(1, "Gi0/0", "GigabitEthernet0/0", null)], Cancellation);
        await host.SeedInterfacesAsync(accessTwo, [(1, "Gi0/0", "GigabitEthernet0/0", null)], Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            core,
            TopologyFixtures.NeighborResult(
                localChassisId: CoreChassis,
                lldp:
                [
                    TopologyFixtures.Lldp(1, AccessOneChassis, "Gi0/0", systemName: "acc-sw-1"),
                    TopologyFixtures.Lldp(2, AccessTwoChassis, "Gi0/0", systemName: "acc-sw-2")
                ]),
            Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            accessOne,
            TopologyFixtures.NeighborResult(
                localChassisId: AccessOneChassis,
                lldp: [TopologyFixtures.Lldp(1, CoreChassis, "Gi0/1", systemName: "core-sw")]),
            Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            accessTwo,
            TopologyFixtures.NeighborResult(
                localChassisId: AccessTwoChassis,
                lldp: [TopologyFixtures.Lldp(1, CoreChassis, "Gi0/2", systemName: "core-sw")]),
            Cancellation);

        return new Estate(core, accessOne, accessTwo);
    }

    /// <summary>Reports one device seeing another over LLDP, which is one edge.</summary>
    private static async Task CableAsync(
        InventoryHost host,
        Guid deviceId,
        string localChassis,
        Guid farDeviceId,
        string farChassis,
        string farName)
    {
        await host.SeedInterfacesAsync(farDeviceId, [(1, "Gi0/0", "GigabitEthernet0/0", null)], Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            deviceId,
            TopologyFixtures.NeighborResult(
                localChassisId: localChassis,
                lldp: [TopologyFixtures.Lldp(9, farChassis, "Gi0/0", systemName: farName)]),
            Cancellation);
    }

    private static async Task<Guid> SitedDeviceAsync(
        InventoryHost host,
        string hostname,
        string address,
        string site)
    {
        ApiResponse created = await host.Client.PostAsync(
            Devices,
            new CreateDeviceRequest(hostname, address, DeviceVendor.CiscoIos, Site: site, Role: DeviceRole.Switch),
            Cancellation);

        created.Status.Should().Be(201);

        return Read<DeviceDetail>(created).Id;
    }

    private static async Task SetSiteAsync(InventoryHost host, Guid deviceId, string site)
    {
        DeviceDetail device = Read<DeviceDetail>(
            await host.Client.GetAsync($"{Devices}/{deviceId}", Cancellation));

        ApiResponse updated = await host.Client.PutAsync(
            $"{Devices}/{deviceId}",
            new UpdateDeviceRequest(
                device.Hostname,
                device.PrimaryIpAddress,
                device.Vendor,
                device.Model,
                device.OsVersion,
                device.SerialNumber,
                site,
                device.Role,
                device.Criticality,
                device.Environment,
                device.Owner,
                device.Tags,
                device.Notes),
            Cancellation);

        updated.Status.Should().Be(200);
    }

    /// <summary>Walks every page of a graph and returns what arrived across all of them.</summary>
    private static async Task<(List<TopologyGraphNode> Nodes, List<TopologyGraphEdge> Edges, int Pages)>
        ReadEveryPageAsync(InventoryHost host, string route)
    {
        List<TopologyGraphNode> nodes = [];
        List<TopologyGraphEdge> edges = [];

        string? cursor = null;
        int pages = 0;

        do
        {
            string url = cursor is null ? route : $"{route}&cursor={Uri.EscapeDataString(cursor)}";

            ApiResponse response = await host.Client.GetAsync(url, Cancellation);

            response.Status.Should().Be(200);

            TopologyGraph page = Read<TopologyGraph>(response);

            nodes.AddRange(page.Nodes);
            edges.AddRange(page.Edges);
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null);

        return (nodes, edges, pages);
    }

    private static TopologyGraphNode Node(TopologyGraph graph, Guid deviceId) =>
        graph.Nodes.Single(node => node.DeviceId == deviceId);

    private static T Read<T>(ApiResponse response) =>
        JsonSerializer.Deserialize<T>(response.Body, JsonOptions)!;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}
