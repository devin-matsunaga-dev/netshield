using System.Text.Json;

using FluentAssertions;

using NetShield.Contracts.Collector;

using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Inventory;
using NetShield.IntegrationTests.Platform;

using NetShield.Inventory.Collector;
using NetShield.Inventory.Collector.Handlers;

using NetShield.Platform.Auditing;

namespace NetShield.IntegrationTests.Collector;

/// <summary>
/// The decrypt path in anger: a stored credential becomes plaintext exactly once, on a lease, and
/// the release is recorded.
/// </summary>
/// <remarks>
/// WP-1.2 built <c>ICredentialResolver</c> with no production call site and recorded that gap in
/// STATUS.md. This is the package that closes it, and these are the tests that say so.
/// </remarks>
public sealed class CollectorCredentialDeliveryTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Jobs = "/internal/collector/jobs?collector=collector-test";
    private const string Community = "fixture-community-9f2a";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lease_AJobNamingAProfile_CarriesTheOpenedCredential()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        (Guid deviceId, Guid profileId) = await SeedAsync(host);

        await host.EnqueueAsync(
            new NewCollectorJob(CollectorJobKind.Poll, deviceId, profileId),
            Cancellation);

        ApiResponse leased = await host.Collector.GetAsync(Jobs, Cancellation);

        JsonElement credential = leased.Json.GetProperty("jobs")[0].GetProperty("credential");

        credential.GetProperty("credentialProfileId").GetGuid().Should().Be(profileId);
        credential.GetProperty("kind").GetString().Should().Be("SnmpV2c");
        credential.GetProperty("material").GetProperty("community").GetString().Should().Be(Community);
    }

    [Fact]
    public async Task Lease_ThatReleasedACredential_WritesOneAuditRowNamingTheProfile()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        (Guid deviceId, Guid profileId) = await SeedAsync(host);

        await host.EnqueueAsync(
            new NewCollectorJob(CollectorJobKind.Poll, deviceId, profileId),
            Cancellation);

        await host.Collector.GetAsync(Jobs, Cancellation);

        IReadOnlyList<AuditRow> released = [.. (await host.AuditRowsAsync(Cancellation))
            .Where(row => row.Action == LeaseCollectorJobsHandler.CredentialReleasedAction)];

        released.Should().ContainSingle();
        released[0].TargetType.Should().Be("credential-profile");
        released[0].TargetId.Should().Be(profileId.ToString());
        released[0].Outcome.Should().Be(AuditOutcome.Succeeded);
    }

    [Fact]
    public async Task Lease_WithNoCredential_WritesNoReleaseRow()
    {
        // The heartbeat and the result batch write nothing, and neither does a lease that handed
        // no credential over. What is audited is the security-relevant act, not the traffic.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await host.EnqueueAsync(new NewCollectorJob(CollectorJobKind.Discover), Cancellation);

        await host.Collector.GetAsync(Jobs, Cancellation);

        (await host.AuditRowsAsync(Cancellation)).Should()
            .NotContain(row => row.Action == LeaseCollectorJobsHandler.CredentialReleasedAction);
    }

    [Fact]
    public async Task TheAuditRow_DoesNotCarryTheMaterial()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        (Guid deviceId, Guid profileId) = await SeedAsync(host);

        await host.EnqueueAsync(
            new NewCollectorJob(CollectorJobKind.Poll, deviceId, profileId),
            Cancellation);

        await host.Collector.GetAsync(Jobs, Cancellation);

        IReadOnlyList<string> snapshots = await host.AuditSnapshotsAsync(
            LeaseCollectorJobsHandler.CredentialReleasedAction,
            Cancellation);

        snapshots.Should().NotBeEmpty();
        snapshots.Should().NotContain(snapshot => snapshot.Contains(Community, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Lease_DoesNotWriteTheCredentialToAnyLogLine()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        (Guid deviceId, Guid profileId) = await SeedAsync(host);

        await host.EnqueueAsync(
            new NewCollectorJob(CollectorJobKind.Poll, deviceId, profileId),
            Cancellation);

        await host.Collector.GetAsync(Jobs, Cancellation);

        host.RecordedLogs().Should()
            .NotContain(line => line.Contains(Community, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Lease_AJobWhoseProfileHasBeenRevoked_FailsItAndReleasesNothing()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        (Guid deviceId, Guid profileId) = await SeedAsync(host);

        Guid jobId = await host.EnqueueAsync(
            new NewCollectorJob(CollectorJobKind.Poll, deviceId, profileId),
            Cancellation);

        (await host.Client.DeleteAsync($"/api/v1/credential-profiles/{profileId}", Cancellation))
            .Status.Should().Be(204);

        ApiResponse leased = await host.Collector.GetAsync(Jobs, Cancellation);

        leased.Json.GetProperty("jobs").GetArrayLength().Should()
            .Be(0, "a revoked credential must not keep reaching a collector");

        CollectorJobRow row = await host.JobAsync(jobId, Cancellation);

        row.Status.Should().Be(CollectorJobStatus.Failed);
        row.Detail.Should().Contain("no longer available");

        (await host.AuditRowsAsync(Cancellation)).Should()
            .NotContain(audit => audit.Action == LeaseCollectorJobsHandler.CredentialReleasedAction);
    }

    private static async Task<(Guid DeviceId, Guid ProfileId)> SeedAsync(InventoryHost host)
    {
        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(host, "core-sw-01", "10.0.0.1", Cancellation);

        Guid profileId = await CollectorFixtures.CreateCredentialProfileAsync(
            host,
            "Core read-only",
            Community,
            Cancellation);

        return (deviceId, profileId);
    }
}
