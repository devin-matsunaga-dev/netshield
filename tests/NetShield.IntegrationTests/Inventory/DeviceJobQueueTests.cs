using System.Text.Json;

using FluentAssertions;

using NetShield.Contracts.Collector;
using NetShield.Contracts.Identity;

using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// The device's collector queue: what it shows, what it refuses to show, and what cancelling a
/// job does and does not do.
/// </summary>
/// <remarks>
/// The queue is the first read of <c>collector_jobs</c> outside the collector's own contract.
/// Cancelling is the seam WP-1.3 named and left uncut — so the tests that matter most here are
/// the ones about what a cancelled job is: not leasable, not deleted, and reachable only from
/// <c>Pending</c>.
/// </remarks>
public sealed class DeviceJobQueueTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Jobs = "/internal/collector/jobs?collector=collector-test&capacity=8";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string QueueOf(Guid deviceId) => $"/api/v1/devices/{deviceId}/jobs";

    private static string CancelOf(Guid deviceId, Guid jobId) =>
        $"/api/v1/devices/{deviceId}/jobs/{jobId}/cancel";

    [Fact]
    public async Task TheQueue_ShowsTheJobAWalkQueued()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);
        await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation);

        ApiResponse queue = await host.Client.GetAsync(QueueOf(deviceId), Cancellation);

        queue.Status.Should().Be(200);

        JsonElement row = queue.Json.GetProperty("items")[0];

        row.GetProperty("kind").GetString().Should().Be(nameof(CollectorJobKind.Discover));
        row.GetProperty("status").GetString().Should().Be(nameof(CollectorJobStatus.Pending));
        row.GetProperty("attempts").GetInt32().Should().Be(0);

        // The walk discriminator is what makes a queue of `Discover` rows readable: five
        // different reads share one kind, and "which walk is this" is the question being asked.
        row.GetProperty("walk").GetString().Should().Be("snmp");

        // Cancellable is computed on the server so the screen and the endpoint cannot disagree.
        row.GetProperty("cancellable").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task TheQueue_TellsNobodyWhichCredentialAJobRunsUnder()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);
        await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation);

        ApiResponse queue = await host.Client.GetAsync(QueueOf(deviceId), Cancellation);

        // Which credential a job opens is a statement about which accounts NetShield holds
        // passwords for, which WP-1.2 gated behind CredentialsManage. This route is InventoryRead.
        queue.Body.Should().NotContain("credentialProfileId");

        // The lease token is the capability to submit a result for the job, and the result is an
        // unbounded blob belonging to whichever package owns the kind. Neither belongs on a queue.
        queue.Body.Should().NotContain("leaseToken");
        queue.Body.Should().NotContain("\"result\"");
    }

    [Fact]
    public async Task TheQueue_OfADeviceNothingHasBeenQueuedFor_IsEmptyRatherThanMissing()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        ApiResponse queue = await host.Client.GetAsync(QueueOf(deviceId), Cancellation);

        // "Nothing has been asked of this device" and "there is no such device" are different
        // facts and only one of them is a mistake in the request.
        queue.Status.Should().Be(200);
        queue.Json.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task TheQueue_OfADeviceThatIsNotThere_IsA404()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        (await host.Client.GetAsync(QueueOf(Guid.NewGuid()), Cancellation)).Status.Should().Be(404);
    }

    [Fact]
    public async Task TheQueue_CanBeNarrowedToOneStatus()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);
        await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation);

        ApiResponse pending = await host.Client.GetAsync(
            $"{QueueOf(deviceId)}?status=Pending",
            Cancellation);

        ApiResponse succeeded = await host.Client.GetAsync(
            $"{QueueOf(deviceId)}?status=Succeeded",
            Cancellation);

        pending.Json.GetProperty("items").GetArrayLength().Should().Be(1);
        succeeded.Json.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task CancellingAPendingJob_MovesItToCancelledAndKeepsTheRow()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);
        await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation);

        Guid jobId = (await host.JobIdsForAsync(deviceId, Cancellation))[0];

        ApiResponse cancelled = await host.Client.PostAsync(CancelOf(deviceId, jobId), Cancellation);

        cancelled.Status.Should().Be(200);
        cancelled.Json.GetProperty("status").GetString()
            .Should().Be(nameof(CollectorJobStatus.Cancelled));
        cancelled.Json.GetProperty("cancellable").GetBoolean().Should().BeFalse();

        // Cancelled, not deleted: what was queued and then withdrawn is part of the record.
        CollectorJobRow row = await host.JobAsync(jobId, Cancellation);

        row.Status.Should().Be(CollectorJobStatus.Cancelled);
        row.LeaseToken.Should().BeNull();
    }

    [Fact]
    public async Task ACancelledJob_IsNeverLeasedToACollector()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);
        await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation);

        Guid jobId = (await host.JobIdsForAsync(deviceId, Cancellation))[0];

        await host.Client.PostAsync(CancelOf(deviceId, jobId), Cancellation);

        // The whole point. The claim query selects Pending, or Leased with an expired lease, so
        // a cancelled job stops being a row anything returns — no change to the collector
        // contract was needed to make that true.
        ApiResponse leased = await host.Collector.GetAsync(Jobs, Cancellation);

        leased.Status.Should().Be(200);
        leased.Json.GetProperty("jobs").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task CancellingAJob_FreesTheDeviceForAnotherWalk()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);
        await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation);

        // One outstanding Discover per device, so the second is refused while the first is queued.
        (await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation))
            .Status.Should().Be(409);

        Guid jobId = (await host.JobIdsForAsync(deviceId, Cancellation))[0];

        await host.Client.PostAsync(CancelOf(deviceId, jobId), Cancellation);

        // This is the operator value of the whole feature: a job that will never run otherwise
        // blocks every future walk of that device, and there was no way to clear it.
        (await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation))
            .Status.Should().Be(202);
    }

    [Fact]
    public async Task CancellingALeasedJob_IsRefusedWith409()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);
        await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation);

        Guid jobId = (await host.JobIdsForAsync(deviceId, Cancellation))[0];

        await host.Collector.GetAsync(Jobs, Cancellation);

        ApiResponse refused = await host.Client.PostAsync(CancelOf(deviceId, jobId), Cancellation);

        // A collector is running it and does not know anybody changed their mind. Letting a read
        // that is already open finish is a better end than a result the API then refuses.
        refused.Status.Should().Be(409);
        refused.Member("code").Should().Be("collector.job-not-cancellable");

        (await host.JobAsync(jobId, Cancellation)).Status.Should().Be(CollectorJobStatus.Leased);
    }

    [Fact]
    public async Task CancellingAnAlreadyCancelledJob_IsRefusedWith409()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);
        await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation);

        Guid jobId = (await host.JobIdsForAsync(deviceId, Cancellation))[0];

        await host.Client.PostAsync(CancelOf(deviceId, jobId), Cancellation);

        (await host.Client.PostAsync(CancelOf(deviceId, jobId), Cancellation))
            .Status.Should().Be(409);
    }

    [Fact]
    public async Task CancellingAJobOfAnotherDevice_IsA404()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);
        Guid other = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host,
            Cancellation,
            "switch-02",
            "10.10.0.2");

        await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation);

        Guid jobId = (await host.JobIdsForAsync(deviceId, Cancellation))[0];

        // One answer for "no such job" and for "that job is another device's": telling the two
        // apart would answer a question about a device the caller did not ask about.
        (await host.Client.PostAsync(CancelOf(other, jobId), Cancellation)).Status.Should().Be(404);
    }

    [Fact]
    public async Task CancellingAJob_IsAudited()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);
        await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation);

        Guid jobId = (await host.JobIdsForAsync(deviceId, Cancellation))[0];

        await host.Client.PostAsync(CancelOf(deviceId, jobId), Cancellation);

        IReadOnlyList<AuditRow> rows = await host.AuditRowsAsync(Cancellation);

        // SPEC.md §5: every state-changing call is recorded with actor and target.
        rows.Should().Contain(row =>
            row.Action == "inventory.device-job-cancel" && row.TargetId == deviceId.ToString());
    }

    [Theory]
    [InlineData(UserRole.Administrator)]
    [InlineData(UserRole.Operator)]
    [InlineData(UserRole.Analyst)]
    [InlineData(UserRole.ReadOnly)]
    public async Task ReadingTheQueue_IsPermittedForEveryRole(UserRole role)
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        // Created as the administrator the host starts as, then read as the role under test —
        // three of the four cannot create a device, and that is not what this asserts.
        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await host.SignInAsync(role, Cancellation);

        // Reading what is queued for a device is reading about the device, so it rides on
        // InventoryRead like the rest of the detail screen.
        (await host.Client.GetAsync(QueueOf(deviceId), Cancellation)).Status.Should().Be(200);
    }

    [Theory]
    [InlineData(UserRole.Analyst)]
    [InlineData(UserRole.ReadOnly)]
    public async Task Cancelling_WithoutDiscoveryRun_IsRefusedWith403(UserRole role)
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);
        await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation);

        Guid jobId = (await host.JobIdsForAsync(deviceId, Cancellation))[0];

        await host.SignInAsync(role, Cancellation);

        // Stopping NetShield from reading a device outside its schedule is the same authority as
        // starting it: DiscoveryRun, which neither of these roles holds.
        (await host.Client.PostAsync(CancelOf(deviceId, jobId), Cancellation))
            .Status.Should().Be(403);

        (await host.JobAsync(jobId, Cancellation)).Status.Should().Be(CollectorJobStatus.Pending);
    }
}
