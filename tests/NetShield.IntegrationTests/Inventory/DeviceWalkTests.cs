using System.Text.Json;

using FluentAssertions;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.IntegrationTests.Collector;
using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// The on-demand fingerprint route: who may ask for a walk, when it is refused, and which
/// credential the queued job is given.
/// </summary>
/// <remarks>
/// It queues a job and answers <c>202</c>; it does not talk to a device. Everything that happens
/// after the collector leases it is <see cref="DeviceFingerprintTests"/>.
/// </remarks>
public sealed class DeviceWalkTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AWalkOfADeviceWithAnSnmpCredential_IsQueuedAndAccepted()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        ApiResponse queued = await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation);

        queued.Status.Should().Be(202);
        queued.Json.GetProperty("deviceId").GetGuid().Should().Be(deviceId);
        queued.Json.GetProperty("jobId").GetGuid().Should().NotBeEmpty();

        // Deliberately absent: which credential profile was chosen is not told to a caller who
        // holds DiscoveryRun and may hold nothing about credentials at all.
        queued.Body.Should().NotContain("credentialProfileId");

        (await host.JobIdsForAsync(deviceId, Cancellation)).Should().ContainSingle();
    }

    [Fact]
    public async Task TheQueuedJob_CarriesTheSnmpWalkParametersTheCollectorReads()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation);

        Guid jobId = (await host.JobIdsForAsync(deviceId, Cancellation))[0];
        string? parameters = await host.JobParametersAsync(jobId, Cancellation);

        parameters.Should().NotBeNull();

        // Read as JSON rather than as text: the column is jsonb, so Postgres decides the
        // whitespace and the key order, and only the members matter.
        using JsonDocument document = JsonDocument.Parse(parameters!);

        document.RootElement.GetProperty("walk").GetString().Should().Be("snmp");
        document.RootElement.GetProperty("maxInterfaces").GetInt32().Should().Be(512);
        document.RootElement.GetProperty("timeoutSeconds").GetDouble().Should().Be(5);
    }

    [Fact]
    public async Task AWalkOfADeviceWithNoSnmpCredential_IsRefusedWithSomethingToDoAboutIt()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(
            host, "switch-01", "10.10.0.1", Cancellation);

        ApiResponse queued = await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation);

        queued.Status.Should().Be(409);
        queued.Body.Should().Contain("discovery.no-snmp-credential");
        queued.Body.Should().Contain("Assign an SNMPv2c or SNMPv3 profile");

        (await host.JobIdsForAsync(deviceId, Cancellation)).Should().BeEmpty(
            "a job that could only fail should not be queued");
    }

    [Fact]
    public async Task AWalkOfADeviceThatAlreadyHasOneQueued_IsRefused()
    {
        // What bounds the queue at one outstanding walk per device: a person clicking twice must
        // not become two collectors walking one device and applying their results in whatever
        // order they came back.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        (await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation)).Status.Should().Be(202);

        ApiResponse again = await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation);

        again.Status.Should().Be(409);
        again.Body.Should().Contain("discovery.walk-outstanding");

        (await host.JobIdsForAsync(deviceId, Cancellation)).Should().ContainSingle();
    }

    [Fact]
    public async Task AWalkAfterTheFirstOneFinished_IsQueuedAgain()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.WalkAsync(host, deviceId, DiscoveryFixtures.WalkResult(), Cancellation);

        (await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation)).Status.Should().Be(202);
        (await host.JobIdsForAsync(deviceId, Cancellation)).Should().HaveCount(2);
    }

    [Fact]
    public async Task AWalkOfADeviceThatIsNotThere_IsNotFound()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse queued = await DiscoveryFixtures.RequestWalkAsync(host, Guid.NewGuid(), Cancellation);

        queued.Status.Should().Be(404);
        queued.Body.Should().Contain("device.not-found");
    }

    [Fact]
    public async Task AWalkOfARemovedDevice_IsNotFound()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await host.Client.DeleteAsync($"/api/v1/devices/{deviceId}", Cancellation);

        (await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation)).Status.Should().Be(404);
    }

    /// <summary>
    /// The permission is <c>DiscoveryRun</c>, which the Analyst and Read-only roles do not hold.
    /// Reading a device with a credential is not the same privilege as reading its record.
    /// </summary>
    [Theory]
    [InlineData(UserRole.Administrator, 202)]
    [InlineData(UserRole.Operator, 202)]
    [InlineData(UserRole.Analyst, 403)]
    [InlineData(UserRole.ReadOnly, 403)]
    public async Task TheRoute_IsGatedOnDiscoveryRun(UserRole role, int expected)
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await host.SignInAsync(role, Cancellation);

        (await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation)).Status.Should().Be(expected);
    }

    [Fact]
    public async Task AnSnmpV3ProfileIsPreferredOverAnSnmpV2cOne()
    {
        // Deterministic, and the secure default: v3 authenticates and encrypts, v2c does neither.
        // Configurable ordering is WP-1.6's.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(
            host, "switch-01", "10.10.0.1", Cancellation);

        // The v2c profile is created and assigned first, so that "the earliest assignment" would
        // have chosen it and only the kind preference can produce the v3 answer.
        Guid v2c = await CollectorFixtures.CreateCredentialProfileAsync(
            host, "Legacy v2c", "fixture-community", Cancellation);

        Guid v3 = await CreateV3Async(host, "Modern v3");

        await DiscoveryFixtures.AssignAsync(host, deviceId, [v2c, v3], Cancellation);

        ApiResponse queued = await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation);

        queued.Status.Should().Be(202);

        // The choice is not in the answer, so it is read off the job the answer names.
        Guid jobId = queued.Json.GetProperty("jobId").GetGuid();

        (await host.JobAsync(jobId, Cancellation)).CredentialProfileId.Should().Be(v3);
    }

    [Fact]
    public async Task AnSshProfileIsNotAnSnmpCredential()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(
            host, "switch-01", "10.10.0.1", Cancellation);

        ApiResponse profile = await host.Client.PostAsync(
            "/api/v1/credential-profiles",
            new CreateCredentialProfileRequest(
                "Console login",
                CredentialKind.SshPassword,
                new CredentialMaterial(Password: "fixture-password"),
                Username: "netshield-ro"),
            Cancellation);

        profile.Status.Should().Be(201);

        await DiscoveryFixtures.AssignAsync(
            host, deviceId, [profile.Json.GetProperty("id").GetGuid()], Cancellation);

        ApiResponse queued = await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation);

        queued.Status.Should().Be(409);
        queued.Body.Should().Contain("discovery.no-snmp-credential");
    }

    [Fact]
    public async Task ARevokedProfileIsNotOfferedToTheCollector()
    {
        // A credential an operator revoked must not keep reaching a collector — the same rule the
        // lease applies one step later (WP-1.3).
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(
            host, "switch-01", "10.10.0.1", Cancellation);

        Guid profileId = await CollectorFixtures.CreateCredentialProfileAsync(
            host, "Core SNMP", "fixture-community", Cancellation);

        await DiscoveryFixtures.AssignAsync(host, deviceId, [profileId], Cancellation);

        await host.Client.DeleteAsync($"/api/v1/credential-profiles/{profileId}", Cancellation);

        ApiResponse queued = await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation);

        queued.Status.Should().Be(409);
        queued.Body.Should().Contain("discovery.no-snmp-credential");
    }

    [Fact]
    public async Task AskingForAWalk_WritesAnAuditRow()
    {
        // A person asked a machine to go and read a device with a credential. SPEC.md §5: every
        // state-changing call is recorded with actor and target.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation);

        IReadOnlyList<AuditRow> rows = await host.AuditRowsAsync(Cancellation);

        rows.Should().ContainSingle(row =>
            row.Action == "inventory.device-walk"
            && row.TargetType == "device"
            && row.TargetId == deviceId.ToString());
    }

    [Fact]
    public async Task ARefusedWalk_IsStillAudited()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(
            host, "switch-01", "10.10.0.1", Cancellation);

        await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation);

        (await host.AuditRowsAsync(Cancellation))
            .Should().Contain(row => row.Action == "inventory.device-walk");
    }

    private static async Task<Guid> CreateV3Async(InventoryHost host, string name)
    {
        ApiResponse created = await host.Client.PostAsync(
            "/api/v1/credential-profiles",
            new CreateCredentialProfileRequest(
                name,
                CredentialKind.SnmpV3,
                new CredentialMaterial(
                    AuthPassword: "fixture-auth-password",
                    PrivacyPassword: "fixture-privacy-password"),
                Username: "netshield-ro",
                AuthAlgorithm: SnmpAuthAlgorithm.Sha256,
                PrivacyAlgorithm: SnmpPrivacyAlgorithm.Aes128),
            Cancellation);

        if (created.Status != 201)
        {
            throw new InvalidOperationException($"Could not create {name}: {created.Status} {created.Body}");
        }

        return created.Json.GetProperty("id").GetGuid();
    }
}
