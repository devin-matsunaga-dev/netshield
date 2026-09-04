using System.Text.Json;

using FluentAssertions;

using NetShield.Contracts.Inventory;
using NetShield.Contracts.Inventory.Events;

using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

using NetShield.Platform.Auditing;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// The many-to-many between devices and credential profiles, replaced as a whole set.
/// </summary>
public sealed class DeviceCredentialAssignmentTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Devices = "/api/v1/devices";

    private const string Profiles = "/api/v1/credential-profiles";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Set_AssignsTheProfiles_AndTheListReadsThemBack()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid device = await CreateDeviceAsync(host, "core-sw-01", "10.0.0.1");
        Guid snmp = await CredentialProfileCrudTests.CreateAsync(host, "Core SNMP", Cancellation);
        Guid community = await CredentialProfileCrudTests.CreateCommunityAsync(host, "Core v2c", Cancellation);

        ApiResponse set = await host.Client.PutAsync(
            $"{Devices}/{device}/credential-profiles",
            new SetDeviceCredentialProfilesRequest([snmp, community]),
            Cancellation);

        set.Status.Should().Be(200);

        ApiResponse listed = await host.Client.GetAsync($"{Devices}/{device}/credential-profiles", Cancellation);

        listed.Status.Should().Be(200);
        Summaries(listed).Select(profile => profile.Id).Should().BeEquivalentTo([snmp, community]);
    }

    /// <summary>
    /// A profile knows how many live devices it covers, which is what a UI shows before somebody
    /// deletes one.
    /// </summary>
    [Fact]
    public async Task Assigning_RaisesTheProfilesDeviceCount()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid device = await CreateDeviceAsync(host, "core-sw-01", "10.0.0.1");
        Guid profile = await CredentialProfileCrudTests.CreateAsync(host, "Core SNMP", Cancellation);

        await host.Client.PutAsync(
            $"{Devices}/{device}/credential-profiles",
            new SetDeviceCredentialProfilesRequest([profile]),
            Cancellation);

        CredentialProfileCrudTests
            .Read(await host.Client.GetAsync($"{Profiles}/{profile}", Cancellation))
            .DeviceCount.Should().Be(1);
    }

    /// <summary>
    /// A soft-deleted device no longer counts. The assignment row survives the delete, and
    /// reporting it would say the credential is in use when nothing will use it.
    /// </summary>
    [Fact]
    public async Task RemovingTheDevice_LowersTheProfilesDeviceCount()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid device = await CreateDeviceAsync(host, "core-sw-01", "10.0.0.1");
        Guid profile = await CredentialProfileCrudTests.CreateAsync(host, "Core SNMP", Cancellation);

        await host.Client.PutAsync(
            $"{Devices}/{device}/credential-profiles",
            new SetDeviceCredentialProfilesRequest([profile]),
            Cancellation);

        await host.Client.DeleteAsync($"{Devices}/{device}", Cancellation);

        CredentialProfileCrudTests
            .Read(await host.Client.GetAsync($"{Profiles}/{profile}", Cancellation))
            .DeviceCount.Should().Be(0);
    }

    [Fact]
    public async Task Set_WithAnEmptyList_UnassignsEverything()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid device = await CreateDeviceAsync(host, "core-sw-01", "10.0.0.1");
        Guid profile = await CredentialProfileCrudTests.CreateAsync(host, "Core SNMP", Cancellation);

        await host.Client.PutAsync(
            $"{Devices}/{device}/credential-profiles",
            new SetDeviceCredentialProfilesRequest([profile]),
            Cancellation);

        await host.Client.PutAsync(
            $"{Devices}/{device}/credential-profiles",
            new SetDeviceCredentialProfilesRequest([]),
            Cancellation);

        Summaries(await host.Client.GetAsync($"{Devices}/{device}/credential-profiles", Cancellation))
            .Should().BeEmpty();
    }

    /// <summary>Sent twice is sent once: a caller repeating an id describes the same assignment.</summary>
    [Fact]
    public async Task Set_WithADuplicateId_AssignsItOnce()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid device = await CreateDeviceAsync(host, "core-sw-01", "10.0.0.1");
        Guid profile = await CredentialProfileCrudTests.CreateAsync(host, "Core SNMP", Cancellation);

        ApiResponse set = await host.Client.PutAsync(
            $"{Devices}/{device}/credential-profiles",
            new SetDeviceCredentialProfilesRequest([profile, profile]),
            Cancellation);

        set.Status.Should().Be(200);
        Summaries(set).Should().ContainSingle();
    }

    /// <summary>
    /// A profile that does not exist is refused rather than skipped. Answering 200 to a request
    /// that did not happen is the failure mode that puts a device in production with no
    /// credential and nobody the wiser.
    /// </summary>
    [Fact]
    public async Task Set_WithAProfileThatDoesNotExist_Returns404()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid device = await CreateDeviceAsync(host, "core-sw-01", "10.0.0.1");

        ApiResponse refused = await host.Client.PutAsync(
            $"{Devices}/{device}/credential-profiles",
            new SetDeviceCredentialProfilesRequest([Guid.CreateVersion7()]),
            Cancellation);

        refused.Status.Should().Be(404);
        refused.Member("code").Should().Be("credential-profile.not-found");
    }

    [Fact]
    public async Task Set_WithAProfileThatHasBeenRemoved_Returns404()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid device = await CreateDeviceAsync(host, "core-sw-01", "10.0.0.1");
        Guid profile = await CredentialProfileCrudTests.CreateAsync(host, "Core SNMP", Cancellation);

        await host.Client.DeleteAsync($"{Profiles}/{profile}", Cancellation);

        ApiResponse refused = await host.Client.PutAsync(
            $"{Devices}/{device}/credential-profiles",
            new SetDeviceCredentialProfilesRequest([profile]),
            Cancellation);

        refused.Status.Should().Be(404);
    }

    [Fact]
    public async Task Set_ForADeviceThatDoesNotExist_Returns404()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse refused = await host.Client.PutAsync(
            $"{Devices}/{Guid.CreateVersion7()}/credential-profiles",
            new SetDeviceCredentialProfilesRequest([]),
            Cancellation);

        refused.Status.Should().Be(404);
        refused.Member("code").Should().Be("device.not-found");
    }

    /// <summary>
    /// Removing a profile takes its assignments with it, and tells every device that just lost a
    /// credential — otherwise scheduling would go on queuing work against a credential nothing
    /// will resolve.
    /// </summary>
    [Fact]
    public async Task RemovingAProfile_UnassignsItAndPublishesTheChangeForEachDevice()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid device = await CreateDeviceAsync(host, "core-sw-01", "10.0.0.1");
        Guid profile = await CredentialProfileCrudTests.CreateAsync(host, "Core SNMP", Cancellation);

        await host.Client.PutAsync(
            $"{Devices}/{device}/credential-profiles",
            new SetDeviceCredentialProfilesRequest([profile]),
            Cancellation);

        await host.Client.DeleteAsync($"{Profiles}/{profile}", Cancellation);

        Summaries(await host.Client.GetAsync($"{Devices}/{device}/credential-profiles", Cancellation))
            .Should().BeEmpty();

        (await host.OutboxEventNamesAsync(Cancellation)).Should().EndWith(
        [
            typeof(CredentialProfileRemoved).FullName!,
            typeof(DeviceCredentialProfilesChanged).FullName!
        ]);

        JsonElement published = JsonDocument.Parse((await host.LastOutboxPayloadAsync(Cancellation))!).RootElement;

        published.GetProperty("DeviceId").GetGuid().Should().Be(device);
        published.GetProperty("CredentialProfileIds").GetArrayLength().Should().Be(0);
    }

    /// <summary>
    /// A PUT that changes nothing writes nothing. A subscriber rebuilding a cache on every event
    /// would otherwise rebuild it every time somebody saved an unchanged form.
    /// </summary>
    [Fact]
    public async Task Set_ThatChangesNothing_PublishesNoEvent()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid device = await CreateDeviceAsync(host, "core-sw-01", "10.0.0.1");
        Guid profile = await CredentialProfileCrudTests.CreateAsync(host, "Core SNMP", Cancellation);

        await host.Client.PutAsync(
            $"{Devices}/{device}/credential-profiles",
            new SetDeviceCredentialProfilesRequest([profile]),
            Cancellation);

        int before = (await host.OutboxEventNamesAsync(Cancellation)).Count;

        await host.Client.PutAsync(
            $"{Devices}/{device}/credential-profiles",
            new SetDeviceCredentialProfilesRequest([profile]),
            Cancellation);

        (await host.OutboxEventNamesAsync(Cancellation)).Count.Should().Be(before);
    }

    /// <summary>The row is about the device, because the device is what changed.</summary>
    [Fact]
    public async Task Set_WritesAnAuditRowAgainstTheDevice_NamingTheProfilesByIdAndNotByMaterial()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid device = await CreateDeviceAsync(host, "core-sw-01", "10.0.0.1");
        Guid profile = await CredentialProfileCrudTests.CreateAsync(host, "Core SNMP", Cancellation);

        await host.Client.PutAsync(
            $"{Devices}/{device}/credential-profiles",
            new SetDeviceCredentialProfilesRequest([profile]),
            Cancellation);

        IReadOnlyList<AuditRow> rows = await host.AuditRowsAsync(Cancellation);

        rows.Should().Contain(row =>
            row.Action == "inventory.device-credentials-set"
            && row.TargetType == "device"
            && row.TargetId == device.ToString()
            && row.Outcome == AuditOutcome.Succeeded);
    }

    private static IReadOnlyList<CredentialProfileSummary> Summaries(ApiResponse response) =>
        JsonSerializer.Deserialize<IReadOnlyList<CredentialProfileSummary>>(
            response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static async Task<Guid> CreateDeviceAsync(InventoryHost host, string hostname, string address)
    {
        ApiResponse created = await host.Client.PostAsync(
            Devices,
            new CreateDeviceRequest(hostname, address),
            TestContext.Current.CancellationToken);

        created.Status.Should().Be(201);

        return created.Json.GetProperty("id").GetGuid();
    }
}
