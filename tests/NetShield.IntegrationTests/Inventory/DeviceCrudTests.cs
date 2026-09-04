using System.Text.Json;

using FluentAssertions;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Inventory.Events;

using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

using NetShield.Platform.Auditing;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// Device CRUD against real PostgreSQL: the happy path, the refusals, and the two things that
/// only hold because the database enforces them — the duplicate address and the soft delete.
/// </summary>
public sealed class DeviceCrudTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Devices = "/api/v1/devices";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_AValidDevice_Returns201WithLocationAndTheStoredShape()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse created = await host.Client.PostAsync(
            Devices,
            new CreateDeviceRequest(
                "core-sw-01",
                "10.0.0.1",
                DeviceVendor.CiscoIos,
                Model: "C9300-48P",
                Site: "HQ",
                Role: DeviceRole.Switch,
                Criticality: CriticalityTier.Critical,
                Environment: DeviceEnvironment.Production,
                Tags: ["Core", "core", " EDGE "]),
            Cancellation);

        created.Status.Should().Be(201);

        DeviceDetail device = Read<DeviceDetail>(created);

        device.Hostname.Should().Be("core-sw-01");
        device.PrimaryIpAddress.Should().Be("10.0.0.1");
        device.Vendor.Should().Be(DeviceVendor.CiscoIos);
        device.Criticality.Should().Be(CriticalityTier.Critical);

        // Normalised on the way in: case folded, duplicates collapsed, sorted.
        device.Tags.Should().Equal("core", "edge");

        // Never asserted by a caller — there is no member to assert it with, and nothing has
        // probed this device (WP-1.4).
        device.State.Should().Be(DeviceState.Unknown);
    }

    [Fact]
    public async Task Create_AnAddressALiveDeviceHolds_Returns409()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await CreateAsync(host, "core-sw-01", "10.0.0.1");

        ApiResponse conflict = await host.Client.PostAsync(
            Devices,
            new CreateDeviceRequest("core-sw-02", "10.0.0.1"),
            Cancellation);

        conflict.Status.Should().Be(409);
        conflict.Member("code").Should().Be("device.duplicate-primary-ip");
    }

    /// <summary>
    /// <c>inet</c> normalises the notation, so two spellings of one address cannot both be
    /// stored and both look free. This is the reason the column is not <c>text</c>.
    /// </summary>
    [Fact]
    public async Task Create_TheSameAddressSpeltDifferently_IsStillAConflict()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await CreateAsync(host, "edge-fw-01", "2001:db8::1");

        ApiResponse conflict = await host.Client.PostAsync(
            Devices,
            new CreateDeviceRequest("edge-fw-02", "2001:0db8:0000:0000:0000:0000:0000:0001"),
            Cancellation);

        conflict.Status.Should().Be(409);
    }

    [Fact]
    public async Task Create_TwoDevicesWithOneHostname_IsAllowed()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await CreateAsync(host, "switch", "10.0.0.1");

        // Hostnames are descriptions, not identities: DHCP naming, reused defaults, split DNS and
        // cloned systems all produce real duplicates that discovery has to be able to record.
        ApiResponse second = await host.Client.PostAsync(
            Devices,
            new CreateDeviceRequest("switch", "10.0.0.2"),
            Cancellation);

        second.Status.Should().Be(201);
    }

    [Fact]
    public async Task Create_AnAddressThatIsNotAnAddress_Returns400()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse rejected = await host.Client.PostAsync(
            Devices,
            new CreateDeviceRequest("core-sw-01", "not-an-address"),
            Cancellation);

        rejected.Status.Should().Be(400);
    }

    [Fact]
    public async Task Get_ADeviceThatExists_ReturnsIt()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateAsync(host, "core-sw-01", "10.0.0.1");

        ApiResponse read = await host.Client.GetAsync($"{Devices}/{id}", Cancellation);

        read.Status.Should().Be(200);
        Read<DeviceDetail>(read).Id.Should().Be(id);
    }

    [Fact]
    public async Task Get_ADeviceThatDoesNotExist_Returns404()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse read = await host.Client.GetAsync($"{Devices}/{Guid.CreateVersion7()}", Cancellation);

        read.Status.Should().Be(404);
        read.Member("code").Should().Be("device.not-found");
    }

    [Fact]
    public async Task Update_AValidChange_ReplacesEveryMemberIncludingTheOnesOmitted()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateAsync(host, "core-sw-01", "10.0.0.1", notes: "Rack 4");

        ApiResponse updated = await host.Client.PutAsync(
            $"{Devices}/{id}",
            new UpdateDeviceRequest("core-sw-01a", "10.0.0.9", DeviceVendor.AristaEos),
            Cancellation);

        updated.Status.Should().Be(200);

        DeviceDetail device = Read<DeviceDetail>(updated);

        device.Hostname.Should().Be("core-sw-01a");
        device.PrimaryIpAddress.Should().Be("10.0.0.9");
        device.Vendor.Should().Be(DeviceVendor.AristaEos);

        // A PUT describes the device as it should now be. Notes were not sent, so they are gone.
        device.Notes.Should().BeNull();
    }

    [Fact]
    public async Task Update_ToAnAddressAnotherLiveDeviceHolds_Returns409()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await CreateAsync(host, "core-sw-01", "10.0.0.1");
        Guid second = await CreateAsync(host, "core-sw-02", "10.0.0.2");

        ApiResponse conflict = await host.Client.PutAsync(
            $"{Devices}/{second}",
            new UpdateDeviceRequest("core-sw-02", "10.0.0.1"),
            Cancellation);

        conflict.Status.Should().Be(409);
    }

    [Fact]
    public async Task Update_LeavingItsOwnAddressUnchanged_IsNotAConflictWithItself()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateAsync(host, "core-sw-01", "10.0.0.1");

        ApiResponse updated = await host.Client.PutAsync(
            $"{Devices}/{id}",
            new UpdateDeviceRequest("core-sw-01-renamed", "10.0.0.1"),
            Cancellation);

        updated.Status.Should().Be(200);
    }

    [Fact]
    public async Task Delete_ADevice_Returns204AndTheDeviceLeavesEveryRead()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateAsync(host, "core-sw-01", "10.0.0.1");

        ApiResponse deleted = await host.Client.DeleteAsync($"{Devices}/{id}", Cancellation);

        deleted.Status.Should().Be(204);

        (await host.Client.GetAsync($"{Devices}/{id}", Cancellation)).Status.Should().Be(404);

        ApiResponse list = await host.Client.GetAsync(Devices, Cancellation);
        list.Json.GetProperty("items").GetArrayLength().Should().Be(0);

        // Soft delete: the row is still there, so telemetry and audit rows naming it resolve.
        (await host.DeletedAtAsync(id, Cancellation)).Should().NotBeNull();
    }

    [Fact]
    public async Task Delete_ThenCreateAtTheSameAddress_Succeeds()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateAsync(host, "core-sw-01", "10.0.0.1");

        await host.Client.DeleteAsync($"{Devices}/{id}", Cancellation);

        // The unique index is partial on deleted_at IS NULL, so removing a device releases its
        // address for the replacement that is about to be racked.
        ApiResponse replacement = await host.Client.PostAsync(
            Devices,
            new CreateDeviceRequest("core-sw-01", "10.0.0.1"),
            Cancellation);

        replacement.Status.Should().Be(201);
    }

    [Fact]
    public async Task Delete_ADeviceAlreadyDeleted_Returns404()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateAsync(host, "core-sw-01", "10.0.0.1");

        await host.Client.DeleteAsync($"{Devices}/{id}", Cancellation);

        (await host.Client.DeleteAsync($"{Devices}/{id}", Cancellation)).Status.Should().Be(404);
    }

    [Fact]
    public async Task EveryMutation_WritesOneOutboxRow_InTheSameTransaction()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateAsync(host, "core-sw-01", "10.0.0.1");

        await host.Client.PutAsync(
            $"{Devices}/{id}",
            new UpdateDeviceRequest("core-sw-01", "10.0.0.2"),
            Cancellation);

        await host.Client.DeleteAsync($"{Devices}/{id}", Cancellation);

        IReadOnlyList<string> events = await host.OutboxEventNamesAsync(Cancellation);

        events.Should().Equal(
            typeof(DeviceCreated).FullName,
            typeof(DeviceUpdated).FullName,
            typeof(DeviceRemoved).FullName);
    }

    [Fact]
    public async Task Update_ThatMovesTheAddress_PublishesBothTheNewAndThePreviousOne()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateAsync(host, "core-sw-01", "10.0.0.1");

        await host.Client.PutAsync(
            $"{Devices}/{id}",
            new UpdateDeviceRequest("core-sw-01", "10.0.0.2"),
            Cancellation);

        // A subscriber holding a cache keyed by address needs the old key to evict it, and WP-1.8
        // is the first thing that will.
        string payload = (await host.LastOutboxPayloadAsync(Cancellation))!;
        JsonElement published = JsonDocument.Parse(payload).RootElement;

        published.GetProperty("PrimaryIpAddress").GetString().Should().Be("10.0.0.2");
        published.GetProperty("PreviousPrimaryIpAddress").GetString().Should().Be("10.0.0.1");
    }

    [Fact]
    public async Task ACreateThatConflicts_WritesNoOutboxRow()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await CreateAsync(host, "core-sw-01", "10.0.0.1");

        await host.Client.PostAsync(Devices, new CreateDeviceRequest("core-sw-02", "10.0.0.1"), Cancellation);

        // One create succeeded, one was refused. If the event were written outside the domain
        // transaction there would be two rows here.
        (await host.OutboxEventNamesAsync(Cancellation)).Should().ContainSingle();
    }

    [Fact]
    public async Task EveryMutation_WritesAnAuditRowNamingTheDevice()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateAsync(host, "core-sw-01", "10.0.0.1");

        await host.Client.PutAsync(
            $"{Devices}/{id}",
            new UpdateDeviceRequest("core-sw-01", "10.0.0.2"),
            Cancellation);

        await host.Client.DeleteAsync($"{Devices}/{id}", Cancellation);

        IReadOnlyList<AuditRow> rows = await host.AuditRowsAsync(Cancellation);

        IReadOnlyList<AuditRow> deviceRows =
            [.. rows.Where(row => row.TargetType == "device" && row.TargetId == id.ToString())];

        deviceRows.Select(row => row.Action).Should().Equal(
            "inventory.device-create",
            "inventory.device-update",
            "inventory.device-delete");

        deviceRows.Should().OnlyContain(row => row.Outcome == AuditOutcome.Succeeded);
    }

    [Fact]
    public async Task AReadEndpoint_WritesNoAuditRow()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateAsync(host, "core-sw-01", "10.0.0.1");

        await host.Client.GetAsync($"{Devices}/{id}", Cancellation);
        await host.Client.GetAsync(Devices, Cancellation);

        IReadOnlyList<AuditRow> rows = await host.AuditRowsAsync(Cancellation);

        // SPEC.md §5 records every state-changing call. A GET changes nothing, and an audit log
        // that also records reads is one nobody can find a change in.
        rows.Where(row => row.TargetType == "device").Should().ContainSingle();
    }

    private static async Task<Guid> CreateAsync(
        InventoryHost host,
        string hostname,
        string address,
        string? notes = null)
    {
        ApiResponse created = await host.Client.PostAsync(
            Devices,
            new CreateDeviceRequest(hostname, address, Notes: notes),
            Cancellation);

        created.Status.Should().Be(201, "the fixture device has to exist for the test to mean anything");

        return Read<DeviceDetail>(created).Id;
    }

    private static T Read<T>(ApiResponse response) =>
        JsonSerializer.Deserialize<T>(response.Body, JsonOptions)!;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
}
