using FluentAssertions;

using NetShield.Contracts.Inventory;

using NetShield.IntegrationTests.Collector;
using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// Which of a device's SNMP credential profiles a walk is run with.
/// </summary>
/// <remarks>
/// WP-1.5 wrote the order into <c>QueueDeviceWalkHandler</c> — SNMPv3, then SNMPv2c — and
/// recorded that WP-1.6 owned making it configurable. This is that: the same default, now read
/// from <c>Inventory:Discovery:CredentialKindOrder</c>, which is the "credential profile order"
/// the WP-1.6 entry names.
/// </remarks>
public sealed class SnmpCredentialOrderTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ByDefaultAVersionThreeProfileIsPreferredOverAVersionTwoOne()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        (Guid deviceId, Guid v2c, Guid v3) = await BothProfilesAsync(host, Cancellation);

        Guid chosen = await ChosenProfileAsync(host, deviceId, Cancellation);

        chosen.Should().Be(v3);
        chosen.Should().NotBe(v2c);
    }

    [Fact]
    public async Task ReversingTheConfiguredOrderChoosesTheOtherProfile()
    {
        // An installation that has not deployed v3 everywhere can say which it wants used,
        // without the choice moving anywhere a caller can reach.
        await using InventoryHost host = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            discovery: new DiscoverySettings
            {
                CredentialKindOrder = [CredentialKind.SnmpV2c, CredentialKind.SnmpV3]
            });

        (Guid deviceId, Guid v2c, _) = await BothProfilesAsync(host, Cancellation);

        (await ChosenProfileAsync(host, deviceId, Cancellation)).Should().Be(v2c);
    }

    [Fact]
    public async Task AKindLeftOutOfTheOrderIsNeverChosen()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            discovery: new DiscoverySettings { CredentialKindOrder = [CredentialKind.SnmpV3] });

        (Guid deviceId, _, Guid v3) = await BothProfilesAsync(host, Cancellation);

        (await ChosenProfileAsync(host, deviceId, Cancellation)).Should().Be(v3);
    }

    [Fact]
    public async Task ADeviceWhoseOnlyProfileIsOfAnExcludedKindCannotBeWalked()
    {
        // The honest answer: with that configuration the device has no credential NetShield will
        // use, which is what the endpoint says.
        await using InventoryHost host = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            discovery: new DiscoverySettings { CredentialKindOrder = [CredentialKind.SnmpV3] });

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        ApiResponse refused = await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation);

        refused.Status.Should().Be(409);
        refused.Json.GetProperty("code").GetString().Should().Be("discovery.no-snmp-credential");
    }

    [Fact]
    public async Task ARevokedProfileIsNeverChosenEvenWhenItsKindComesFirst()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        (Guid deviceId, Guid v2c, Guid v3) = await BothProfilesAsync(host, Cancellation);

        (await host.Client.DeleteAsync($"/api/v1/credential-profiles/{v3}", Cancellation))
            .Status.Should().Be(204);

        // Deleting a profile hard-deletes its assignments, so the device is left with the v2c one.
        (await ChosenProfileAsync(host, deviceId, Cancellation)).Should().Be(v2c);
    }

    /// <summary>A device with one SNMPv2c profile and one SNMPv3 profile assigned, in that order.</summary>
    private static async Task<(Guid DeviceId, Guid V2c, Guid V3)> BothProfilesAsync(
        InventoryHost host,
        CancellationToken cancellationToken)
    {
        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(
            host,
            "switch-01",
            "10.20.0.1",
            cancellationToken);

        Guid v2c = await CollectorFixtures.CreateCredentialProfileAsync(
            host,
            "Estate v2c",
            "fixture-community",
            cancellationToken);

        ApiResponse created = await host.Client.PostAsync(
            "/api/v1/credential-profiles",
            new CreateCredentialProfileRequest(
                "Estate v3",
                CredentialKind.SnmpV3,
                new CredentialMaterial(
                    AuthPassword: "fixture-auth-password",
                    PrivacyPassword: "fixture-privacy-password"),
                Username: "netshield",
                AuthAlgorithm: SnmpAuthAlgorithm.Sha256,
                PrivacyAlgorithm: SnmpPrivacyAlgorithm.Aes128),
            cancellationToken);

        created.Status.Should().Be(201);

        Guid v3 = created.Json.GetProperty("id").GetGuid();

        await DiscoveryFixtures.AssignAsync(host, deviceId, [v2c, v3], cancellationToken);

        return (deviceId, v2c, v3);
    }

    /// <summary>
    /// The profile the queued walk names, read off the job row — the route deliberately does not
    /// say which credential it chose (WP-1.5).
    /// </summary>
    private static async Task<Guid> ChosenProfileAsync(
        InventoryHost host,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        ApiResponse queued = await DiscoveryFixtures.RequestWalkAsync(host, deviceId, cancellationToken);

        queued.Status.Should().Be(202);

        Guid jobId = queued.Json.GetProperty("jobId").GetGuid();

        CollectorJobRow job = await host.JobAsync(jobId, cancellationToken);

        job.CredentialProfileId.Should().NotBeNull();

        return job.CredentialProfileId!.Value;
    }
}
