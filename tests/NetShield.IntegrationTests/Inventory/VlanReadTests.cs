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
/// The three routes that make the VLAN inventory reachable by something other than a database
/// client: the estate list, one VLAN's detail, and one device's VLANs.
/// </summary>
/// <remarks>
/// Driven through the real round trip and then read back over HTTP, for the reason
/// <see cref="TopologyReadTests"/> does the same: a read endpoint that agrees with a hand-inserted
/// row proves less than one that agrees with what a collector actually reported.
/// </remarks>
public sealed class VlanReadTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Devices = "/api/v1/devices";
    private const string Vlans = "/api/v1/vlans";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // --- paging -------------------------------------------------------------------------------

    [Fact]
    public async Task TheEstateList_IsOrderedByVlanIdAndPagesByIt()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.VlanWalkAsync(
            host,
            deviceId,
            TopologyFixtures.VlanResult(
            [
                TopologyFixtures.Vlan(10, "Server VLAN", [1], [1]),
                TopologyFixtures.Vlan(20, "Users VLAN", [2], [2]),
                TopologyFixtures.Vlan(30, "Voice VLAN", [3], [3]),
                TopologyFixtures.Vlan(40, "Guest VLAN", [4], [4])
            ]),
            Cancellation);

        CursorPage<VlanSummary> first = Read<CursorPage<VlanSummary>>(
            await host.Client.GetAsync($"{Vlans}?limit=2", Cancellation));

        first.Items.Select(vlan => vlan.VlanId).Should().Equal(10, 20);
        first.TotalCount.Should().Be(4);
        first.NextCursor.Should().NotBeNull();

        CursorPage<VlanSummary> second = Read<CursorPage<VlanSummary>>(
            await host.Client.GetAsync($"{Vlans}?limit=2&cursor={first.NextCursor}", Cancellation));

        second.Items.Select(vlan => vlan.VlanId).Should().Equal(30, 40);
        second.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task TheDeviceList_PagesByVlanIdToo()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.VlanWalkAsync(
            host,
            deviceId,
            TopologyFixtures.VlanResult(
            [
                TopologyFixtures.Vlan(10, "Server VLAN", [1], [1]),
                TopologyFixtures.Vlan(20, "Users VLAN", [2], [2]),
                TopologyFixtures.Vlan(30, "Voice VLAN", [3], [3])
            ]),
            Cancellation);

        CursorPage<DeviceVlanSummary> first = Read<CursorPage<DeviceVlanSummary>>(
            await host.Client.GetAsync($"{Devices}/{deviceId}/vlans?limit=1", Cancellation));

        first.Items.Should().ContainSingle();
        first.TotalCount.Should().Be(3);

        CursorPage<DeviceVlanSummary> rest = Read<CursorPage<DeviceVlanSummary>>(
            await host.Client.GetAsync(
                $"{Devices}/{deviceId}/vlans?limit=10&cursor={first.NextCursor}",
                Cancellation));

        rest.Items.Select(vlan => vlan.VlanId).Should().Equal(20, 30);
    }

    [Theory]
    [InlineData("?limit=0")]
    [InlineData("?limit=201")]
    [InlineData("?cursor=!!!")]
    public async Task AMalformedPageRequest_IsRefusedRatherThanClamped(string query)
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        (await host.Client.GetAsync($"{Vlans}{query}", Cancellation)).Status.Should().Be(400);
    }

    // --- the empty and the missing --------------------------------------------------------------

    [Fact]
    public async Task AnEstateWithNoVlansReadYet_IsAnEmptyPage()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        CursorPage<VlanSummary> page = Read<CursorPage<VlanSummary>>(
            await host.Client.GetAsync(Vlans, Cancellation));

        page.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task ADeviceNothingHasWalked_HasAnEmptyVlanPageRatherThanA404()
    {
        // The topology-scan route is where "nothing has read this device" is said. The same split
        // WP-1.7 drew between the interface list and the fingerprint.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        ApiResponse read = await host.Client.GetAsync($"{Devices}/{deviceId}/vlans", Cancellation);

        read.Status.Should().Be(200);
        Read<CursorPage<DeviceVlanSummary>>(read).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ADeviceThatDoesNotExist_Is404()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        (await host.Client.GetAsync($"{Devices}/{Guid.NewGuid()}/vlans", Cancellation))
            .Status.Should().Be(404);
    }

    [Fact]
    public async Task AVlanNoDeviceCarries_Is404()
    {
        // A VLAN exists in NetShield because a device reported it, so "no device carries it" and
        // "there is no such VLAN" are the same statement.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse missing = await host.Client.GetAsync($"{Vlans}/20", Cancellation);

        missing.Status.Should().Be(404);
        missing.Body.Should().Contain("topology.vlan-not-found");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4095)]
    public async Task AVlanIdOutsideTheStandardsRange_IsRefusedRatherThanNotFound(int vlanId)
    {
        // A caller asking for VLAN 9000 has made a different kind of mistake from one asking for
        // a VLAN nothing carries, and only one of the two is worth retrying with a new number.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse refused = await host.Client.GetAsync($"{Vlans}/{vlanId}", Cancellation);

        refused.Status.Should().Be(400);
        refused.Body.Should().Contain("topology.vlan-id-out-of-range");
    }

    // --- what a removed device stops contributing -----------------------------------------------

    [Fact]
    public async Task ARemovedDevice_StopsCountingTowardsAVlan()
    {
        // A soft-deleted device's VLAN rows survive as history. Counting them would have a switch
        // an operator deleted still contributing to the tile they read.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host, Cancellation, "core-sw-1", "10.10.0.1");

        Guid access = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host, Cancellation, "acc-sw-1", "10.10.0.2");

        foreach (Guid deviceId in new[] { core, access })
        {
            await TopologyFixtures.VlanWalkAsync(
                host,
                deviceId,
                TopologyFixtures.VlanResult([TopologyFixtures.Vlan(20, "Users VLAN", [1], [1])]),
                Cancellation);
        }

        Read<CursorPage<VlanSummary>>(await host.Client.GetAsync(Vlans, Cancellation))
            .Items[0].DeviceCount.Should().Be(2);

        (await host.Client.DeleteAsync($"{Devices}/{access}", Cancellation)).Status.Should().Be(204);

        VlanDetail detail = Read<VlanDetail>(await host.Client.GetAsync($"{Vlans}/20", Cancellation));

        detail.DeviceCount.Should().Be(1);
        detail.Devices.Should().ContainSingle();
        detail.Devices[0].DeviceId.Should().Be(core);
    }

    // --- client counts ---------------------------------------------------------------------------

    [Fact]
    public async Task AVlansClientCount_ComesFromTheOpenPortBindings()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.VlanWalkAsync(
            host,
            deviceId,
            TopologyFixtures.VlanResult(
            [
                TopologyFixtures.Vlan(20, "Users VLAN", [10], [10]),
                TopologyFixtures.Vlan(30, "Voice VLAN", [11], [11])
            ]),
            Cancellation);

        await ClientFixtures.WalkAsync(
            host,
            deviceId,
            ClientFixtures.WalkResult(forwarding:
            [
                ClientFixtures.Forwarding("AA:BB:CC:00:00:01", 10, vlanId: 20),
                ClientFixtures.Forwarding("AA:BB:CC:00:00:02", 10, vlanId: 20),
                ClientFixtures.Forwarding("AA:BB:CC:00:00:03", 11, vlanId: 30)
            ]),
            Cancellation);

        CursorPage<VlanSummary> page = Read<CursorPage<VlanSummary>>(
            await host.Client.GetAsync(Vlans, Cancellation));

        page.Items.Single(vlan => vlan.VlanId == 20).ClientCount.Should().Be(2);
        page.Items.Single(vlan => vlan.VlanId == 30).ClientCount.Should().Be(1);
    }

    [Fact]
    public async Task AClientLearnedByTwoSwitches_CountsOnceForTheEstateAndOnceForEach()
    {
        // A MAC is learned by every bridge on the path to it, and WP-1.8 keeps one open binding
        // per client and device. The estate-wide count de-duplicates by client; the per-device
        // count deliberately does not, because that is the truth about a forwarding database.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid core = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host, Cancellation, "core-sw-1", "10.10.0.1");

        Guid access = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host, Cancellation, "acc-sw-1", "10.10.0.2");

        foreach (Guid deviceId in new[] { core, access })
        {
            await TopologyFixtures.VlanWalkAsync(
                host,
                deviceId,
                TopologyFixtures.VlanResult([TopologyFixtures.Vlan(20, "Users VLAN", [10], [10])]),
                Cancellation);

            await ClientFixtures.WalkAsync(
                host,
                deviceId,
                ClientFixtures.WalkResult(forwarding:
                    [ClientFixtures.Forwarding("AA:BB:CC:00:00:01", 10, vlanId: 20)]),
                Cancellation);
        }

        VlanDetail detail = Read<VlanDetail>(await host.Client.GetAsync($"{Vlans}/20", Cancellation));

        detail.ClientCount.Should().Be(1, "one laptop is one client however many bridges saw it");
        detail.Devices.Should().OnlyContain(device => device.ClientCount == 1);
    }

    // --- authorization -----------------------------------------------------------------------------

    /// <summary>
    /// Every VLAN read is on <see cref="Permission.TopologyRead"/>, whose own definition reads
    /// "the topology graph and the VLAN inventory" — checked at the endpoint and again in the
    /// module (ARCHITECTURE.md §8). No RBAC table changed for this package.
    /// </summary>
    [Theory]
    [InlineData(UserRole.Analyst)]
    [InlineData(UserRole.ReadOnly)]
    public async Task EveryVlanRoute_IsReadableByAnyRoleHoldingTopologyRead(UserRole role)
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.VlanWalkAsync(
            host,
            deviceId,
            TopologyFixtures.VlanResult([TopologyFixtures.Vlan(20, "Users VLAN", [1], [1])]),
            Cancellation);

        await host.SignInAsync(role, Cancellation);

        (await host.Client.GetAsync(Vlans, Cancellation)).Status.Should().Be(200);
        (await host.Client.GetAsync($"{Vlans}/20", Cancellation)).Status.Should().Be(200);

        (await host.Client.GetAsync($"{Devices}/{deviceId}/vlans", Cancellation))
            .Status.Should().Be(200);
    }

    [Theory]
    [InlineData(UserRole.Analyst)]
    [InlineData(UserRole.ReadOnly)]
    public async Task AVlanWalk_IsRefusedToARoleWithoutDiscoveryRun(UserRole role)
    {
        // Queueing a walk makes NetShield open a credential and read a device outside its
        // schedule, which is a different privilege from reading what it found.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await host.SignInAsync(role, Cancellation);

        (await TopologyFixtures.RequestVlanWalkAsync(host, deviceId, Cancellation))
            .Status.Should().Be(403);
    }

    [Theory]
    [InlineData(UserRole.Administrator)]
    [InlineData(UserRole.Operator)]
    public async Task AVlanWalk_IsPermittedToARoleHoldingDiscoveryRun(UserRole role)
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await host.SignInAsync(role, Cancellation);

        (await TopologyFixtures.RequestVlanWalkAsync(host, deviceId, Cancellation))
            .Status.Should().Be(202);
    }

    [Fact]
    public async Task AVlanWalk_WritesAnAuditRow()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.RequestVlanWalkAsync(host, deviceId, Cancellation);

        (await host.AuditRowsAsync(Cancellation))
            .Should().Contain(row => row.Action == "inventory.device-vlan-walk");
    }

    private static T Read<T>(ApiResponse response) =>
        JsonSerializer.Deserialize<T>(response.Body, JsonOptions)!;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}
