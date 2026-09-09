using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using FluentAssertions;

using NetShield.Contracts.Inventory;
using NetShield.Contracts.Paging;

using NetShield.IntegrationTests.Collector;
using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// The VLAN inventory over the whole round trip: queue a walk, answer it as the collector would,
/// and read back what the estate concluded.
/// </summary>
/// <remarks>
/// Both WP-2.2 "Done when" criteria are here — the tile counts on a fixture estate, and a VLAN on
/// several switches appearing once — beside the rules that decide when an absence is allowed to
/// mean something. Driven through the real contract against real PostgreSQL, because what has to
/// hold is that the API copes with what a separate process in another language actually sends.
/// </remarks>
public sealed class VlanInventoryTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Devices = "/api/v1/devices";
    private const string Vlans = "/api/v1/vlans";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>The outbox records an event by its full type name.</summary>
    private static readonly string VlansChanged =
        typeof(Contracts.Inventory.Events.DeviceVlansChanged).FullName!;

    // --- what one switch says -----------------------------------------------------------------

    [Fact]
    public async Task AVlanWalk_RecordsWhatTheDeviceIsConfiguredWith()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.VlanWalkAsync(
            host,
            deviceId,
            TopologyFixtures.VlanResult(
            [
                TopologyFixtures.Vlan(10, "Server VLAN", [1, 2], [1]),
                TopologyFixtures.Vlan(20, "Users VLAN", [1, 2, 3], [3])
            ]),
            Cancellation);

        CursorPage<DeviceVlanSummary> page = Read<CursorPage<DeviceVlanSummary>>(
            await host.Client.GetAsync($"{Devices}/{deviceId}/vlans", Cancellation));

        page.Items.Should().HaveCount(2);

        DeviceVlanSummary server = page.Items[0];

        server.VlanId.Should().Be(10);
        server.Name.Should().Be("Server VLAN");
        server.Ports.Select(port => port.IfIndex).Should().Equal(1, 2);
        server.Ports.Single(port => port.IfIndex == 1).Untagged.Should().BeTrue();
        server.Ports.Single(port => port.IfIndex == 2).Untagged.Should().BeFalse();
    }

    [Fact]
    public async Task AVlansMemberPorts_AreNamedFromTheInterfaceInventory()
    {
        // The name is joined rather than stored, because the interface inventory is WP-1.5's and
        // a copy would go stale the first time somebody renamed a port.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.WalkAsync(
            host,
            deviceId,
            DiscoveryFixtures.WalkResult(interfaces: [1, 2]),
            Cancellation);

        await TopologyFixtures.VlanWalkAsync(
            host,
            deviceId,
            TopologyFixtures.VlanResult([TopologyFixtures.Vlan(10, "Server VLAN", [1, 2], [1])]),
            Cancellation);

        CursorPage<DeviceVlanSummary> page = Read<CursorPage<DeviceVlanSummary>>(
            await host.Client.GetAsync($"{Devices}/{deviceId}/vlans", Cancellation));

        page.Items[0].Ports.Select(port => port.InterfaceName).Should().Equal("Gi0/1", "Gi0/2");
    }

    [Fact]
    public async Task APortTheDeviceCouldNotPlace_IsCountedRatherThanHidden()
    {
        // A switch whose bridge-port table is unreadable is not a switch whose VLANs are empty,
        // and the two must not look the same on a screen.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.VlanWalkAsync(
            host,
            deviceId,
            TopologyFixtures.VlanResult(
                [TopologyFixtures.Vlan(10, "Server VLAN", [1], portCount: 3, unresolvedPortCount: 2)]),
            Cancellation);

        CursorPage<DeviceVlanSummary> page = Read<CursorPage<DeviceVlanSummary>>(
            await host.Client.GetAsync($"{Devices}/{deviceId}/vlans", Cancellation));

        page.Items[0].PortCount.Should().Be(3);
        page.Items[0].UnresolvedPortCount.Should().Be(2);
        page.Items[0].Ports.Should().ContainSingle();
    }

    // --- "a VLAN present on multiple switches appears once with aggregated membership" ---------

    [Fact]
    public async Task AVlanOnSeveralSwitches_AppearsOnceWithAggregatedMembership()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host, Cancellation, "core-sw-1", "10.10.0.1");

        Guid access = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host, Cancellation, "acc-sw-1", "10.10.0.2");

        Guid second = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host, Cancellation, "acc-sw-2", "10.10.0.3");

        await TopologyFixtures.VlanWalkAsync(
            host,
            core,
            TopologyFixtures.VlanResult([TopologyFixtures.Vlan(20, "Users VLAN", [1, 2], [])]),
            Cancellation);

        await TopologyFixtures.VlanWalkAsync(
            host,
            access,
            TopologyFixtures.VlanResult([TopologyFixtures.Vlan(20, "Users VLAN", [1, 2, 3], [2, 3])]),
            Cancellation);

        await TopologyFixtures.VlanWalkAsync(
            host,
            second,
            TopologyFixtures.VlanResult([TopologyFixtures.Vlan(20, "Users VLAN", [1, 2], [2])]),
            Cancellation);

        CursorPage<VlanSummary> page = Read<CursorPage<VlanSummary>>(
            await host.Client.GetAsync(Vlans, Cancellation));

        page.Items.Should().ContainSingle("three switches carrying VLAN 20 are carrying one VLAN");

        VlanSummary users = page.Items[0];

        users.VlanId.Should().Be(20);
        users.Name.Should().Be("Users VLAN");
        users.DeviceCount.Should().Be(3);
        users.PortCount.Should().Be(7, "membership is summed — a port belongs to one device");
        users.NameDisputed.Should().BeFalse();
    }

    [Fact]
    public async Task AVlansDetail_NamesEverySwitchCarryingItAndTheirPorts()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host, Cancellation, "core-sw-1", "10.10.0.1");

        Guid access = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host, Cancellation, "acc-sw-1", "10.10.0.2");

        await TopologyFixtures.VlanWalkAsync(
            host,
            core,
            TopologyFixtures.VlanResult([TopologyFixtures.Vlan(30, "Voice VLAN", [1, 2], [])]),
            Cancellation);

        await TopologyFixtures.VlanWalkAsync(
            host,
            access,
            TopologyFixtures.VlanResult([TopologyFixtures.Vlan(30, "Voice VLAN", [5], [5])]),
            Cancellation);

        VlanDetail detail = Read<VlanDetail>(await host.Client.GetAsync($"{Vlans}/30", Cancellation));

        detail.VlanId.Should().Be(30);
        detail.DeviceCount.Should().Be(2);
        detail.PortCount.Should().Be(3);
        detail.Devices.Select(device => device.Hostname).Should().Equal("acc-sw-1", "core-sw-1");
        detail.Devices.Single(device => device.DeviceId == access).Ports.Should().ContainSingle();
        detail.Devices.Single(device => device.DeviceId == access).Ports[0].Untagged.Should().BeTrue();
        detail.Devices.Single(device => device.DeviceId == core).Ports.Should().HaveCount(2);
    }

    [Fact]
    public async Task SwitchesDisagreeingAboutAVlansName_SurfaceTheDisagreement()
    {
        // The VLAN is still one VLAN. Splitting it would change the identity model; hiding the
        // disagreement would lose the only clue that somebody has mis-typed a name somewhere.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host, Cancellation, "core-sw-1", "10.10.0.1");

        Guid access = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host, Cancellation, "acc-sw-1", "10.10.0.2");

        await TopologyFixtures.VlanWalkAsync(
            host,
            core,
            TopologyFixtures.VlanResult([TopologyFixtures.Vlan(40, "Guest VLAN", [1], [1])]),
            Cancellation);

        await TopologyFixtures.VlanWalkAsync(
            host,
            access,
            TopologyFixtures.VlanResult([TopologyFixtures.Vlan(40, "Visitors", [1], [1])]),
            Cancellation);

        VlanDetail detail = Read<VlanDetail>(await host.Client.GetAsync($"{Vlans}/40", Cancellation));

        detail.DeviceCount.Should().Be(2);
        detail.NameDisputed.Should().BeTrue();
        detail.Names.Should().BeEquivalentTo(["Guest VLAN", "Visitors"]);
        detail.Devices.Single(device => device.DeviceId == core).Name.Should().Be("Guest VLAN");
        detail.Devices.Single(device => device.DeviceId == access).Name.Should().Be("Visitors");
    }

    // --- "VLAN counts on a fixture estate match the reference dashboard's VLAN tiles" ----------

    /// <summary>
    /// The estate behind <c>docs/design/reference-dashboard.png</c>'s VLAN tiles, built and then
    /// counted.
    /// </summary>
    /// <remarks>
    /// The tiles under the topology card read <em>Server VLAN · 12 Devices</em>, <em>Users VLAN ·
    /// 245 Clients</em>, <em>Voice VLAN · 32 Clients</em>, <em>Guest VLAN · 83 Clients</em> and
    /// <em>IoT VLAN · 45 Devices</em>. Those exact numbers are the criterion, so they are built
    /// here rather than approximated: twelve switches carrying VLAN 10, forty-five carrying VLAN
    /// 50, and a forwarding database holding 245, 32 and 83 endpoints on the three client VLANs.
    /// </remarks>
    [Fact]
    public async Task TheFixtureEstate_MatchesTheReferenceDashboardsVlanTiles()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid profileId = await CollectorFixtures.CreateCredentialProfileAsync(
            host,
            "Estate SNMP",
            "fixture-community",
            Cancellation);

        List<Guid> switches = [];

        for (int index = 0; index < 45; index++)
        {
            Guid deviceId = await CollectorFixtures.CreateDeviceAsync(
                host,
                $"sw-{index:D2}",
                $"10.20.{index / 254}.{(index % 254) + 1}",
                Cancellation);

            await DiscoveryFixtures.AssignAsync(host, deviceId, [profileId], Cancellation);

            switches.Add(deviceId);
        }

        // Every switch carries the IoT VLAN; the first twelve also carry the server VLAN, and the
        // first also carries the three the clients are on.
        foreach ((Guid deviceId, int index) in switches.Select((id, index) => (id, index)))
        {
            List<string> vlans = [TopologyFixtures.Vlan(50, "IoT VLAN", [1], [1])];

            if (index < 12)
            {
                vlans.Add(TopologyFixtures.Vlan(10, "Server VLAN", [2], [2]));
            }

            if (index == 0)
            {
                vlans.Add(TopologyFixtures.Vlan(20, "Users VLAN", [3], [3]));
                vlans.Add(TopologyFixtures.Vlan(30, "Voice VLAN", [4], [4]));
                vlans.Add(TopologyFixtures.Vlan(40, "Guest VLAN", [5], [5]));
            }

            (await TopologyFixtures.RequestVlanWalkAsync(host, deviceId, Cancellation))
                .Status.Should().Be(202);

            await TopologyFixtures.ReportAsync(
                host,
                await TopologyFixtures.LeaseAsync(host, Cancellation),
                TopologyFixtures.VlanResult(vlans),
                Cancellation);
        }

        // The clients, all learned by the first switch, on the three VLANs the tiles count
        // endpoints on.
        await ClientFixtures.WalkAsync(
            host,
            switches[0],
            ClientFixtures.WalkResult(forwarding:
            [
                .. Endpoints(20, 245, ifIndex: 3),
                .. Endpoints(30, 32, ifIndex: 4),
                .. Endpoints(40, 83, ifIndex: 5)
            ]),
            Cancellation);

        CursorPage<VlanSummary> page = Read<CursorPage<VlanSummary>>(
            await host.Client.GetAsync($"{Vlans}?limit=200", Cancellation));

        Dictionary<int, VlanSummary> tiles = page.Items.ToDictionary(vlan => vlan.VlanId);

        tiles[10].Name.Should().Be("Server VLAN");
        tiles[10].DeviceCount.Should().Be(12);

        tiles[20].Name.Should().Be("Users VLAN");
        tiles[20].ClientCount.Should().Be(245);

        tiles[30].Name.Should().Be("Voice VLAN");
        tiles[30].ClientCount.Should().Be(32);

        tiles[40].Name.Should().Be("Guest VLAN");
        tiles[40].ClientCount.Should().Be(83);

        tiles[50].Name.Should().Be("IoT VLAN");
        tiles[50].DeviceCount.Should().Be(45);
    }

    // --- when an absence means something, and when it does not --------------------------------

    [Fact]
    public async Task AVlanACompleteReadingNoLongerContains_LeavesTheInventory()
    {
        // A static VLAN table is the device's whole current statement about what it is configured
        // with, unlike WP-1.8's ARP cache. A VLAN it no longer lists has been removed from it.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.VlanWalkAsync(
            host,
            deviceId,
            TopologyFixtures.VlanResult(
            [
                TopologyFixtures.Vlan(10, "Server VLAN", [1], [1]),
                TopologyFixtures.Vlan(20, "Users VLAN", [2], [2])
            ]),
            Cancellation);

        await TopologyFixtures.VlanWalkAsync(
            host,
            deviceId,
            TopologyFixtures.VlanResult([TopologyFixtures.Vlan(10, "Server VLAN", [1], [1])]),
            Cancellation);

        CursorPage<DeviceVlanSummary> page = Read<CursorPage<DeviceVlanSummary>>(
            await host.Client.GetAsync($"{Devices}/{deviceId}/vlans", Cancellation));

        page.Items.Select(vlan => vlan.VlanId).Should().Equal(10);

        (await host.Client.GetAsync($"{Vlans}/20", Cancellation)).Status.Should().Be(404);
    }

    [Fact]
    public async Task ADeviceThatAnswersNoVlanTable_WithdrawsNothing()
    {
        // A router implements neither Q-BRIDGE table. Reading that as "this device has no VLANs"
        // would empty the inventory of every device that is not a bridge.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.VlanWalkAsync(
            host,
            deviceId,
            TopologyFixtures.VlanResult([TopologyFixtures.Vlan(10, "Server VLAN", [1], [1])]),
            Cancellation);

        await TopologyFixtures.VlanWalkAsync(
            host,
            deviceId,
            TopologyFixtures.VlanResult(vlansSupported: false, vlanTable: null),
            Cancellation);

        CursorPage<DeviceVlanSummary> page = Read<CursorPage<DeviceVlanSummary>>(
            await host.Client.GetAsync($"{Devices}/{deviceId}/vlans", Cancellation));

        page.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task AReadingCutShortByItsCeiling_WithdrawsNothing()
    {
        // A truncated reading saw part of a table, so an absence from it is not an absence.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.VlanWalkAsync(
            host,
            deviceId,
            TopologyFixtures.VlanResult(
            [
                TopologyFixtures.Vlan(10, "Server VLAN", [1], [1]),
                TopologyFixtures.Vlan(20, "Users VLAN", [2], [2])
            ]),
            Cancellation);

        await TopologyFixtures.VlanWalkAsync(
            host,
            deviceId,
            TopologyFixtures.VlanResult(
                [TopologyFixtures.Vlan(10, "Server VLAN", [1], [1])],
                truncated: true,
                vlanCount: 2),
            Cancellation);

        CursorPage<DeviceVlanSummary> page = Read<CursorPage<DeviceVlanSummary>>(
            await host.Client.GetAsync($"{Devices}/{deviceId}/vlans", Cancellation));

        page.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task AFailedWalk_ChangesNoVlanAndSaysWhyOnTheScanRow()
    {
        // A collector's health is never evidence about the estate.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.VlanWalkAsync(
            host,
            deviceId,
            TopologyFixtures.VlanResult([TopologyFixtures.Vlan(10, "Server VLAN", [1], [1])]),
            Cancellation);

        await TopologyFixtures.FailVlanWalkAsync(
            host,
            deviceId,
            "10.10.0.1 did not answer the Q-BRIDGE tables.",
            Cancellation);

        CursorPage<DeviceVlanSummary> page = Read<CursorPage<DeviceVlanSummary>>(
            await host.Client.GetAsync($"{Devices}/{deviceId}/vlans", Cancellation));

        page.Items.Should().ContainSingle();

        DeviceTopologyScanDetail scan = Read<DeviceTopologyScanDetail>(
            await host.Client.GetAsync($"{Devices}/{deviceId}/topology-scan", Cancellation));

        scan.LastVlanError.Should().Contain("did not answer");
        scan.LastVlanWalkAt.Should().NotBeNull();
    }

    // --- the scan row and the schedule --------------------------------------------------------

    [Fact]
    public async Task TheScanRow_ReportsTheVlanHalfBesideTheOtherTwo()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.VlanWalkAsync(
            host,
            deviceId,
            TopologyFixtures.VlanResult([TopologyFixtures.Vlan(10, "Server VLAN", [1], [1])]),
            Cancellation);

        DeviceTopologyScanDetail scan = Read<DeviceTopologyScanDetail>(
            await host.Client.GetAsync($"{Devices}/{deviceId}/topology-scan", Cancellation));

        scan.VlansSupported.Should().BeTrue();
        scan.VlanTable.Should().Be("dot1qVlanStatic");
        scan.LastVlanCount.Should().Be(1);
        scan.LastVlanError.Should().BeNull();
        scan.NextVlanWalkAt.Should().BeAfter(DateTimeOffset.UtcNow);

        // The other two halves are untouched by a VLAN walk, which is the point of three walks.
        scan.LldpSupported.Should().BeNull();
        scan.RoutingSupported.Should().BeNull();
    }

    [Fact]
    public async Task ADeviceWithNoVlanTable_IsRecordedAsUnsupportedRatherThanEmpty()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.VlanWalkAsync(
            host,
            deviceId,
            TopologyFixtures.VlanResult(vlansSupported: false, vlanTable: null),
            Cancellation);

        DeviceTopologyScanDetail scan = Read<DeviceTopologyScanDetail>(
            await host.Client.GetAsync($"{Devices}/{deviceId}/topology-scan", Cancellation));

        scan.VlansSupported.Should().BeFalse();
        scan.VlanTable.Should().BeNull();
        scan.LastVlanCount.Should().BeNull();
    }

    [Fact]
    public async Task ARedeliveredWalk_IsANoOp()
    {
        // Outbox delivery is at-least-once. A second application of a reading whose evidence has
        // already been spent would withdraw VLANs a later walk had established.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        TopologyFixtures.LeasedWalk walk = await Queue(host, deviceId);

        await TopologyFixtures.ReportAsync(
            host,
            walk,
            TopologyFixtures.VlanResult([TopologyFixtures.Vlan(10, "Server VLAN", [1], [1])]),
            Cancellation);

        await host.DispatchOutboxAsync(Cancellation);
        await host.DispatchOutboxAsync(Cancellation);

        CursorPage<DeviceVlanSummary> page = Read<CursorPage<DeviceVlanSummary>>(
            await host.Client.GetAsync($"{Devices}/{deviceId}/vlans", Cancellation));

        page.Items.Should().ContainSingle();
        page.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task AWalkForADeviceRemovedWhileItWasInFlight_IsDropped()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        TopologyFixtures.LeasedWalk walk = await Queue(host, deviceId);

        (await host.Client.DeleteAsync($"{Devices}/{deviceId}", Cancellation)).Status.Should().Be(204);

        await TopologyFixtures.ReportAsync(
            host,
            walk,
            TopologyFixtures.VlanResult([TopologyFixtures.Vlan(10, "Server VLAN", [1], [1])]),
            Cancellation);

        // The estate-wide read joins live devices, so a removed one contributes nothing.
        CursorPage<VlanSummary> page = Read<CursorPage<VlanSummary>>(
            await host.Client.GetAsync(Vlans, Cancellation));

        page.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ASecondWalkOfOneDevice_IsRefusedWhileTheFirstIsOutstanding()
    {
        // Two readings applied in whichever order they came back would have each withdrawing what
        // the other had just established.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        (await TopologyFixtures.RequestVlanWalkAsync(host, deviceId, Cancellation))
            .Status.Should().Be(202);

        ApiResponse refused =
            await TopologyFixtures.RequestVlanWalkAsync(host, deviceId, Cancellation);

        refused.Status.Should().Be(409);
    }

    [Fact]
    public async Task AQueuedVlanWalk_SaysWhichWalkItIs()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        NeighborWalkQueued queued = Read<NeighborWalkQueued>(
            await TopologyFixtures.RequestVlanWalkAsync(host, deviceId, Cancellation));

        queued.Walk.Should().Be(TopologyWalkKind.Vlans);
        queued.DeviceId.Should().Be(deviceId);
    }

    [Fact]
    public async Task TheScheduleQueuesAVlanWalkOnceTheOtherTwoAreSatisfied()
    {
        // One job per device at a time, so a device due for everything takes three passes. The
        // neighbour walk goes first, then the route walk, then this.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        List<string> walks = [];

        for (int pass = 0; pass < 3; pass++)
        {
            (await host.ScheduleTopologyAsync(Cancellation)).Should().Be(1);

            TopologyFixtures.LeasedWalk leased =
                await TopologyFixtures.LeaseAsync(host, Cancellation);

            walks.Add(await WalkNameAsync(host, leased.JobId));

            await TopologyFixtures.ReportAsync(
                host,
                leased,
                TopologyFixtures.VlanResult(vlansSupported: false, vlanTable: null),
                Cancellation,
                outcome: "Succeeded");
        }

        walks.Should().Equal("neighbors", "routes", "vlans");
    }

    // --- the event ----------------------------------------------------------------------------

    [Fact]
    public async Task AChangedVlanSet_PublishesDeviceVlansChanged()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.VlanWalkAsync(
            host,
            deviceId,
            TopologyFixtures.VlanResult([TopologyFixtures.Vlan(10, "Server VLAN", [1], [1])]),
            Cancellation);

        (await host.OutboxEventNamesAsync(Cancellation)).Should().Contain(VlansChanged);
    }

    [Fact]
    public async Task AWalkThatConfirmsWhatWasAlreadyKnown_PublishesNothing()
    {
        // A subscriber rebuilding a VLAN filter should not be woken every hour by the schedule
        // agreeing with itself.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        string reading = TopologyFixtures.VlanResult(
            [TopologyFixtures.Vlan(10, "Server VLAN", [1], [1])]);

        await TopologyFixtures.VlanWalkAsync(host, deviceId, reading, Cancellation);

        int before = (await host.OutboxEventNamesAsync(Cancellation))
            .Count(name => name == VlansChanged);

        await TopologyFixtures.VlanWalkAsync(host, deviceId, reading, Cancellation);

        (await host.OutboxEventNamesAsync(Cancellation))
            .Count(name => name == VlansChanged)
            .Should().Be(before);
    }

    // --- helpers ------------------------------------------------------------------------------

    private static async Task<TopologyFixtures.LeasedWalk> Queue(InventoryHost host, Guid deviceId)
    {
        (await TopologyFixtures.RequestVlanWalkAsync(host, deviceId, Cancellation))
            .Status.Should().Be(202);

        return await TopologyFixtures.LeaseAsync(host, Cancellation);
    }

    private static async Task<string> WalkNameAsync(InventoryHost host, Guid jobId)
    {
        CollectorJobParametersRow job =
            (await host.CollectorJobsAsync(Cancellation)).Single(row => row.Id == jobId);

        using JsonDocument parameters = JsonDocument.Parse(job.Parameters);

        return parameters.RootElement.GetProperty("walk").GetString()!;
    }

    /// <summary>Distinct endpoints on one VLAN, as a forwarding database reports them.</summary>
    private static IEnumerable<string> Endpoints(int vlanId, int count, int ifIndex) =>
        Enumerable.Range(0, count).Select(index => ClientFixtures.Forwarding(
            Mac(vlanId, index),
            ifIndex,
            vlanId,
            macCountOnPort: count));

    private static string Mac(int vlanId, int index) => string.Create(
        CultureInfo.InvariantCulture,
        $"AA:BB:{vlanId / 256:X2}:{vlanId % 256:X2}:{index / 256:X2}:{index % 256:X2}");

    private static T Read<T>(ApiResponse response) =>
        JsonSerializer.Deserialize<T>(response.Body, JsonOptions)!;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}
