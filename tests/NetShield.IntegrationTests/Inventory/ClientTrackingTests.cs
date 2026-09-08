using FluentAssertions;

using NetShield.Contracts.Inventory.Events;

using NetShield.IntegrationTests.Collector;
using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// The whole round trip: a client walk queued, leased, reported and folded into closed intervals.
/// </summary>
/// <remarks>
/// Against a real PostgreSQL, because every guarantee this package makes is one the database
/// carries: the partial unique index that makes two open bindings for one address impossible, the
/// <c>inet</c> column that stops one address being stored under two spellings, and the
/// transaction that carries every binding and the event announcing a new client together or not
/// at all.
/// </remarks>
public sealed class ClientTrackingTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private const string Laptop = "AA:BB:CC:00:00:21";
    // Both are unicast — an even first octet — because a group address is dropped where an
    // observation is read rather than recorded as a client. Both carry the local bit, which is
    // what an address somebody assigned looks like.
    private const string Printer = "AE:BB:CC:00:00:42";

    [Fact]
    public async Task AWalkThatReadsAnArpTable_RecordsEachEndpointAndTheAddressItHeld()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await ClientFixtures.WalkAsync(
            host,
            deviceId,
            ClientFixtures.WalkResult(
                neighbors:
                [
                    ClientFixtures.Neighbor("10.10.0.21", Laptop),
                    ClientFixtures.Neighbor("10.10.0.42", Printer)
                ]),
            Cancellation);

        IReadOnlyList<ClientRow> clients = await host.ClientsAsync(Cancellation);

        clients.Should().HaveCount(2);
        clients.Select(client => client.MacAddress).Should().BeEquivalentTo([Laptop, Printer]);

        IReadOnlyList<ClientIpBindingRow> bindings = await host.IpBindingsAsync(Cancellation);

        bindings.Should().HaveCount(2);
        bindings.Should().OnlyContain(binding => binding.ObservedTo == null);
    }

    [Fact]
    public async Task AWalkThatSpellsAMacDifferently_FindsTheSameClient()
    {
        // A client is keyed by its MAC and by nothing else, so an address that normalises two
        // ways would be two clients each holding half of one endpoint's history.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await ClientFixtures.WalkAsync(
            host,
            deviceId,
            ClientFixtures.WalkResult(neighbors: [ClientFixtures.Neighbor("10.10.0.21", Laptop)]),
            Cancellation);

        await ClientFixtures.WalkAsync(
            host,
            deviceId,
            ClientFixtures.WalkResult(neighbors: [ClientFixtures.Neighbor("10.10.0.21", "aabb.cc00.0021")]),
            Cancellation);

        (await host.ClientsAsync(Cancellation)).Should().ContainSingle();
    }

    [Fact]
    public async Task AWalkThatReadsAForwardingDatabase_RecordsWhichPortReportedEachEndpoint()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await ClientFixtures.WalkAsync(
            host,
            deviceId,
            ClientFixtures.WalkResult(
                forwarding:
                [
                    ClientFixtures.Forwarding(Laptop, ifIndex: 1, vlanId: 10, macCountOnPort: 1),
                    ClientFixtures.Forwarding(Printer, ifIndex: 48, vlanId: 20, macCountOnPort: 214)
                ]),
            Cancellation);

        IReadOnlyList<ClientPortBindingRow> ports = await host.PortBindingsAsync(Cancellation);

        ports.Should().HaveCount(2);

        ClientPortBindingRow uplink = ports.Single(port => port.MacAddress == Printer);

        uplink.IfIndex.Should().Be(48);
        uplink.VlanId.Should().Be(20);
        // The evidence that tells an access port from a trunk without a topology to ask.
        uplink.MacCountOnPort.Should().Be(214);
    }

    [Fact]
    public async Task ADeviceReportingAnEndpointItAlreadyReported_ConfirmsRatherThanReopening()
    {
        // A switch re-reports every endpoint on it at every walk. If confirmation opened a new
        // interval, an ordinary laptop would accumulate one every fifteen minutes.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        string result = ClientFixtures.WalkResult(
            neighbors: [ClientFixtures.Neighbor("10.10.0.21", Laptop)],
            forwarding: [ClientFixtures.Forwarding(Laptop, ifIndex: 1)]);

        await ClientFixtures.WalkAsync(host, deviceId, result, Cancellation);
        await ClientFixtures.WalkAsync(host, deviceId, result, Cancellation);
        await ClientFixtures.WalkAsync(host, deviceId, result, Cancellation);

        (await host.IpBindingsAsync(Cancellation)).Should().ContainSingle();
        (await host.PortBindingsAsync(Cancellation)).Should().ContainSingle();
    }

    [Fact]
    public async Task AnEndpointThatStopsAnsweringArp_KeepsItsOpenBinding()
    {
        // An ARP cache ages an entry out after a few hours whether or not the host is still
        // there. Closing on absence would put a gap in the history for an address that never
        // actually changed hands, and ResolveAssetAt would answer "unresolved" inside it.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await ClientFixtures.WalkAsync(
            host,
            deviceId,
            ClientFixtures.WalkResult(neighbors: [ClientFixtures.Neighbor("10.10.0.21", Laptop)]),
            Cancellation);

        await ClientFixtures.WalkAsync(
            host,
            deviceId,
            ClientFixtures.WalkResult(neighbors: [], neighborsSupported: true),
            Cancellation);

        IReadOnlyList<ClientIpBindingRow> bindings = await host.IpBindingsAsync(Cancellation);

        bindings.Should().ContainSingle();
        bindings[0].ObservedTo.Should().BeNull(
            "an interval closes only when a conflicting observation supersedes it");
    }

    [Fact]
    public async Task AnEndpointMovingToAnotherPortOfTheSameDevice_ClosesTheOldBinding()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await ClientFixtures.WalkAsync(
            host,
            deviceId,
            ClientFixtures.WalkResult(forwarding: [ClientFixtures.Forwarding(Laptop, ifIndex: 1)]),
            Cancellation);

        await ClientFixtures.WalkAsync(
            host,
            deviceId,
            ClientFixtures.WalkResult(forwarding: [ClientFixtures.Forwarding(Laptop, ifIndex: 7)]),
            Cancellation);

        IReadOnlyList<ClientPortBindingRow> ports = await host.PortBindingsAsync(Cancellation);

        ports.Should().HaveCount(2);
        ports[0].IfIndex.Should().Be(7);
        ports[0].ObservedTo.Should().BeNull();
        ports[1].IfIndex.Should().Be(1);
        ports[1].ObservedTo.Should().Be(ports[0].ObservedFrom, "the intervals must abut exactly");
    }

    [Fact]
    public async Task TwoSwitchesReportingOneEndpoint_BothKeepAnOpenBinding()
    {
        // A MAC is learned by every bridge on the path to it. Enforcing one open port binding per
        // client would have each walk close the other switch's correct observation, and the
        // client would appear to move between switches on every scan.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid access = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host, Cancellation, "access-sw-01", "10.10.0.1");
        Guid distribution = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host, Cancellation, "dist-sw-01", "10.10.0.2");

        await ClientFixtures.WalkAsync(
            host,
            access,
            ClientFixtures.WalkResult(
                forwarding: [ClientFixtures.Forwarding(Laptop, ifIndex: 1, macCountOnPort: 1)]),
            Cancellation);

        await ClientFixtures.WalkAsync(
            host,
            distribution,
            ClientFixtures.WalkResult(
                forwarding: [ClientFixtures.Forwarding(Laptop, ifIndex: 48, macCountOnPort: 214)]),
            Cancellation);

        IReadOnlyList<ClientPortBindingRow> ports = await host.PortBindingsAsync(Cancellation);

        ports.Should().HaveCount(2);
        ports.Should().OnlyContain(port => port.ObservedTo == null);
        ports.Select(port => port.DeviceId).Should().BeEquivalentTo([access, distribution]);
    }

    [Fact]
    public async Task AWalkThatCouldNotBePerformed_ChangesNoBinding()
    {
        // A collector's health is never evidence about the estate. Applying a failed walk as an
        // empty one would report every endpoint behind a switch as having left because the switch
        // stopped answering SNMP.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await ClientFixtures.WalkAsync(
            host,
            deviceId,
            ClientFixtures.WalkResult(neighbors: [ClientFixtures.Neighbor("10.10.0.21", Laptop)]),
            Cancellation);

        await ClientFixtures.FailWalkAsync(host, deviceId, "The device did not answer.", Cancellation);

        IReadOnlyList<ClientIpBindingRow> bindings = await host.IpBindingsAsync(Cancellation);

        bindings.Should().ContainSingle();
        bindings[0].ObservedTo.Should().BeNull();

        ClientScanRow? scan = await host.ClientScanAsync(deviceId, Cancellation);

        scan!.LastError.Should().Contain("did not answer");
    }

    [Fact]
    public async Task ADeviceThatImplementsNeitherTable_IsRecordedAsNotAnsweringRatherThanEmpty()
    {
        // The distinction the interval tables depend on: a router implements no forwarding
        // database and an access switch no neighbour cache, and "nothing to say" is weaker
        // evidence than "nothing there".
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await ClientFixtures.WalkAsync(
            host,
            deviceId,
            ClientFixtures.WalkResult(neighborsSupported: false, forwardingSupported: false),
            Cancellation);

        ClientScanRow? scan = await host.ClientScanAsync(deviceId, Cancellation);

        scan!.NeighborsSupported.Should().BeFalse();
        scan.ForwardingSupported.Should().BeFalse();
        scan.LastNeighborCount.Should().BeNull("a count of an unimplemented table means nothing");
        scan.LastError.Should().BeNull("the walk itself succeeded");
    }

    [Fact]
    public async Task AMulticastAddressInAForwardingDatabase_IsNotRecordedAsAClient()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await ClientFixtures.WalkAsync(
            host,
            deviceId,
            ClientFixtures.WalkResult(
                forwarding:
                [
                    ClientFixtures.Forwarding(Laptop, ifIndex: 1),
                    ClientFixtures.Forwarding("01:00:5E:00:00:FB", ifIndex: 1),
                    ClientFixtures.Forwarding("FF:FF:FF:FF:FF:FF", ifIndex: 1)
                ]),
            Cancellation);

        (await host.ClientsAsync(Cancellation)).Should().ContainSingle()
            .Which.MacAddress.Should().Be(Laptop);
    }

    [Fact]
    public async Task AnIncompleteArpEntry_IsNotRecordedAsAClient()
    {
        // The all-zero hardware address of an entry the device asked about and got no answer for.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await ClientFixtures.WalkAsync(
            host,
            deviceId,
            ClientFixtures.WalkResult(
                neighbors:
                [
                    ClientFixtures.Neighbor("10.10.0.21", Laptop),
                    ClientFixtures.Neighbor("10.10.0.99", "00:00:00:00:00:00")
                ]),
            Cancellation);

        (await host.ClientsAsync(Cancellation)).Should().ContainSingle();
    }

    [Fact]
    public async Task AnUnreadableResultPayload_IsRecordedAsTheCollectorsProblem()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await ClientFixtures.WalkAsync(host, deviceId, """{"walk":"clients","neighbors":42}""", Cancellation);

        ClientScanRow? scan = await host.ClientScanAsync(deviceId, Cancellation);

        scan!.LastError.Should().Contain("could not read");
        (await host.ClientsAsync(Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task ARedeliveredResult_IsAppliedOnce()
    {
        // Outbox delivery is at-least-once. A second application arriving after a competing
        // observation would close and reopen an interval on evidence already spent.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await ClientFixtures.WalkAsync(
            host,
            deviceId,
            ClientFixtures.WalkResult(neighbors: [ClientFixtures.Neighbor("10.10.0.21", Laptop)]),
            Cancellation);

        await host.RedeliverOutboxAsync(Cancellation);

        (await host.IpBindingsAsync(Cancellation)).Should().ContainSingle();
        (await host.ClientsAsync(Cancellation)).Should().ContainSingle();
    }

    [Fact]
    public async Task ANewClient_IsAnnouncedOnceAndNotOncePerSighting()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        string result = ClientFixtures.WalkResult(
            neighbors: [ClientFixtures.Neighbor("10.10.0.21", Laptop)]);

        await ClientFixtures.WalkAsync(host, deviceId, result, Cancellation);
        await ClientFixtures.WalkAsync(host, deviceId, result, Cancellation);

        IReadOnlyList<ClientDiscovered> announced =
            await host.OutboxPayloadsAsync<ClientDiscovered>(Cancellation);

        announced.Should().ContainSingle();
        announced[0].MacAddress.Should().Be(Laptop);
        announced[0].IpAddress.Should().Be("10.10.0.21");
    }

    [Fact]
    public async Task AWalkOfADeviceRemovedWhileItWasInFlight_IsDropped()
    {
        // The device has to go *after* the lease: WP-1.3's claim already fails a job naming a
        // device that has been removed, so the only way to reach the handler's own liveness
        // check is the window between a collector taking the job and reporting on it.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        (await ClientFixtures.RequestWalkAsync(host, deviceId, Cancellation)).Status.Should().Be(202);

        ClientFixtures.LeasedWalk walk = await ClientFixtures.LeaseAsync(host, Cancellation);

        (await host.Client.DeleteAsync($"/api/v1/devices/{deviceId}", Cancellation))
            .Status.Should().Be(204);

        await ClientFixtures.ReportAsync(
            host,
            walk,
            ClientFixtures.WalkResult(neighbors: [ClientFixtures.Neighbor("10.10.0.21", Laptop)]),
            Cancellation);

        (await host.ClientsAsync(Cancellation)).Should().BeEmpty(
            "writing bindings for a device an operator removed would resurrect it in every join");
    }

    [Fact]
    public async Task ASecondWalkOfADeviceThatAlreadyHasOneQueued_IsRefused()
    {
        // Two collectors reading one forwarding database and having their results applied in
        // whichever order they came back would mean two walks each closing what the other opened.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        (await ClientFixtures.RequestWalkAsync(host, deviceId, Cancellation)).Status.Should().Be(202);

        ApiResponse again = await ClientFixtures.RequestWalkAsync(host, deviceId, Cancellation);

        again.Status.Should().Be(409);
        again.Body.Should().Contain("client.walk-outstanding");
    }

    [Fact]
    public async Task AWalkOfADeviceWithNoSnmpCredential_IsRefusedWithSomethingToDoAboutIt()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(
            host, "switch-01", "10.10.0.1", Cancellation);

        ApiResponse queued = await ClientFixtures.RequestWalkAsync(host, deviceId, Cancellation);

        queued.Status.Should().Be(409);
        queued.Body.Should().Contain("discovery.no-snmp-credential");

        (await host.JobIdsForAsync(deviceId, Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task TheSchedule_QueuesAWalkForEveryDeviceWithACredential()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid walkable = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host, Cancellation, "sw-01", "10.10.0.1");
        Guid noCredential = await CollectorFixtures.CreateDeviceAsync(
            host, "sw-02", "10.10.0.2", Cancellation);

        (await host.ScheduleClientsAsync(Cancellation)).Should().Be(1);

        (await host.JobIdsForAsync(walkable, Cancellation)).Should().ContainSingle();
        (await host.JobIdsForAsync(noCredential, Cancellation)).Should().BeEmpty(
            "a device with no credential would produce one failed job per interval for ever");
    }

    [Fact]
    public async Task TheSchedule_PushesADeviceWithNoCredentialForwardRatherThanRetryingItEveryScan()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(
            host, "sw-02", "10.10.0.2", Cancellation);

        await host.ScheduleClientsAsync(Cancellation);

        ClientScanRow? scan = await host.ClientScanAsync(deviceId, Cancellation);

        scan!.NextWalkAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task TheSchedule_SkipsADeviceThatAlreadyHasAWalkOutstanding()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        (await host.ScheduleClientsAsync(Cancellation)).Should().Be(1);

        await host.MakeClientScanDueAsync(deviceId, Cancellation);

        (await host.ScheduleClientsAsync(Cancellation)).Should().Be(0,
            "a collector outage must not build a backlog nobody will ever run");
    }
}
