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
/// The two routes that make a device's topology reachable by something other than a database
/// client: its edge list and the state of its topology collection.
/// </summary>
/// <remarks>
/// Driven through the real round trip — queue, collector contract, outbox — and then read back
/// over HTTP, for the reason <c>DeviceObservationReadTests</c> does the same: a read endpoint
/// that agrees with a hand-inserted row proves less than one that agrees with what a collector
/// actually reported.
/// </remarks>
public sealed class TopologyReadTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Devices = "/api/v1/devices";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private const string AccessChassis = "00:1C:73:00:00:02";

    [Fact]
    public async Task Adjacencies_AfterAWalk_ReportWhatTheWalkFound()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            deviceId,
            TopologyFixtures.NeighborResult(
                localChassisId: "00:1C:73:00:00:01",
                lldp:
                [
                    TopologyFixtures.Lldp(
                        1,
                        AccessChassis,
                        "Gi0/1",
                        systemName: "acc-sw-1",
                        localPortName: "Et1")
                ]),
            Cancellation);

        ApiResponse read = await host.Client.GetAsync($"{Devices}/{deviceId}/adjacencies", Cancellation);

        read.Status.Should().Be(200);

        CursorPage<DeviceAdjacencySummary> page = Read<CursorPage<DeviceAdjacencySummary>>(read);

        page.Items.Should().ContainSingle();

        DeviceAdjacencySummary edge = page.Items[0];

        edge.ADeviceId.Should().Be(deviceId);
        edge.ADeviceHostname.Should().Be("switch-01");
        edge.AIfIndex.Should().Be(1);
        edge.AInterfaceName.Should().Be("Et1");
        edge.BChassisId.Should().Be(AccessChassis);
        edge.BChassisIdKind.Should().Be(NeighborIdKind.MacAddress);
        edge.BPortId.Should().Be("Gi0/1");
        edge.BSystemName.Should().Be("acc-sw-1");
        edge.Sources.Should().BeEquivalentTo([NeighborSource.Lldp]);
        edge.Confidence.Should().Be(AdjacencyConfidence.Probable);
        edge.Bidirectional.Should().BeFalse();
    }

    [Fact]
    public async Task Adjacencies_CarryTheEvidenceBehindEachEdge()
    {
        // An adjacency is a conclusion; the evidence is what each protocol actually said. Both
        // are shown because they disagree in ways an operator has to be able to see.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            deviceId,
            TopologyFixtures.NeighborResult(
                lldp:
                [
                    TopologyFixtures.Lldp(
                        1,
                        AccessChassis,
                        "Gi0/1",
                        systemName: "acc-sw-1",
                        managementAddress: "10.10.0.42")
                ]),
            Cancellation);

        CursorPage<DeviceAdjacencySummary> page = Read<CursorPage<DeviceAdjacencySummary>>(
            await host.Client.GetAsync($"{Devices}/{deviceId}/adjacencies", Cancellation));

        AdjacencyEvidence evidence = page.Items[0].Evidence.Should().ContainSingle().Subject;

        evidence.ObservedByDeviceId.Should().Be(deviceId);
        evidence.Source.Should().Be(NeighborSource.Lldp);
        evidence.LocalIfIndex.Should().Be(1);
        evidence.RemoteChassisId.Should().Be(AccessChassis);
        evidence.RemoteManagementAddress.Should().Be("10.10.0.42");
    }

    [Fact]
    public async Task Adjacencies_OfADeviceNothingHasWalked_AreAnEmptyPage()
    {
        // An empty page rather than a 404: the scan route is where "nothing has read this
        // device's topology" is said. The same split WP-1.7 drew for interfaces.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        ApiResponse read = await host.Client.GetAsync($"{Devices}/{deviceId}/adjacencies", Cancellation);

        read.Status.Should().Be(200);
        Read<CursorPage<DeviceAdjacencySummary>>(read).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Adjacencies_OfADeviceThatDoesNotExist_AreANotFound()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        (await host.Client.GetAsync($"{Devices}/{Guid.NewGuid()}/adjacencies", Cancellation))
            .Status.Should().Be(404);
    }

    [Fact]
    public async Task Adjacencies_ArePaginated()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            deviceId,
            TopologyFixtures.NeighborResult(
                lldp:
                [
                    TopologyFixtures.Lldp(1, "00:1C:73:00:00:02", "Gi0/1"),
                    TopologyFixtures.Lldp(2, "00:1C:73:00:00:03", "Gi0/1"),
                    TopologyFixtures.Lldp(3, "00:1C:73:00:00:04", "Gi0/1")
                ]),
            Cancellation);

        CursorPage<DeviceAdjacencySummary> first = Read<CursorPage<DeviceAdjacencySummary>>(
            await host.Client.GetAsync($"{Devices}/{deviceId}/adjacencies?limit=2", Cancellation));

        first.Items.Should().HaveCount(2);
        first.TotalCount.Should().Be(3);
        first.NextCursor.Should().NotBeNull();

        CursorPage<DeviceAdjacencySummary> second = Read<CursorPage<DeviceAdjacencySummary>>(
            await host.Client.GetAsync(
                $"{Devices}/{deviceId}/adjacencies?limit=2&cursor={Uri.EscapeDataString(first.NextCursor!)}",
                Cancellation));

        second.Items.Should().ContainSingle();
        second.NextCursor.Should().BeNull();

        first.Items.Concat(second.Items).Select(edge => edge.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Adjacencies_RefuseACursorTheEndpointDidNotIssue()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        (await host.Client.GetAsync($"{Devices}/{deviceId}/adjacencies?cursor=nonsense", Cancellation))
            .Status.Should().Be(400);
    }

    [Fact]
    public async Task Adjacencies_DoNotIncludeAWithdrawnEdge()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            deviceId,
            TopologyFixtures.NeighborResult(
                lldp: [TopologyFixtures.Lldp(1, AccessChassis, "Gi0/1")]),
            Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            deviceId,
            TopologyFixtures.NeighborResult(lldpSupported: true),
            Cancellation);

        Read<CursorPage<DeviceAdjacencySummary>>(
            await host.Client.GetAsync($"{Devices}/{deviceId}/adjacencies", Cancellation))
            .Items.Should().BeEmpty();
    }

    // --- The scan route ---------------------------------------------------------------------------

    [Fact]
    public async Task TopologyScan_ReportsWhichProtocolsTheDeviceAnswered()
    {
        // The route that answers "why does this switch show no edges?".
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            deviceId,
            TopologyFixtures.NeighborResult(
                lldp: [TopologyFixtures.Lldp(1, AccessChassis, "Gi0/1")],
                cdpSupported: false),
            Cancellation);

        ApiResponse read = await host.Client.GetAsync(
            $"{Devices}/{deviceId}/topology-scan",
            Cancellation);

        read.Status.Should().Be(200);

        DeviceTopologyScanDetail scan = Read<DeviceTopologyScanDetail>(read);

        scan.DeviceId.Should().Be(deviceId);
        scan.LldpSupported.Should().BeTrue();
        scan.CdpSupported.Should().BeFalse();
        scan.LastLldpCount.Should().Be(1);
        scan.LastCdpCount.Should().BeNull("the device answered no CDP cache to count");
        scan.LastNeighborWalkAt.Should().NotBeNull();
        scan.LastNeighborError.Should().BeNull();

        // The route half is untouched: nothing has read this device's routing table yet.
        scan.RoutingSupported.Should().BeNull();
        scan.LastRouteWalkAt.Should().BeNull();
    }

    [Fact]
    public async Task TopologyScan_OfADeviceNothingHasWalked_IsItsOwnNotFound()
    {
        // Distinct from device.not-found, the way WP-1.7 made the fingerprint's absence distinct:
        // only one of the two is something an operator can act on, and the action is to walk it.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        ApiResponse read = await host.Client.GetAsync(
            $"{Devices}/{deviceId}/topology-scan",
            Cancellation);

        read.Status.Should().Be(404);
        read.Member("code").Should().Be("topology.scan-not-found");
    }

    [Fact]
    public async Task TopologyScan_OfADeviceThatDoesNotExist_IsADeviceNotFound()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse read = await host.Client.GetAsync(
            $"{Devices}/{Guid.NewGuid()}/topology-scan",
            Cancellation);

        read.Status.Should().Be(404);
        read.Member("code").Should().Be("device.not-found");
    }

    // --- Authorization ----------------------------------------------------------------------------

    /// <summary>
    /// Both are topology reads, on the permission whose own definition names the topology graph —
    /// checked at the endpoint and again in the module (ARCHITECTURE.md §8). Nothing here is a
    /// credential and nothing here says which credential a walk used.
    /// </summary>
    [Theory]
    [InlineData(UserRole.Analyst, "adjacencies")]
    [InlineData(UserRole.Analyst, "topology-scan")]
    [InlineData(UserRole.ReadOnly, "adjacencies")]
    [InlineData(UserRole.ReadOnly, "topology-scan")]
    public async Task EveryTopologyRoute_IsReadableByAnyRoleHoldingTopologyRead(
        UserRole role,
        string route)
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await TopologyFixtures.NeighborWalkAsync(
            host,
            deviceId,
            TopologyFixtures.NeighborResult(
                lldp: [TopologyFixtures.Lldp(1, AccessChassis, "Gi0/1")]),
            Cancellation);

        await host.SignInAsync(role, Cancellation);

        (await host.Client.GetAsync($"{Devices}/{deviceId}/{route}", Cancellation))
            .Status.Should().Be(200);
    }

    [Theory]
    [InlineData(UserRole.Analyst, "neighbor-walk")]
    [InlineData(UserRole.Analyst, "route-walk")]
    [InlineData(UserRole.ReadOnly, "neighbor-walk")]
    [InlineData(UserRole.ReadOnly, "route-walk")]
    public async Task AWalk_IsRefusedToARoleWithoutDiscoveryRun(UserRole role, string route)
    {
        // Queueing a walk makes NetShield open a credential and read a device outside its
        // schedule, which is a different privilege from reading what it found.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await host.SignInAsync(role, Cancellation);

        ApiResponse refused = await host.Client.PostAsync(
            $"{Devices}/{deviceId}/{route}",
            new { },
            Cancellation);

        refused.Status.Should().Be(403);
    }

    [Theory]
    [InlineData(UserRole.Administrator, "neighbor-walk")]
    [InlineData(UserRole.Operator, "route-walk")]
    public async Task AWalk_IsPermittedToARoleHoldingDiscoveryRun(UserRole role, string route)
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await host.SignInAsync(role, Cancellation);

        ApiResponse queued = await host.Client.PostAsync(
            $"{Devices}/{deviceId}/{route}",
            new { },
            Cancellation);

        queued.Status.Should().Be(202);
        queued.Json.GetProperty("deviceId").GetGuid().Should().Be(deviceId);
        queued.Json.GetProperty("walk").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AQueuedWalk_SaysWhichWalkItIsAndNothingAboutTheCredential()
    {
        // The chosen profile is on the job row and in the log, and deliberately not here: this
        // route is gated on DiscoveryRun, which says nothing about credentials.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        ApiResponse queued =
            await TopologyFixtures.RequestRouteWalkAsync(host, deviceId, Cancellation);

        queued.Status.Should().Be(202);

        NeighborWalkQueued body = Read<NeighborWalkQueued>(queued);

        body.Walk.Should().Be(TopologyWalkKind.Routes);
        body.DeviceId.Should().Be(deviceId);
        body.JobId.Should().NotBeEmpty();

        queued.Body.Should().NotContain("credential", "the answer says what was queued and not what it will be run with");
    }

    private static T Read<T>(ApiResponse response) =>
        JsonSerializer.Deserialize<T>(response.Body, JsonOptions)!;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}
