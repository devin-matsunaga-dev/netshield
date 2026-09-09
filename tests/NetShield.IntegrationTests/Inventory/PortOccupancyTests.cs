using System.Text.Json;
using System.Text.Json.Serialization;

using FluentAssertions;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Paging;

using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// What is connected to each switch port, over the whole round trip.
/// </summary>
/// <remarks>
/// <para>
/// Every fact this route serves is written by a different package's walk, so every test here
/// drives the real thing: the fingerprint walk writes the interfaces, the neighbour walk writes
/// the adjacencies, the client walk writes the port bindings, and each goes through the queue,
/// the collector contract and the outbox before anything is read back over HTTP. A port view
/// that agreed with hand-inserted rows would prove nothing about whether the three walks
/// actually key on the same <c>(device, ifIndex)</c>.
/// </para>
/// <para>
/// The four behavioural criteria WP-2.6 is measured against are the first four tests.
/// </para>
/// </remarks>
public sealed class PortOccupancyTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Devices = "/api/v1/devices";

    private const string CoreChassis = "00:1C:73:00:00:01";
    private const string ApChassis = "00:0B:86:AA:BB:CC";
    private const string PhoneChassis = "00:1A:A1:11:22:33";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // --- the four criteria -----------------------------------------------------------------------

    [Fact]
    public async Task APortWithOneHost_ShowsThatHost()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await WalkedDeviceAsync(host);

        await ClientFixtures.WalkAsync(
            host,
            deviceId,
            ClientFixtures.WalkResult(
                neighbors: [ClientFixtures.Neighbor("10.20.0.50", "AA:BB:CC:00:00:01", 1)],
                forwarding: [ClientFixtures.Forwarding("AA:BB:CC:00:00:01", 2, vlanId: 30, macCountOnPort: 1)]),
            Cancellation);

        DevicePortSummary port = await PortAsync(host, deviceId, 2);

        port.Role.Should().Be(PortRole.Access);
        port.RoleReason.Should().Be(PortRoleReason.Endpoints);
        port.ClientsListed.Should().BeTrue();
        port.LearnedAddressCount.Should().Be(1);

        PortClient client = port.Clients.Should().ContainSingle().Subject;

        client.MacAddress.Should().Be("AA:BB:CC:00:00:01");
        client.IpAddress.Should().Be("10.20.0.50");
        client.VlanId.Should().Be(30);
        client.Oui.Should().Be("AA:BB:CC");
    }

    [Fact]
    public async Task APortFacingAnotherSwitch_ShowsTheTopologyEdge()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await WalkedDeviceAsync(host);

        // A far end NetShield monitors, so the edge resolves to a device rather than to a
        // chassis identifier nobody can open.
        Guid coreId = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host,
            Cancellation,
            hostname: "core-sw-1",
            address: "10.10.0.9");

        await DiscoveryFixtures.WalkAsync(host, coreId, DiscoveryFixtures.WalkResult(), Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            deviceId,
            TopologyFixtures.NeighborResult(
                lldp:
                [
                    TopologyFixtures.Lldp(
                        1,
                        CoreChassis,
                        "Gi0/1",
                        systemName: "core-sw-1",
                        managementAddress: "10.10.0.9",
                        localPortName: "Gi0/1")
                ]),
            Cancellation);

        DevicePortSummary port = await PortAsync(host, deviceId, 1);

        PortNeighbor neighbor = port.Neighbors.Should().ContainSingle().Subject;

        neighbor.Managed.Should().BeTrue();
        neighbor.DeviceId.Should().Be(coreId);
        neighbor.Hostname.Should().Be("core-sw-1");
        neighbor.ChassisId.Should().Be(CoreChassis);
        neighbor.Sources.Should().Equal(NeighborSource.Lldp);
        neighbor.Confidence.Should().Be(AdjacencyConfidence.Probable);
        neighbor.AdjacencyId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task APortWithAnLldpSpeakingAccessPoint_ShowsItsNameAndItsCapabilityInWords()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await WalkedDeviceAsync(host);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            deviceId,
            TopologyFixtures.NeighborResult(
                lldp:
                [
                    TopologyFixtures.Lldp(
                        2,
                        ApChassis,
                        "eth0",
                        systemName: "ap-floor-1",
                        systemDescription: "ArubaOS (MODEL: 535), Version 8.10.0.6",
                        capabilities: TopologyFixtures.Capabilities.AccessPoint)
                ]),
            Cancellation);

        DevicePortSummary port = await PortAsync(host, deviceId, 2);

        PortNeighbor ap = port.Neighbors.Should().ContainSingle().Subject;

        ap.SystemName.Should().Be("ap-floor-1");
        ap.SystemDescription.Should().Be("ArubaOS (MODEL: 535), Version 8.10.0.6");
        ap.Managed.Should().BeFalse();
        ap.Capabilities.Should().Equal(SystemCapability.Bridge, SystemCapability.WlanAccessPoint);

        // And the port is still an access port. An access point advertises Bridge because
        // bridging is what it does, and reading that bit alone would stop the screen showing the
        // access point on its own port.
        port.Role.Should().Be(PortRole.Access);
    }

    [Fact]
    public async Task AnUplink_SaysHowManyAddressesItCarries_RatherThanListingThemAsOccupants()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await WalkedDeviceAsync(host);

        // Twelve hosts, every one of them learned on port 1 because that is the port everything
        // beyond this switch is reachable through. All twelve bindings are true and all twelve
        // are recorded; none of them is plugged into this cable.
        IReadOnlyList<string> forwarding =
        [
            .. Enumerable.Range(1, 12).Select(index =>
                ClientFixtures.Forwarding($"AA:BB:CC:00:01:{index:X2}", 1, macCountOnPort: 200))
        ];

        await ClientFixtures.WalkAsync(
            host,
            deviceId,
            ClientFixtures.WalkResult(forwarding: forwarding),
            Cancellation);

        DevicePortSummary port = await PortAsync(host, deviceId, 1);

        port.Role.Should().Be(PortRole.Uplink);
        port.RoleReason.Should().Be(PortRoleReason.LearnedAddressCount);
        port.LearnedAddressCount.Should().Be(200);
        port.ClientCount.Should().Be(12);

        port.ClientsListed.Should().BeFalse();
        port.Clients.Should().BeEmpty();
    }

    // --- the classification, through the real tables ------------------------------------------------

    [Fact]
    public async Task AManagedFarEnd_MakesAPortAnUplinkWhateverTheAddressCount()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await WalkedDeviceAsync(host);

        Guid coreId = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host,
            Cancellation,
            hostname: "core-sw-1",
            address: "10.10.0.9");

        await DiscoveryFixtures.WalkAsync(host, coreId, DiscoveryFixtures.WalkResult(), Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            deviceId,
            TopologyFixtures.NeighborResult(
                lldp: [TopologyFixtures.Lldp(1, CoreChassis, "Gi0/1", managementAddress: "10.10.0.9")]),
            Cancellation);

        await ClientFixtures.WalkAsync(
            host,
            deviceId,
            ClientFixtures.WalkResult(
                forwarding: [ClientFixtures.Forwarding("AA:BB:CC:00:00:07", 1, macCountOnPort: 1)]),
            Cancellation);

        DevicePortSummary port = await PortAsync(host, deviceId, 1);

        // One address on the port would read as an access port on the count alone. The topology
        // is the stronger statement and the reason has to say so, or an operator would go looking
        // at a threshold that had nothing to do with it.
        port.Role.Should().Be(PortRole.Uplink);
        port.RoleReason.Should().Be(PortRoleReason.ManagedDevice);
        port.Clients.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnmanagedSwitchOnAPort_MakesItAnUplinkOnItsOwnCapabilityStatement()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await WalkedDeviceAsync(host);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            deviceId,
            TopologyFixtures.NeighborResult(
                lldp:
                [
                    TopologyFixtures.Lldp(
                        2,
                        "00:1C:73:99:99:99",
                        "Gi1/1",
                        systemName: "desk-switch",
                        capabilities: TopologyFixtures.Capabilities.Switch)
                ]),
            Cancellation);

        DevicePortSummary port = await PortAsync(host, deviceId, 2);

        port.Role.Should().Be(PortRole.Uplink);
        port.RoleReason.Should().Be(PortRoleReason.InfrastructureNeighbor);
        port.Neighbors[0].Managed.Should().BeFalse();
        port.Neighbors[0].Capabilities.Should().Equal(SystemCapability.Bridge, SystemCapability.Router);
    }

    [Fact]
    public async Task APhoneOnAPort_IsAnAccessPortAndSaysItIsATelephone()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await WalkedDeviceAsync(host);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            deviceId,
            TopologyFixtures.NeighborResult(
                lldp:
                [
                    TopologyFixtures.Lldp(
                        2,
                        PhoneChassis,
                        "P1",
                        systemName: "SEP001AA1112233",
                        capabilities: TopologyFixtures.Capabilities.Phone)
                ]),
            Cancellation);

        DevicePortSummary port = await PortAsync(host, deviceId, 2);

        port.Role.Should().Be(PortRole.Access);
        port.Neighbors[0].Capabilities.Should().Equal(
            SystemCapability.Bridge,
            SystemCapability.Telephone);
    }

    [Fact]
    public async Task ACdpCapabilityWord_IsReadWithItsOwnTable()
    {
        // The trap this package exists partly to avoid: CDP bit 3 is a switch and LLDP bit 3 is a
        // wireless access point. One table applied to both columns would report every Cisco
        // switch as an access point, which is a wrong answer rather than a missing one.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await WalkedDeviceAsync(host);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            deviceId,
            TopologyFixtures.NeighborResult(
                cdp:
                [
                    TopologyFixtures.Cdp(
                        2,
                        "core-sw-1",
                        "GigabitEthernet1/1",
                        capabilities: TopologyFixtures.Capabilities.CdpSwitch)
                ]),
            Cancellation);

        DevicePortSummary port = await PortAsync(host, deviceId, 2);

        port.Neighbors[0].Capabilities.Should().Equal(
            SystemCapability.Bridge,
            SystemCapability.Router);
        port.Role.Should().Be(PortRole.Uplink);
    }

    // --- the port set ----------------------------------------------------------------------------

    [Fact]
    public async Task APortNoWalkHasFingerprinted_IsStillListed()
    {
        // A switch can forward on a port whose interface row the fingerprint walk has not
        // recorded yet, which is why the client binding's ifIndex is deliberately not a foreign
        // key. Paging the interface table alone would hide the port and whatever is on it.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await ClientFixtures.WalkAsync(
            host,
            deviceId,
            ClientFixtures.WalkResult(
                forwarding: [ClientFixtures.Forwarding("AA:BB:CC:00:00:09", 48, macCountOnPort: 1)]),
            Cancellation);

        DevicePortSummary port = await PortAsync(host, deviceId, 48);

        port.InterfaceKnown.Should().BeFalse();
        port.Name.Should().BeNull();
        port.AdminStatus.Should().Be(InterfaceStatus.Unknown);
        port.OperStatus.Should().Be(InterfaceStatus.Unknown);
        port.Clients.Should().ContainSingle();
    }

    [Fact]
    public async Task AFingerprintedPortWithNothingOnIt_IsListedAsEmpty()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await WalkedDeviceAsync(host);

        DevicePortSummary port = await PortAsync(host, deviceId, 1);

        port.InterfaceKnown.Should().BeTrue();
        port.Name.Should().Be("Gi0/1");
        port.AdminStatus.Should().Be(InterfaceStatus.Up);
        port.Role.Should().Be(PortRole.Empty);
        port.RoleReason.Should().Be(PortRoleReason.NoEvidence);
        port.LearnedAddressCount.Should().BeNull();
        port.Neighbors.Should().BeEmpty();
        port.Clients.Should().BeEmpty();
    }

    [Fact]
    public async Task AWithdrawnEdge_IsNotAnOccupant()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await WalkedDeviceAsync(host);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            deviceId,
            TopologyFixtures.NeighborResult(lldp: [TopologyFixtures.Lldp(1, CoreChassis, "Gi0/1")]),
            Cancellation);

        (await PortAsync(host, deviceId, 1)).Neighbors.Should().ContainSingle();

        // A complete, untruncated reading that no longer contains the entry: the cable has gone.
        await TopologyFixtures.NeighborWalkAsync(
            host,
            deviceId,
            TopologyFixtures.NeighborResult(lldp: [], lldpSupported: true),
            Cancellation);

        DevicePortSummary port = await PortAsync(host, deviceId, 1);

        port.Neighbors.Should().BeEmpty();
        port.Role.Should().Be(PortRole.Empty);
    }

    [Fact]
    public async Task ADeviceAtTheFarEndOfItsOwnEdge_FindsThePortOnItsOwnSide()
    {
        // WP-2.1 orders an edge's endpoints canonically rather than by who observed it, so a
        // device is the `A` end of some of its edges and the `B` end of others. Reading only the
        // `A` end would lose half a switch's uplinks.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid first = await WalkedDeviceAsync(host);
        Guid second = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host,
            Cancellation,
            hostname: "core-sw-1",
            address: "10.10.0.9");

        await DiscoveryFixtures.WalkAsync(host, second, DiscoveryFixtures.WalkResult(), Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            first,
            TopologyFixtures.NeighborResult(
                lldp: [TopologyFixtures.Lldp(1, CoreChassis, "Gi0/1", managementAddress: "10.10.0.9")]),
            Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            second,
            TopologyFixtures.NeighborResult(
                localChassisId: CoreChassis,
                lldp: [TopologyFixtures.Lldp(1, "00:1A:2B:3C:4D:01", "Gi0/1", managementAddress: "10.10.0.1")]),
            Cancellation);

        // Whichever way the two devices ordered themselves, each finds the edge on its own port 1.
        (await PortAsync(host, first, 1)).Neighbors.Should().ContainSingle();
        (await PortAsync(host, second, 1)).Neighbors.Should().ContainSingle();
    }

    // --- the route ---------------------------------------------------------------------------------

    [Fact]
    public async Task Ports_AreOrderedByIfIndexAndPaginated()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.WalkAsync(
            host,
            deviceId,
            DiscoveryFixtures.WalkResult(interfaces: [1, 2, 3, 4, 5]),
            Cancellation);

        CursorPage<DevicePortSummary> first = await PageAsync(host, deviceId, "?limit=2");

        first.Items.Select(port => port.IfIndex).Should().Equal(1, 2);
        first.TotalCount.Should().Be(5);
        first.NextCursor.Should().NotBeNull();

        CursorPage<DevicePortSummary> second = await PageAsync(
            host,
            deviceId,
            $"?limit=2&cursor={Uri.EscapeDataString(first.NextCursor!)}");

        second.Items.Select(port => port.IfIndex).Should().Equal(3, 4);
    }

    [Fact]
    public async Task Ports_OfADeviceNothingHasWalked_AreAnEmptyPage()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        ApiResponse read = await host.Client.GetAsync($"{Devices}/{deviceId}/ports", Cancellation);

        read.Status.Should().Be(200);
        Read<CursorPage<DevicePortSummary>>(read).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Ports_OfADeviceThatDoesNotExist_AreANotFound()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        (await host.Client.GetAsync($"{Devices}/{Guid.NewGuid()}/ports", Cancellation))
            .Status.Should().Be(404);
    }

    [Fact]
    public async Task Ports_RefuseACursorTheEndpointDidNotIssue()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        (await host.Client.GetAsync($"{Devices}/{deviceId}/ports?cursor=nonsense", Cancellation))
            .Status.Should().Be(400);
    }

    /// <summary>
    /// Every role holds <c>TopologyRead</c>, so there is no role this route refuses — which is
    /// the point of asserting it rather than assuming it. WP-2.1 put the adjacency reads on that
    /// permission because its own definition already covers the topology graph, and this route is
    /// the same reading of the same tables; if a later package ever narrows the permission, the
    /// least-privileged role failing here is what will say so.
    /// </summary>
    [Theory]
    [InlineData(UserRole.Analyst)]
    [InlineData(UserRole.Operator)]
    [InlineData(UserRole.ReadOnly)]
    public async Task Ports_AreReadableByEveryRoleHoldingTopologyRead(UserRole role)
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await host.SignInAsync(role, Cancellation);

        (await host.Client.GetAsync($"{Devices}/{deviceId}/ports", Cancellation))
            .Status.Should().Be(200);
    }

    // --- helpers -------------------------------------------------------------------------------------

    /// <summary>A device with its interface inventory read, so its ports have names.</summary>
    private static async Task<Guid> WalkedDeviceAsync(InventoryHost host)
    {
        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.WalkAsync(
            host,
            deviceId,
            DiscoveryFixtures.WalkResult(interfaces: [1, 2, 3]),
            Cancellation);

        return deviceId;
    }

    private static async Task<DevicePortSummary> PortAsync(
        InventoryHost host,
        Guid deviceId,
        int ifIndex)
    {
        CursorPage<DevicePortSummary> page = await PageAsync(host, deviceId, "?limit=200");

        return page.Items.Should().ContainSingle(port => port.IfIndex == ifIndex).Subject;
    }

    private static async Task<CursorPage<DevicePortSummary>> PageAsync(
        InventoryHost host,
        Guid deviceId,
        string query)
    {
        ApiResponse read = await host.Client.GetAsync($"{Devices}/{deviceId}/ports{query}", Cancellation);

        read.Status.Should().Be(200);

        return Read<CursorPage<DevicePortSummary>>(read);
    }

    private static T Read<T>(ApiResponse response) =>
        JsonSerializer.Deserialize<T>(response.Json, Options)
            ?? throw new InvalidOperationException("The response body was null.");

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
