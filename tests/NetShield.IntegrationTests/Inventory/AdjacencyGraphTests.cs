using FluentAssertions;

using NetShield.Contracts.Inventory;

using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// The three-switch estate, which is what WP-2.1's headline criterion is measured against, and
/// the edges that come out of walking all of it.
/// </summary>
/// <remarks>
/// <para>
/// The same topology as <c>tests/fixtures/snmp/topology/</c> in the collector, described here in
/// the API's own terms: a core switch with two access switches under it, each link reported from
/// both ends, and the Cisco pair reporting the core over CDP as well as LLDP.
/// </para>
/// <code>
///                  core-sw-1  (LLDP only)
///                  Et1            Et2
///                   │              │
///                 Gi0/1          Gi0/1
///              acc-sw-1        acc-sw-2   (LLDP + CDP)
/// </code>
/// <para>
/// Two links. The correct edge set is therefore <em>two</em> edges and not four: both devices'
/// walks resolve the far end to a device, resolve its port against the interface inventory, and
/// compute the same canonical pair.
/// </para>
/// </remarks>
public sealed class AdjacencyGraphTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private const string CoreChassis = "00:1C:73:00:00:01";
    private const string AccessOneChassis = "00:1C:73:00:00:02";
    private const string AccessTwoChassis = "00:1C:73:00:00:03";

    private const string CoreAddress = "10.10.0.11";
    private const string AccessOneAddress = "10.10.0.12";
    private const string AccessTwoAddress = "10.10.0.13";

    [Fact]
    public async Task TheThreeSwitchEstate_ProducesTheCorrectEdgeSet()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Estate estate = await BuildAsync(host);

        await WalkCoreAsync(host, estate);
        await WalkAccessOneAsync(host, estate);
        await WalkAccessTwoAsync(host, estate);

        IReadOnlyList<AdjacencyRow> edges = await host.AdjacenciesAsync(Cancellation);

        // Two links, two edges. Six one-sided observations became two two-sided conclusions.
        edges.Should().HaveCount(2);
        edges.Should().OnlyContain(edge => edge.Confidence == AdjacencyConfidence.Confirmed);
        edges.Should().OnlyContain(edge => edge.ObservedFromA && edge.ObservedFromB);

        AdjacencyRow toAccessOne = Between(edges, estate.Core, estate.AccessOne);
        AdjacencyRow toAccessTwo = Between(edges, estate.Core, estate.AccessTwo);

        Ports(toAccessOne, estate.Core).Should().Be(1);
        Ports(toAccessOne, estate.AccessOne).Should().Be(1);
        Ports(toAccessTwo, estate.Core).Should().Be(2);
        Ports(toAccessTwo, estate.AccessTwo).Should().Be(1);

        // Each Cisco reported the core over both protocols, and both accounts resolved to the
        // same device — so they merged into one edge carrying two sources rather than two edges.
        toAccessOne.Sources.Should().BeEquivalentTo(["Cdp", "Lldp"]);
        toAccessTwo.Sources.Should().BeEquivalentTo(["Cdp", "Lldp"]);
    }

    [Fact]
    public async Task TheEdgeSet_IsTheSameWhicheverOrderTheDevicesAreWalkedIn()
    {
        // The canonical ordering is what makes this true, and it is the property the whole
        // converge-on-one-row design exists for.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Estate estate = await BuildAsync(host);

        await WalkAccessTwoAsync(host, estate);
        await WalkAccessOneAsync(host, estate);
        await WalkCoreAsync(host, estate);

        IReadOnlyList<AdjacencyRow> edges = await host.AdjacenciesAsync(Cancellation);

        edges.Should().HaveCount(2);
        edges.Should().OnlyContain(edge => edge.Confidence == AdjacencyConfidence.Confirmed);
    }

    [Fact]
    public async Task OneEndReportingALinkAlone_IsProbableUntilTheOtherConfirmsIt()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Estate estate = await BuildAsync(host);

        await WalkCoreAsync(host, estate);

        IReadOnlyList<AdjacencyRow> half = await host.AdjacenciesAsync(Cancellation);

        half.Should().HaveCount(2);
        half.Should().OnlyContain(edge => edge.Confidence == AdjacencyConfidence.Probable);

        await WalkAccessOneAsync(host, estate);

        IReadOnlyList<AdjacencyRow> both = await host.AdjacenciesAsync(Cancellation);

        both.Should().HaveCount(2);
        Between(both, estate.Core, estate.AccessOne)
            .Confidence.Should().Be(AdjacencyConfidence.Confirmed);
        Between(both, estate.Core, estate.AccessTwo)
            .Confidence.Should().Be(AdjacencyConfidence.Probable);
    }

    [Fact]
    public async Task OneSwitchGoingQuiet_DowngradesTheLinkRatherThanDeletingIt()
    {
        // The flags are per side for exactly this: the far end still reports the cable, and an
        // edge withdrawn on one device's silence would take a live link out of the graph.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Estate estate = await BuildAsync(host);

        await WalkCoreAsync(host, estate);
        await WalkAccessOneAsync(host, estate);

        Between(await host.AdjacenciesAsync(Cancellation), estate.Core, estate.AccessOne)
            .Confidence.Should().Be(AdjacencyConfidence.Confirmed);

        // acc-sw-1 now sees nothing, over both protocols, in a complete reading.
        await TopologyFixtures.NeighborWalkAsync(
            host,
            estate.AccessOne,
            TopologyFixtures.NeighborResult(
                localChassisId: AccessOneChassis,
                lldpSupported: true,
                cdpSupported: true),
            Cancellation);

        IReadOnlyList<AdjacencyRow> after = await host.AdjacenciesAsync(Cancellation);

        AdjacencyRow link = Between(after, estate.Core, estate.AccessOne);

        link.Confidence.Should().Be(AdjacencyConfidence.Probable);
        link.WithdrawnAt.Should().BeNull();
        link.Sources.Should().BeEquivalentTo(["Lldp"], "only the core still reports it");
    }

    [Fact]
    public async Task ALinkBothEndsStopReporting_IsWithdrawn()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Estate estate = await BuildAsync(host);

        await WalkCoreAsync(host, estate);
        await WalkAccessOneAsync(host, estate);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            estate.AccessOne,
            TopologyFixtures.NeighborResult(
                localChassisId: AccessOneChassis,
                lldpSupported: true,
                cdpSupported: true),
            Cancellation);

        // And the core now sees only acc-sw-2.
        await TopologyFixtures.NeighborWalkAsync(
            host,
            estate.Core,
            TopologyFixtures.NeighborResult(
                localChassisId: CoreChassis,
                localSystemName: "core-sw-1",
                lldp:
                [
                    TopologyFixtures.Lldp(
                        2,
                        AccessTwoChassis,
                        "Gi0/1",
                        systemName: "acc-sw-2",
                        managementAddress: AccessTwoAddress)
                ]),
            Cancellation);

        IReadOnlyList<AdjacencyRow> live = await host.AdjacenciesAsync(Cancellation);

        live.Should().ContainSingle();
        live[0].BDeviceId.Should().NotBeNull();

        IReadOnlyList<AdjacencyRow> all =
            await host.AdjacenciesAsync(Cancellation, includeWithdrawn: true);

        all.Should().HaveCount(2);
    }

    [Fact]
    public async Task TwoCablesBetweenOnePairOfSwitches_AreTwoEdges()
    {
        // An aggregate is two cables. Including the observing device's own port in the edge's
        // identity is what stops them collapsing into one link.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Estate estate = await BuildAsync(host);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            estate.Core,
            TopologyFixtures.NeighborResult(
                localChassisId: CoreChassis,
                lldp:
                [
                    TopologyFixtures.Lldp(
                        1,
                        AccessOneChassis,
                        "Gi0/1",
                        systemName: "acc-sw-1",
                        managementAddress: AccessOneAddress),
                    TopologyFixtures.Lldp(
                        2,
                        AccessOneChassis,
                        "Gi0/2",
                        systemName: "acc-sw-1",
                        managementAddress: AccessOneAddress)
                ]),
            Cancellation);

        IReadOnlyList<AdjacencyRow> edges = await host.AdjacenciesAsync(Cancellation);

        edges.Should().HaveCount(2);
        edges.Select(edge => edge.AIfIndex).Should().BeEquivalentTo([1, 2]);
        edges.Should().OnlyContain(edge => edge.BDeviceId != null);
    }

    [Fact]
    public async Task AFarEndWhosePortCannotBeResolved_StaysTheObservingDevicesOwnEdge()
    {
        // Without the far port there is nothing to canonicalise on, and asserting the two
        // accounts are of one link on the strength of a device id alone would collapse an
        // aggregate. Two honest edges beat one invented one.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Estate estate = await BuildAsync(host, seedInterfaces: false);

        await WalkCoreAsync(host, estate);
        await WalkAccessOneAsync(host, estate);

        IReadOnlyList<AdjacencyRow> edges = await host.AdjacenciesAsync(Cancellation);

        edges.Should().HaveCount(3, "the core's two edges and acc-sw-1's own account of one");
        edges.Should().OnlyContain(edge => edge.Confidence == AdjacencyConfidence.Probable);
        edges.Should().OnlyContain(edge => edge.BIfIndex == null);
    }

    // --- The L3 half -----------------------------------------------------------------------------

    [Fact]
    public async Task ARoutingNextHopAlone_IsAPossibleEdge()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Estate estate = await BuildAsync(host);

        await TopologyFixtures.RouteWalkAsync(
            host,
            estate.AccessOne,
            TopologyFixtures.RouteResult(
                nextHops: [TopologyFixtures.NextHop(CoreAddress, 1, routeCount: 42)],
                routeCount: 812),
            Cancellation);

        IReadOnlyList<AdjacencyRow> edges = await host.AdjacenciesAsync(Cancellation);

        edges.Should().ContainSingle();
        edges[0].Confidence.Should().Be(AdjacencyConfidence.Possible);
        edges[0].Sources.Should().BeEquivalentTo(["Routing"]);
        edges[0].BDeviceId.Should().Be(estate.Core, "the gateway is a device NetShield monitors");

        IReadOnlyList<NeighborRow> observations = await host.NeighborsAsync(Cancellation);

        observations.Should().ContainSingle();
        observations[0].EvidenceCount.Should().Be(42, "how much of the table leans on this gateway");
    }

    [Fact]
    public async Task ARoutingObservationOfALinkLldpAlreadyFound_MergesIntoTheSameEdge()
    {
        // Layer 3 confirming a cable layer 2 already established is another source, not another
        // edge — and it does not weaken what LLDP had already concluded.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Estate estate = await BuildAsync(host);

        await WalkAccessOneAsync(host, estate);

        await TopologyFixtures.RouteWalkAsync(
            host,
            estate.AccessOne,
            TopologyFixtures.RouteResult(
                nextHops: [TopologyFixtures.NextHop(CoreAddress, 1)]),
            Cancellation);

        IReadOnlyList<AdjacencyRow> edges = await host.AdjacenciesAsync(Cancellation);

        edges.Should().ContainSingle();
        edges[0].Sources.Should().BeEquivalentTo(["Cdp", "Lldp", "Routing"]);
        edges[0].Confidence.Should().Be(AdjacencyConfidence.Probable);
    }

    [Fact]
    public async Task ARouteWalkThatFails_LeavesEveryNeighbourEdgeStanding()
    {
        // The whole reason routes are a separate walk. A device that answers its neighbour
        // protocols and then times out reading forty thousand routes keeps the topology it
        // already established.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Estate estate = await BuildAsync(host);

        await WalkAccessOneAsync(host, estate);

        IReadOnlyList<AdjacencyRow> before = await host.AdjacenciesAsync(Cancellation);

        ApiResponse queued =
            await TopologyFixtures.RequestRouteWalkAsync(host, estate.AccessOne, Cancellation);

        queued.Status.Should().Be(202);

        TopologyFixtures.LeasedWalk walk = await TopologyFixtures.LeaseAsync(host, Cancellation);

        await TopologyFixtures.ReportAsync(
            host,
            walk,
            data: null,
            Cancellation,
            outcome: "Failed",
            detail: "10.10.0.12 stopped answering partway through inetCidrRouteTable.");

        IReadOnlyList<AdjacencyRow> after = await host.AdjacenciesAsync(Cancellation);

        after.Should().HaveCount(before.Count);
        after[0].Sources.Should().BeEquivalentTo(["Cdp", "Lldp"]);

        TopologyScanRow? scan = await host.TopologyScanAsync(estate.AccessOne, Cancellation);

        scan!.LastRouteError.Should().Contain("stopped answering");
        scan.LldpSupported.Should().BeTrue("the neighbour walk's own record is untouched");
    }

    [Fact]
    public async Task ARouteWalkOfADeviceWithNoRoutingTable_RecordsThatRatherThanWithdrawing()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Estate estate = await BuildAsync(host);

        await TopologyFixtures.RouteWalkAsync(
            host,
            estate.AccessOne,
            TopologyFixtures.RouteResult(
                nextHops: [TopologyFixtures.NextHop(CoreAddress, 1)]),
            Cancellation);

        (await host.AdjacenciesAsync(Cancellation)).Should().ContainSingle();

        await TopologyFixtures.RouteWalkAsync(
            host,
            estate.AccessOne,
            TopologyFixtures.RouteResult(routesSupported: false, routeTable: null),
            Cancellation);

        (await host.AdjacenciesAsync(Cancellation)).Should().ContainSingle();

        TopologyScanRow? scan = await host.TopologyScanAsync(estate.AccessOne, Cancellation);

        scan!.RoutingSupported.Should().BeFalse();
        scan.RouteTable.Should().BeNull();
    }

    [Fact]
    public async Task AGatewayThatStopsBeingRoutedThrough_AgesOut()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Estate estate = await BuildAsync(host);

        await TopologyFixtures.RouteWalkAsync(
            host,
            estate.AccessOne,
            TopologyFixtures.RouteResult(
                nextHops:
                [
                    TopologyFixtures.NextHop(CoreAddress, 1),
                    TopologyFixtures.NextHop(AccessTwoAddress, 2)
                ]),
            Cancellation);

        (await host.AdjacenciesAsync(Cancellation)).Should().HaveCount(2);

        await TopologyFixtures.RouteWalkAsync(
            host,
            estate.AccessOne,
            TopologyFixtures.RouteResult(nextHops: [TopologyFixtures.NextHop(CoreAddress, 1)]),
            Cancellation);

        (await host.AdjacenciesAsync(Cancellation)).Should().ContainSingle();
    }

    [Fact]
    public async Task ARouteWalk_RecordsHowBigTheTableWasBeforeTheReduction()
    {
        // The count an operator reads to tell a default route from a transit table.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Estate estate = await BuildAsync(host);

        await TopologyFixtures.RouteWalkAsync(
            host,
            estate.AccessOne,
            TopologyFixtures.RouteResult(
                nextHops: [TopologyFixtures.NextHop(CoreAddress, 1, routeCount: 800)],
                routeCount: 812),
            Cancellation);

        TopologyScanRow? scan = await host.TopologyScanAsync(estate.AccessOne, Cancellation);

        scan!.RoutingSupported.Should().BeTrue();
        scan.RouteTable.Should().Be("inetCidrRoute");
        scan.LastRouteCount.Should().Be(812);
    }

    // --- The estate -------------------------------------------------------------------------------

    /// <summary>The three switches, their credentials and their interface inventories.</summary>
    private static async Task<Estate> BuildAsync(InventoryHost host, bool seedInterfaces = true)
    {
        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host,
            Cancellation,
            "core-sw-1",
            CoreAddress);

        Guid accessOne = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host,
            Cancellation,
            "acc-sw-1",
            AccessOneAddress);

        Guid accessTwo = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host,
            Cancellation,
            "acc-sw-2",
            AccessTwoAddress);

        if (seedInterfaces)
        {
            // What WP-1.5's fingerprint walk records. The reconciler resolves a neighbour's
            // advertised port against it, which is what lets both ends of a link agree on one row.
            await host.SeedInterfacesAsync(
                core,
                [(1, "Et1", "Ethernet1", null), (2, "Et2", "Ethernet2", null)],
                Cancellation);

            await host.SeedInterfacesAsync(
                accessOne,
                [(1, "Gi0/1", "GigabitEthernet0/1", null), (2, "Gi0/2", "GigabitEthernet0/2", null)],
                Cancellation);

            await host.SeedInterfacesAsync(
                accessTwo,
                [(1, "Gi0/1", "GigabitEthernet0/1", null)],
                Cancellation);
        }

        return new Estate(core, accessOne, accessTwo);
    }

    private static Task WalkCoreAsync(InventoryHost host, Estate estate) =>
        TopologyFixtures.NeighborWalkAsync(
            host,
            estate.Core,
            TopologyFixtures.NeighborResult(
                localChassisId: CoreChassis,
                localSystemName: "core-sw-1",
                lldp:
                [
                    TopologyFixtures.Lldp(
                        1,
                        AccessOneChassis,
                        "Gi0/1",
                        systemName: "acc-sw-1",
                        managementAddress: AccessOneAddress,
                        localPortName: "Et1"),
                    TopologyFixtures.Lldp(
                        2,
                        AccessTwoChassis,
                        "Gi0/1",
                        systemName: "acc-sw-2",
                        managementAddress: AccessTwoAddress,
                        localPortName: "Et2")
                ],
                cdpSupported: false),
            Cancellation);

    private static Task WalkAccessOneAsync(InventoryHost host, Estate estate) =>
        TopologyFixtures.NeighborWalkAsync(
            host,
            estate.AccessOne,
            TopologyFixtures.NeighborResult(
                localChassisId: AccessOneChassis,
                localSystemName: "acc-sw-1",
                lldp:
                [
                    TopologyFixtures.Lldp(
                        1,
                        CoreChassis,
                        "Et1",
                        systemName: "core-sw-1",
                        managementAddress: CoreAddress,
                        localPortName: "Gi0/1")
                ],
                cdp: [TopologyFixtures.Cdp(1, "core-sw-1", "Ethernet1", CoreAddress, "Gi0/1")]),
            Cancellation);

    private static Task WalkAccessTwoAsync(InventoryHost host, Estate estate) =>
        TopologyFixtures.NeighborWalkAsync(
            host,
            estate.AccessTwo,
            TopologyFixtures.NeighborResult(
                localChassisId: AccessTwoChassis,
                localSystemName: "acc-sw-2",
                lldp:
                [
                    TopologyFixtures.Lldp(
                        1,
                        CoreChassis,
                        "Et2",
                        systemName: "core-sw-1",
                        managementAddress: CoreAddress,
                        localPortName: "Gi0/1")
                ],
                cdp: [TopologyFixtures.Cdp(1, "core-sw-1", "Ethernet2", CoreAddress, "Gi0/1")]),
            Cancellation);

    /// <summary>The edge between two devices, from whichever end each happens to be stored at.</summary>
    private static AdjacencyRow Between(
        IReadOnlyList<AdjacencyRow> edges,
        Guid first,
        Guid second) =>
        edges.Single(edge =>
            (edge.ADeviceId == first && edge.BDeviceId == second)
            || (edge.ADeviceId == second && edge.BDeviceId == first));

    /// <summary>Which interface one device holds an edge on.</summary>
    private static int? Ports(AdjacencyRow edge, Guid deviceId) =>
        edge.ADeviceId == deviceId ? edge.AIfIndex : edge.BIfIndex;

    private sealed record Estate(Guid Core, Guid AccessOne, Guid AccessTwo);
}
