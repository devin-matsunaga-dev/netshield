using FluentAssertions;

using NetShield.Contracts.Inventory;
using NetShield.Contracts.Inventory.Events;

using NetShield.IntegrationTests.Collector;
using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// The whole round trip: a topology walk queued, leased, reported and folded into observations
/// and edges.
/// </summary>
/// <remarks>
/// Against a real PostgreSQL, because the guarantees this package makes are ones the database
/// carries: the partial unique index that makes two live observations of one port from one
/// protocol impossible, the one that makes two devices' walks converge on one edge row rather
/// than race to write two, and the transaction that carries every edge and the event announcing
/// it together or not at all.
/// </remarks>
public sealed class NeighborCollectionTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private const string CoreChassis = "00:1C:73:00:00:01";
    private const string AccessChassis = "00:1C:73:00:00:02";

    [Fact]
    public async Task AWalkThatReadsAnLldpTable_RecordsAnEdgeToWhatItSaw()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host,
            Cancellation,
            "core-sw-1",
            "10.10.0.1");

        await TopologyFixtures.NeighborWalkAsync(
            host,
            core,
            TopologyFixtures.NeighborResult(
                localChassisId: CoreChassis,
                localSystemName: "core-sw-1",
                lldp: [TopologyFixtures.Lldp(1, AccessChassis, "Gi0/1", systemName: "acc-sw-1")]),
            Cancellation);

        IReadOnlyList<AdjacencyRow> edges = await host.AdjacenciesAsync(Cancellation);

        edges.Should().ContainSingle();
        edges[0].ADeviceId.Should().Be(core);
        edges[0].AIfIndex.Should().Be(1);
        edges[0].BChassisId.Should().Be(AccessChassis);
        edges[0].BPortId.Should().Be("Gi0/1");
        edges[0].BSystemName.Should().Be("acc-sw-1");
        edges[0].Sources.Should().BeEquivalentTo(["Lldp"]);
    }

    [Fact]
    public async Task AnEdgeToSomethingThatIsNotADevice_IsProbableRatherThanConfirmed()
    {
        // The ordinary case for a server, a phone or an unmanaged switch: one end reported it and
        // the far end will never confirm, because nothing is asking the far end.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            core,
            TopologyFixtures.NeighborResult(
                lldp: [TopologyFixtures.Lldp(1, "AA:BB:CC:DD:EE:FF", "eth0", systemName: "app-01")]),
            Cancellation);

        IReadOnlyList<AdjacencyRow> edges = await host.AdjacenciesAsync(Cancellation);

        edges.Should().ContainSingle();
        edges[0].BDeviceId.Should().BeNull();
        edges[0].Confidence.Should().Be(AdjacencyConfidence.Probable);
    }

    [Fact]
    public async Task AFailedWalk_ChangesNoEdgeAndSaysWhy()
    {
        // A collector's health is never evidence about the estate. Letting an unreachable device
        // withdraw its edges would report an estate coming apart because one switch stopped
        // answering SNMP.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            core,
            TopologyFixtures.NeighborResult(
                lldp: [TopologyFixtures.Lldp(1, AccessChassis, "Gi0/1")]),
            Cancellation);

        await TopologyFixtures.FailNeighborWalkAsync(
            host,
            core,
            "192.0.2.10 did not answer within 5s.",
            Cancellation);

        (await host.AdjacenciesAsync(Cancellation)).Should().ContainSingle();

        TopologyScanRow? scan = await host.TopologyScanAsync(core, Cancellation);

        scan.Should().NotBeNull();
        scan!.LastNeighborError.Should().Contain("did not answer");
    }

    [Fact]
    public async Task AProtocolTheDeviceDoesNotImplement_WithdrawsNothing()
    {
        // "This is not a Cisco" and "this Cisco's CDP cache is empty" are different facts, and
        // only the second is grounds for withdrawing a CDP edge.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            core,
            TopologyFixtures.NeighborResult(
                lldp: [TopologyFixtures.Lldp(1, AccessChassis, "Gi0/1")],
                cdp: [TopologyFixtures.Cdp(2, "ghost-sw-7", "Gi1/1")]),
            Cancellation);

        (await host.AdjacenciesAsync(Cancellation)).Should().HaveCount(2);

        // The same LLDP reading, and CDP now reported as unsupported rather than empty.
        await TopologyFixtures.NeighborWalkAsync(
            host,
            core,
            TopologyFixtures.NeighborResult(
                lldp: [TopologyFixtures.Lldp(1, AccessChassis, "Gi0/1")],
                cdpSupported: false),
            Cancellation);

        (await host.AdjacenciesAsync(Cancellation)).Should().HaveCount(2);
    }

    [Fact]
    public async Task ATruncatedReading_WithdrawsNothing()
    {
        // A reading that hit its ceiling saw part of a table, so nothing absent from it is absent
        // from the device. The first package to act on a flag every walk payload has carried
        // since WP-1.5.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            core,
            TopologyFixtures.NeighborResult(
                lldp:
                [
                    TopologyFixtures.Lldp(1, AccessChassis, "Gi0/1"),
                    TopologyFixtures.Lldp(2, "00:1C:73:00:00:03", "Gi0/1")
                ]),
            Cancellation);

        (await host.AdjacenciesAsync(Cancellation)).Should().HaveCount(2);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            core,
            TopologyFixtures.NeighborResult(
                lldp: [TopologyFixtures.Lldp(1, AccessChassis, "Gi0/1")],
                lldpTruncated: true),
            Cancellation);

        (await host.AdjacenciesAsync(Cancellation)).Should().HaveCount(2);
    }

    [Fact]
    public async Task ARemovedLink_AgesOut()
    {
        // The WP-2.1 criterion. An LLDP table is the device's complete current statement about
        // what it can see and its agent expires its own entries, so an entry a successful,
        // untruncated reading no longer contains is a link that has gone.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            core,
            TopologyFixtures.NeighborResult(
                lldp:
                [
                    TopologyFixtures.Lldp(1, AccessChassis, "Gi0/1"),
                    TopologyFixtures.Lldp(2, "00:1C:73:00:00:03", "Gi0/1")
                ]),
            Cancellation);

        (await host.AdjacenciesAsync(Cancellation)).Should().HaveCount(2);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            core,
            TopologyFixtures.NeighborResult(
                lldp: [TopologyFixtures.Lldp(1, AccessChassis, "Gi0/1")]),
            Cancellation);

        IReadOnlyList<AdjacencyRow> live = await host.AdjacenciesAsync(Cancellation);

        live.Should().ContainSingle();
        live[0].AIfIndex.Should().Be(1);

        // The row survives, withdrawn, so "this link existed until Tuesday" stays answerable.
        IReadOnlyList<AdjacencyRow> all =
            await host.AdjacenciesAsync(Cancellation, includeWithdrawn: true);

        all.Should().HaveCount(2);
        all.Should().ContainSingle(edge => edge.WithdrawnAt != null);
    }

    [Fact]
    public async Task AWalkThatFindsTheSameLinkAgain_LeavesItsDiscoveryTimeAlone()
    {
        // "Discovered at and last seen" is the WP entry's own wording, and they mean different
        // things: a link that has been up for a month should say so.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        string result = TopologyFixtures.NeighborResult(
            lldp: [TopologyFixtures.Lldp(1, AccessChassis, "Gi0/1")]);

        await TopologyFixtures.NeighborWalkAsync(host, core, result, Cancellation);

        AdjacencyRow first = (await host.AdjacenciesAsync(Cancellation))[0];

        await TopologyFixtures.NeighborWalkAsync(host, core, result, Cancellation);

        AdjacencyRow second = (await host.AdjacenciesAsync(Cancellation))[0];

        second.Id.Should().Be(first.Id);
        second.FirstDiscoveredAt.Should().Be(first.FirstDiscoveredAt);
        second.LastSeenAt.Should().BeOnOrAfter(first.LastSeenAt);
    }

    [Fact]
    public async Task ConflictingLldpAndCdpOnOnePort_ResolveToTheLldpNeighbour()
    {
        // The WP-2.1 criterion, end to end. An LLDP chassis identifier is an identity; a CDP
        // device id is a host name, which WP-1.1 settled is not one.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            core,
            TopologyFixtures.NeighborResult(
                lldp: [TopologyFixtures.Lldp(1, AccessChassis, "Gi0/1", systemName: "acc-sw-1")],
                cdp: [TopologyFixtures.Cdp(1, "ghost-sw-7", "Gi1/1")]),
            Cancellation);

        IReadOnlyList<AdjacencyRow> edges = await host.AdjacenciesAsync(Cancellation);

        edges.Should().ContainSingle();
        edges[0].BChassisId.Should().Be(AccessChassis);
        edges[0].Sources.Should().BeEquivalentTo(["Lldp"]);
    }

    [Fact]
    public async Task TheSuppressedCdpAccount_IsKeptAsEvidenceWithNoEdge()
    {
        // Losing the argument is not being deleted. An operator asking why NetShield believes
        // what it believes should be able to see the account it set aside.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            core,
            TopologyFixtures.NeighborResult(
                lldp: [TopologyFixtures.Lldp(1, AccessChassis, "Gi0/1")],
                cdp: [TopologyFixtures.Cdp(1, "ghost-sw-7", "Gi1/1")]),
            Cancellation);

        IReadOnlyList<NeighborRow> observations = await host.NeighborsAsync(Cancellation);

        observations.Should().HaveCount(2);
        observations.Should().ContainSingle(row =>
            row.Source == NeighborSource.Cdp && row.AdjacencyId == null);
        observations.Should().ContainSingle(row =>
            row.Source == NeighborSource.Lldp && row.AdjacencyId != null);
    }

    [Fact]
    public async Task ConflictResolution_IsTheSameWhicheverOrderTheProtocolsArrive()
    {
        // Deterministic in the sense that matters: the answer depends on what was reported and
        // not on how the collector happened to order its payload.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            core,
            TopologyFixtures.NeighborResult(
                cdp: [TopologyFixtures.Cdp(1, "ghost-sw-7", "Gi1/1")],
                lldp: [TopologyFixtures.Lldp(1, AccessChassis, "Gi0/1")]),
            Cancellation);

        IReadOnlyList<AdjacencyRow> edges = await host.AdjacenciesAsync(Cancellation);

        edges.Should().ContainSingle();
        edges[0].BChassisId.Should().Be(AccessChassis);
    }

    [Fact]
    public async Task ACdpNeighbourOnAPortLldpSaysNothingAbout_BecomesAnEdge()
    {
        // CDP does not lose to LLDP in general — only on a port they disagree about.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            core,
            TopologyFixtures.NeighborResult(
                lldp: [TopologyFixtures.Lldp(1, AccessChassis, "Gi0/1")],
                cdp: [TopologyFixtures.Cdp(2, "ap-01", "Gi0/0")]),
            Cancellation);

        IReadOnlyList<AdjacencyRow> edges = await host.AdjacenciesAsync(Cancellation);

        edges.Should().HaveCount(2);
        edges.Should().ContainSingle(edge => edge.AIfIndex == 2 && edge.BSystemName == "ap-01");
    }

    [Fact]
    public async Task AWalkThatChangesTheEdgeSet_PublishesDeviceAdjacencyChanged()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            core,
            TopologyFixtures.NeighborResult(
                lldp: [TopologyFixtures.Lldp(1, AccessChassis, "Gi0/1")]),
            Cancellation);

        IReadOnlyList<DeviceAdjacencyChanged> published =
            await host.OutboxPayloadsAsync<DeviceAdjacencyChanged>(Cancellation);

        published.Should().ContainSingle();
        published[0].DeviceId.Should().Be(core);
        published[0].Source.Should().Be(TopologyWalkKind.Neighbors);
        published[0].Added.Should().Be(1);
        published[0].EdgeCount.Should().Be(1);
    }

    [Fact]
    public async Task AWalkThatFindsWhatItAlreadyKnew_PublishesNothing()
    {
        // The rule DeviceFingerprinted and DeviceCredentialProfilesChanged already follow: a
        // subscriber rebuilding a graph should not be woken every time the schedule confirms what
        // it already knew, and this walk runs on every device on an interval.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        string result = TopologyFixtures.NeighborResult(
            lldp: [TopologyFixtures.Lldp(1, AccessChassis, "Gi0/1")]);

        await TopologyFixtures.NeighborWalkAsync(host, core, result, Cancellation);
        await TopologyFixtures.NeighborWalkAsync(host, core, result, Cancellation);

        (await host.OutboxPayloadsAsync<DeviceAdjacencyChanged>(Cancellation)).Should().ContainSingle();
    }

    [Fact]
    public async Task ARedeliveredWalk_IsANoOp()
    {
        // Outbox delivery is at-least-once. A second application arriving after the far device
        // had been walked would withdraw edges the first application's evidence had already been
        // spent on.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        TopologyFixtures.LeasedWalk walk = await Queue(host, core);

        string result = TopologyFixtures.NeighborResult(
            lldp: [TopologyFixtures.Lldp(1, AccessChassis, "Gi0/1")]);

        await TopologyFixtures.ReportAsync(host, walk, result, Cancellation);

        AdjacencyRow first = (await host.AdjacenciesAsync(Cancellation))[0];

        await host.RedeliverOutboxAsync(Cancellation);

        IReadOnlyList<AdjacencyRow> after = await host.AdjacenciesAsync(Cancellation);

        after.Should().ContainSingle();
        after[0].LastSeenAt.Should().Be(first.LastSeenAt);
    }

    [Fact]
    public async Task AWalkOfADeviceRemovedWhileItWasInFlight_IsDropped()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        TopologyFixtures.LeasedWalk walk = await Queue(host, core);

        ApiResponse removed = await host.Client.DeleteAsync($"/api/v1/devices/{core}", Cancellation);
        removed.Status.Should().Be(204);

        await TopologyFixtures.ReportAsync(
            host,
            walk,
            TopologyFixtures.NeighborResult(
                lldp: [TopologyFixtures.Lldp(1, AccessChassis, "Gi0/1")]),
            Cancellation);

        (await host.AdjacenciesAsync(Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task ASecondWalkOfADeviceThatAlreadyHasOne_IsRefused()
    {
        // More than a convenience here: an edge is withdrawn when a complete reading no longer
        // contains it, so two walks applied in whichever order they came back would have each
        // withdrawing what the other had just established.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        (await TopologyFixtures.RequestNeighborWalkAsync(host, core, Cancellation))
            .Status.Should().Be(202);

        ApiResponse second = await TopologyFixtures.RequestNeighborWalkAsync(host, core, Cancellation);

        second.Status.Should().Be(409);
        second.Json.GetProperty("code").GetString().Should().Be("topology.walk-outstanding");
    }

    [Fact]
    public async Task ARouteWalkIsRefusedWhileANeighbourWalkIsOutstanding()
    {
        // One SNMP conversation per device at a time, which is the rule WP-1.8 settled and this
        // package inherits: a fingerprint, a client read and a neighbour read all open a session
        // to the same agent with the same credential.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        (await TopologyFixtures.RequestNeighborWalkAsync(host, core, Cancellation))
            .Status.Should().Be(202);

        (await TopologyFixtures.RequestRouteWalkAsync(host, core, Cancellation))
            .Status.Should().Be(409);
    }

    [Fact]
    public async Task AWalkOfADeviceThatDoesNotExist_IsANotFound()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse response =
            await TopologyFixtures.RequestNeighborWalkAsync(host, Guid.NewGuid(), Cancellation);

        response.Status.Should().Be(404);
    }

    [Fact]
    public async Task AWalkOfADeviceWithNoSnmpCredential_IsRefused()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(
            host,
            "unwalkable",
            "10.10.0.9",
            Cancellation);

        ApiResponse response =
            await TopologyFixtures.RequestNeighborWalkAsync(host, deviceId, Cancellation);

        // 409 rather than 422, which is the answer WP-1.5 settled for the same refusal: nothing
        // is wrong with the request, and the same request succeeds once a credential is assigned.
        response.Status.Should().Be(409);
        response.Json.GetProperty("code").GetString().Should().Be("discovery.no-snmp-credential");
    }

    [Fact]
    public async Task AnLldpEntryOnAnInterfaceTheCollectorCouldNotResolve_IsDropped()
    {
        // The collector drops these before they are reported; the API refuses one anyway, because
        // an edge on an interface the rest of NetShield cannot identify is not an edge.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            core,
            """
            {
              "walk": "neighbors",
              "localChassisId": null,
              "localChassisIdKind": "Unknown",
              "localSystemName": null,
              "lldpSupported": true,
              "lldpCount": 1,
              "lldpTruncated": false,
              "lldp": [
                {
                  "localIfIndex": null,
                  "localPortName": null,
                  "chassisId": "00:1C:73:00:00:02",
                  "chassisIdKind": "MacAddress",
                  "portId": "Gi0/1",
                  "portIdKind": "InterfaceName",
                  "portDescription": null,
                  "systemName": null,
                  "systemDescription": null,
                  "managementAddress": null,
                  "capabilities": null
                }
              ],
              "cdpSupported": false,
              "cdpCount": 0,
              "cdpTruncated": false,
              "cdp": []
            }
            """,
            Cancellation);

        (await host.AdjacenciesAsync(Cancellation)).Should().BeEmpty();
        (await host.NeighborsAsync(Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task AnIdentifierKindThisBuildDoesNotName_ReadsAsUnknownRatherThanFailing()
    {
        // A collector newer than the API leaves an edge that reads Unknown rather than failing
        // the walk — the direction WP-1.5 chose for an unrecognised vendor.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            core,
            TopologyFixtures.NeighborResult(
                lldp: [TopologyFixtures.Lldp(1, "something", "p1", chassisIdKind: "Telepathy")]),
            Cancellation);

        IReadOnlyList<AdjacencyRow> edges = await host.AdjacenciesAsync(Cancellation);

        edges.Should().ContainSingle();
        edges[0].BChassisIdKind.Should().Be(NeighborIdKind.Unknown);
    }

    [Fact]
    public async Task AResultThatNamesAnotherWalk_IsNotRead()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            core,
            TopologyFixtures.NeighborResult(
                lldp: [TopologyFixtures.Lldp(1, AccessChassis, "Gi0/1")],
                walk: "clients"),
            Cancellation);

        (await host.AdjacenciesAsync(Cancellation)).Should().BeEmpty();

        TopologyScanRow? scan = await host.TopologyScanAsync(core, Cancellation);

        scan!.LastNeighborError.Should().Contain("could not read");
    }

    private static async Task<TopologyFixtures.LeasedWalk> Queue(InventoryHost host, Guid deviceId)
    {
        ApiResponse queued =
            await TopologyFixtures.RequestNeighborWalkAsync(host, deviceId, Cancellation);

        queued.Status.Should().Be(202);

        return await TopologyFixtures.LeaseAsync(host, Cancellation);
    }
}
