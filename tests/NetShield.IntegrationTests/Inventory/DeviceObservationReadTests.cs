using System.Text.Json;
using System.Text.Json.Serialization;

using FluentAssertions;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Paging;

using NetShield.IntegrationTests.Collector;
using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// The three routes WP-1.7 added so that what a walk and a probe recorded is reachable by
/// something other than a database client: the fingerprint, the interface inventory and the
/// reachability detail behind a device's state.
/// </summary>
/// <remarks>
/// Everything asserted here was already being written correctly by WP-1.4 and WP-1.5 and had no
/// way out of PostgreSQL. The tests therefore drive the real round trip — queue, collector
/// contract, outbox — and then read the result back over HTTP, because a read endpoint that
/// agrees with a hand-inserted row proves less than one that agrees with what a collector
/// actually reported.
/// </remarks>
public sealed class DeviceObservationReadTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Devices = "/api/v1/devices";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Fingerprint_AfterAWalk_ReportsWhatTheWalkRead()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);
        await DiscoveryFixtures.WalkAsync(host, deviceId, DiscoveryFixtures.WalkResult(), Cancellation);

        ApiResponse read = await host.Client.GetAsync($"{Devices}/{deviceId}/fingerprint", Cancellation);

        read.Status.Should().Be(200);

        DeviceFingerprintDetail fingerprint = Read<DeviceFingerprintDetail>(read);

        fingerprint.DeviceId.Should().Be(deviceId);
        fingerprint.Vendor.Should().Be(DeviceVendor.CiscoIos);
        fingerprint.SysObjectId.Should().Be("1.3.6.1.4.1.9.1.2494");
        fingerprint.SysDescr.Should().StartWith("Cisco IOS Software");
        fingerprint.SysName.Should().Be("lab-sw-ios-01");
        fingerprint.SysContact.Should().Be("netops@example.invalid");
        fingerprint.SysLocation.Should().Be("Lab rack 3");
        fingerprint.UptimeSeconds.Should().Be(1234567.89);
        fingerprint.Model.Should().Be("WS-C2960X-48FPD-L");
        fingerprint.OsVersion.Should().Be("15.2(7)E3");
        fingerprint.SerialNumber.Should().Be("FOC1234X5YZ");
        fingerprint.InterfaceCount.Should().Be(2);
        fingerprint.InterfacesTruncated.Should().BeFalse();
        fingerprint.ReducedCapability.Should().BeFalse();
        fingerprint.OverriddenFields.Should().BeEmpty();
        fingerprint.LastWalkAt.Should().NotBeNull();
        fingerprint.LastError.Should().BeNull();
    }

    /// <summary>
    /// SPEC.md §4 requires a generic-SNMP device's reduced feature set to be clearly labelled in
    /// the UI. WP-1.5 recorded the fact and nothing could read it; this is the route that can.
    /// </summary>
    [Fact]
    public async Task Fingerprint_OfAGenericSnmpDevice_SaysItsCapabilityIsReduced()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.WalkAsync(
            host,
            deviceId,
            DiscoveryFixtures.WalkResult(
                vendor: "GenericSnmp",
                reducedCapability: true,
                sysObjectId: "1.3.6.1.4.1.99999.1",
                sysDescr: "Some appliance nobody has an adapter for",
                model: null,
                osVersion: null,
                serialNumber: null),
            Cancellation);

        DeviceFingerprintDetail fingerprint = Read<DeviceFingerprintDetail>(
            await host.Client.GetAsync($"{Devices}/{deviceId}/fingerprint", Cancellation));

        fingerprint.Vendor.Should().Be(DeviceVendor.GenericSnmp);
        fingerprint.ReducedCapability.Should().BeTrue();
    }

    /// <summary>
    /// An operator who edits a fact the walk discovered keeps their value, and the disagreement
    /// is named. Rendering that distinction is what the device screen needs it for.
    /// </summary>
    [Fact]
    public async Task Fingerprint_AfterAnOperatorOverride_NamesTheOverriddenField()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);
        await DiscoveryFixtures.WalkAsync(host, deviceId, DiscoveryFixtures.WalkResult(), Cancellation);

        DeviceDetail device = Read<DeviceDetail>(
            await host.Client.GetAsync($"{Devices}/{deviceId}", Cancellation));

        ApiResponse updated = await host.Client.PutAsync(
            $"{Devices}/{deviceId}",
            new UpdateDeviceRequest(
                device.Hostname,
                device.PrimaryIpAddress,
                device.Vendor,
                Model: "operator-says-otherwise",
                OsVersion: device.OsVersion,
                SerialNumber: device.SerialNumber,
                Site: device.Site,
                Role: device.Role,
                Criticality: device.Criticality,
                Environment: device.Environment,
                Owner: device.Owner,
                Tags: device.Tags,
                Notes: device.Notes),
            Cancellation);

        updated.Status.Should().Be(200);

        // A second walk is what recomputes the comparison.
        await DiscoveryFixtures.WalkAsync(host, deviceId, DiscoveryFixtures.WalkResult(), Cancellation);

        DeviceFingerprintDetail fingerprint = Read<DeviceFingerprintDetail>(
            await host.Client.GetAsync($"{Devices}/{deviceId}/fingerprint", Cancellation));

        fingerprint.OverriddenFields.Should().Contain("model");

        // The walk's own copy is still what the walk read — that is the whole point of the row.
        fingerprint.Model.Should().Be("WS-C2960X-48FPD-L");
    }

    /// <summary>
    /// A failed walk records why and erases nothing. Without this member, a device whose walks
    /// have been failing since Tuesday looks identical to one that was walked on Tuesday.
    /// </summary>
    [Fact]
    public async Task Fingerprint_AfterAFailedWalk_CarriesTheErrorAndKeepsTheLastGoodFacts()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);
        await DiscoveryFixtures.WalkAsync(host, deviceId, DiscoveryFixtures.WalkResult(), Cancellation);
        await DiscoveryFixtures.FailWalkAsync(host, deviceId, "SNMP timeout after 5s", Cancellation);

        DeviceFingerprintDetail fingerprint = Read<DeviceFingerprintDetail>(
            await host.Client.GetAsync($"{Devices}/{deviceId}/fingerprint", Cancellation));

        fingerprint.LastError.Should().Contain("timeout");
        fingerprint.SysName.Should().Be("lab-sw-ios-01");
        fingerprint.Model.Should().Be("WS-C2960X-48FPD-L");
    }

    /// <summary>
    /// "This device has never been walked" and "this device does not exist" are different
    /// answers, and only the first is something an operator can act on.
    /// </summary>
    [Fact]
    public async Task Fingerprint_OfADeviceNoWalkHasReached_Returns404NamingTheFingerprint()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(
            host,
            "never-walked",
            "10.10.9.9",
            Cancellation);

        ApiResponse read = await host.Client.GetAsync($"{Devices}/{deviceId}/fingerprint", Cancellation);

        read.Status.Should().Be(404);
        read.Member("code").Should().Be("discovery.fingerprint-not-found");
    }

    [Fact]
    public async Task Fingerprint_OfADeviceThatDoesNotExist_Returns404NamingTheDevice()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse read = await host.Client.GetAsync(
            $"{Devices}/{Guid.CreateVersion7()}/fingerprint",
            Cancellation);

        read.Status.Should().Be(404);
        read.Member("code").Should().Be("device.not-found");
    }

    [Fact]
    public async Task Interfaces_AfterAWalk_ListThemInIfIndexOrderWithNamedStatuses()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);
        await DiscoveryFixtures.WalkAsync(host, deviceId, DiscoveryFixtures.WalkResult(), Cancellation);

        ApiResponse read = await host.Client.GetAsync($"{Devices}/{deviceId}/interfaces", Cancellation);

        read.Status.Should().Be(200);

        CursorPage<DeviceInterfaceSummary> page = Read<CursorPage<DeviceInterfaceSummary>>(read);

        page.TotalCount.Should().Be(2);
        page.Items.Select(row => row.IfIndex).Should().Equal(1, 2);

        DeviceInterfaceSummary first = page.Items[0];

        first.Name.Should().Be("Gi0/1");
        first.Description.Should().Be("GigabitEthernet0/1");
        first.Alias.Should().Be("port 1");
        first.Mtu.Should().Be(1500);
        first.SpeedBitsPerSecond.Should().Be(1_000_000_000);
        first.PhysicalAddress.Should().Be("00:1A:2B:3C:4D:01");

        // The IF-MIB integers do not leave the module. 1 is up, on both counters.
        first.AdminStatus.Should().Be(InterfaceStatus.Up);
        first.OperStatus.Should().Be(InterfaceStatus.Up);
    }

    /// <summary>
    /// <c>ifOperStatus</c> 7 is <c>lowerLayerDown</c>, which a screen renders differently from a
    /// port somebody shut. Mapping it is the reason the contract does not carry the integer.
    /// </summary>
    [Fact]
    public async Task Interfaces_WithADownstreamFailure_NameLowerLayerDown()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.WalkAsync(
            host,
            deviceId,
            DiscoveryFixtures.WalkResult(operStatus: 7),
            Cancellation);

        CursorPage<DeviceInterfaceSummary> page = Read<CursorPage<DeviceInterfaceSummary>>(
            await host.Client.GetAsync($"{Devices}/{deviceId}/interfaces", Cancellation));

        page.Items.Should().OnlyContain(row => row.OperStatus == InterfaceStatus.LowerLayerDown);
        page.Items.Should().OnlyContain(row => row.AdminStatus == InterfaceStatus.Up);
    }

    /// <summary>
    /// A value the MIB does not define is <see cref="InterfaceStatus.Unknown"/> rather than a
    /// number the screen has to have an opinion about.
    /// </summary>
    [Fact]
    public async Task Interfaces_WithAStatusTheMibDoesNotDefine_ReadAsUnknown()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.WalkAsync(
            host,
            deviceId,
            DiscoveryFixtures.WalkResult(operStatus: 42),
            Cancellation);

        CursorPage<DeviceInterfaceSummary> page = Read<CursorPage<DeviceInterfaceSummary>>(
            await host.Client.GetAsync($"{Devices}/{deviceId}/interfaces", Cancellation));

        page.Items.Should().OnlyContain(row => row.OperStatus == InterfaceStatus.Unknown);
    }

    [Fact]
    public async Task Interfaces_PageThroughACursorWithoutRepeatingOrSkipping()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.WalkAsync(
            host,
            deviceId,
            DiscoveryFixtures.WalkResult(interfaces: [1, 2, 3, 4, 5]),
            Cancellation);

        List<int> seen = [];
        string? cursor = null;

        do
        {
            string query = cursor is null
                ? $"{Devices}/{deviceId}/interfaces?limit=2"
                : $"{Devices}/{deviceId}/interfaces?limit=2&cursor={Uri.EscapeDataString(cursor)}";

            CursorPage<DeviceInterfaceSummary> page = Read<CursorPage<DeviceInterfaceSummary>>(
                await host.Client.GetAsync(query, Cancellation));

            seen.AddRange(page.Items.Select(row => row.IfIndex));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        seen.Should().Equal(1, 2, 3, 4, 5);
    }

    /// <summary>
    /// A device nothing has walked has no interfaces, which is an empty page. Only a device that
    /// is not there is a 404 — the two absences are different facts.
    /// </summary>
    [Fact]
    public async Task Interfaces_OfADeviceNoWalkHasReached_AreAnEmptyPage()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(
            host,
            "never-walked",
            "10.10.9.8",
            Cancellation);

        ApiResponse read = await host.Client.GetAsync($"{Devices}/{deviceId}/interfaces", Cancellation);

        read.Status.Should().Be(200);
        Read<CursorPage<DeviceInterfaceSummary>>(read).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Interfaces_OfADeviceThatDoesNotExist_Return404()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse read = await host.Client.GetAsync(
            $"{Devices}/{Guid.CreateVersion7()}/interfaces",
            Cancellation);

        read.Status.Should().Be(404);
        read.Member("code").Should().Be("device.not-found");
    }

    [Fact]
    public async Task Reachability_AfterAProbe_ReportsTheRoundTripAndTheLoss()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(
            host,
            "switch-01",
            "10.10.0.1",
            Cancellation);

        await host.ScheduleReachabilityAsync(Cancellation);
        await ReachabilityFixtures.CompleteOneAsync(host, sent: 4, received: 4, Cancellation);

        ApiResponse read = await host.Client.GetAsync($"{Devices}/{deviceId}/reachability", Cancellation);

        read.Status.Should().Be(200);

        DeviceReachabilityDetail reachability = Read<DeviceReachabilityDetail>(read);

        reachability.DeviceId.Should().Be(deviceId);
        reachability.LastRttMilliseconds.Should().NotBeNull();
        reachability.LastLossPercent.Should().Be(0);
        reachability.LastProbeAt.Should().NotBeNull();
        reachability.LastError.Should().BeNull();
    }

    /// <summary>
    /// The gap this route exists to close. A collector that cannot probe leaves the device's
    /// state alone by design (WP-1.4), so without <c>lastError</c> the device presents as
    /// confidently whatever it last was, with a timestamp that quietly stops moving.
    /// </summary>
    [Fact]
    public async Task Reachability_WhenTheCollectorCouldNotProbe_SaysWhyWithoutMovingTheState()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(
            host,
            "switch-01",
            "10.10.0.1",
            Cancellation);

        await host.ScheduleReachabilityAsync(Cancellation);
        await ReachabilityFixtures.CompleteOneAsync(host, sent: 4, received: 4, Cancellation);
        await host.MakeDueAsync(deviceId, Cancellation);

        await host.ScheduleReachabilityAsync(Cancellation);
        await ReachabilityFixtures.FailOneAsync(host, "no ICMP socket could be opened", Cancellation);

        DeviceReachabilityDetail reachability = Read<DeviceReachabilityDetail>(
            await host.Client.GetAsync($"{Devices}/{deviceId}/reachability", Cancellation));

        reachability.LastError.Should().Contain("ICMP socket");
        reachability.State.Should().Be(DeviceState.Unknown, "one good probe is short of the threshold");
    }

    /// <summary>
    /// The state on this route and the state on the device are the same fact read once. Two
    /// places answering it differently is the bug this asserts against.
    /// </summary>
    [Fact]
    public async Task Reachability_ReportsTheSameStateTheDeviceDoes()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(
            host,
            "switch-01",
            "10.10.0.1",
            Cancellation);

        foreach (int _ in Enumerable.Range(0, 3))
        {
            await host.ScheduleReachabilityAsync(Cancellation);
            await ReachabilityFixtures.CompleteOneAsync(host, sent: 4, received: 0, Cancellation);
            await host.MakeDueAsync(deviceId, Cancellation);
        }

        DeviceReachabilityDetail reachability = Read<DeviceReachabilityDetail>(
            await host.Client.GetAsync($"{Devices}/{deviceId}/reachability", Cancellation));

        DeviceDetail device = Read<DeviceDetail>(
            await host.Client.GetAsync($"{Devices}/{deviceId}", Cancellation));

        reachability.State.Should().Be(DeviceState.Offline);
        reachability.State.Should().Be(device.State);
        reachability.LastChangedAt.Should().NotBeNull();
        reachability.LastLossPercent.Should().Be(100);
    }

    [Fact]
    public async Task Reachability_OfADeviceNothingHasScheduled_Returns404NamingTheProbe()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(
            host,
            "never-probed",
            "10.10.9.7",
            Cancellation);

        ApiResponse read = await host.Client.GetAsync($"{Devices}/{deviceId}/reachability", Cancellation);

        read.Status.Should().Be(404);
        read.Member("code").Should().Be("reachability.not-found");
    }

    /// <summary>
    /// All three are ordinary device reads, on the permission every other device read uses —
    /// checked at the endpoint and again in the module (ARCHITECTURE.md §8). Nothing here is a
    /// credential and nothing here says which credential a walk used, which is why none of them
    /// is behind <c>CredentialsManage</c>.
    /// </summary>
    [Theory]
    [InlineData(UserRole.Analyst, "fingerprint")]
    [InlineData(UserRole.Analyst, "interfaces")]
    [InlineData(UserRole.Analyst, "reachability")]
    [InlineData(UserRole.ReadOnly, "fingerprint")]
    [InlineData(UserRole.ReadOnly, "interfaces")]
    [InlineData(UserRole.ReadOnly, "reachability")]
    public async Task Every_ObservationRoute_IsReadableByAnyRoleHoldingInventoryRead(
        UserRole role,
        string route)
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);
        await DiscoveryFixtures.WalkAsync(host, deviceId, DiscoveryFixtures.WalkResult(), Cancellation);
        await host.ScheduleReachabilityAsync(Cancellation);

        await host.SignInAsync(role, Cancellation);

        (await host.Client.GetAsync($"{Devices}/{deviceId}/{route}", Cancellation))
            .Status.Should().Be(200);
    }

    private static T Read<T>(ApiResponse response) =>
        JsonSerializer.Deserialize<T>(response.Body, JsonOptions)!;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}
