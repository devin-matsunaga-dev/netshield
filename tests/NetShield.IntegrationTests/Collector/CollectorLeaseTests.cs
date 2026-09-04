using System.Text.Json;

using FluentAssertions;

using NetShield.Contracts.Collector;
using NetShield.Contracts.Collector.Events;

using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Inventory;
using NetShield.IntegrationTests.Platform;

using NetShield.Inventory.Collector;

namespace NetShield.IntegrationTests.Collector;

/// <summary>
/// The lease model: what a collector is handed, what it is not, and what happens to a job whose
/// holder never came back.
/// </summary>
public sealed class CollectorLeaseTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Jobs = "/internal/collector/jobs?collector=collector-test";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lease_WithNothingQueued_ReturnsAnEmptyBatchAndThePacing()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse leased = await host.Collector.GetAsync(Jobs, Cancellation);

        leased.Status.Should().Be(200);
        leased.Json.GetProperty("jobs").GetArrayLength().Should().Be(0);
        leased.Json.GetProperty("leaseSeconds").GetInt32().Should().Be(300);
    }

    [Fact]
    public async Task Lease_ADueJob_HandsOverTheDeviceAndATokenAndMarksItLeased()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(host, "core-sw-01", "10.0.0.1", Cancellation);
        Guid jobId = await host.EnqueueAsync(new NewCollectorJob(CollectorJobKind.Poll, deviceId), Cancellation);

        ApiResponse leased = await host.Collector.GetAsync(Jobs, Cancellation);

        JsonElement job = leased.Json.GetProperty("jobs")[0];

        job.GetProperty("jobId").GetGuid().Should().Be(jobId);
        job.GetProperty("kind").GetString().Should().Be("Poll");
        job.GetProperty("attempt").GetInt32().Should().Be(1);
        job.GetProperty("leaseToken").GetString().Should().NotBeNullOrWhiteSpace();
        job.GetProperty("device").GetProperty("hostname").GetString().Should().Be("core-sw-01");
        job.GetProperty("device").GetProperty("ipAddress").GetString().Should().Be("10.0.0.1");

        // A job with no credential profile carries no credential.
        job.GetProperty("credential").ValueKind.Should().Be(JsonValueKind.Null);

        CollectorJobRow row = await host.JobAsync(jobId, Cancellation);

        row.Status.Should().Be(CollectorJobStatus.Leased);
        row.Attempts.Should().Be(1);
        row.LeasedBy.Should().Be("collector-test");
    }

    [Fact]
    public async Task Lease_AJobAlreadyLeased_DoesNotHandItOverTwice()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await host.EnqueueAsync(new NewCollectorJob(CollectorJobKind.Discover), Cancellation);

        ApiResponse first = await host.Collector.GetAsync(Jobs, Cancellation);
        ApiResponse second = await host.Collector.GetAsync(Jobs, Cancellation);

        first.Json.GetProperty("jobs").GetArrayLength().Should().Be(1);
        second.Json.GetProperty("jobs").GetArrayLength().Should()
            .Be(0, "a live lease is exclusive — the visibility timeout is what releases it");
    }

    [Fact]
    public async Task Lease_AJobWhoseLeaseExpired_HandsItOverAgainWithANewToken()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid jobId = await host.EnqueueAsync(new NewCollectorJob(CollectorJobKind.Discover), Cancellation);

        ApiResponse first = await host.Collector.GetAsync(Jobs, Cancellation);
        string firstToken = first.Json.GetProperty("jobs")[0].GetProperty("leaseToken").GetString()!;

        await host.ExpireLeaseAsync(jobId, Cancellation);

        ApiResponse second = await host.Collector.GetAsync(Jobs, Cancellation);
        JsonElement requeued = second.Json.GetProperty("jobs")[0];

        requeued.GetProperty("jobId").GetGuid().Should().Be(jobId);
        requeued.GetProperty("attempt").GetInt32().Should().Be(2);
        requeued.GetProperty("leaseToken").GetString().Should()
            .NotBe(firstToken, "a new lease generation is what makes the old one's result refusable");
    }

    [Fact]
    public async Task Lease_AJobThatHasRunOutOfAttempts_FailsItRatherThanHandingItOver()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            maxAttempts: 1);

        Guid jobId = await host.EnqueueAsync(new NewCollectorJob(CollectorJobKind.Discover), Cancellation);

        await host.Collector.GetAsync(Jobs, Cancellation);
        await host.ExpireLeaseAsync(jobId, Cancellation);

        ApiResponse second = await host.Collector.GetAsync(Jobs, Cancellation);

        second.Json.GetProperty("jobs").GetArrayLength().Should().Be(0);

        CollectorJobRow row = await host.JobAsync(jobId, Cancellation);

        row.Status.Should().Be(CollectorJobStatus.Failed);
        row.Outcome.Should().Be(CollectorJobOutcome.Failed);
        row.Detail.Should().Contain("Abandoned");

        (await host.OutboxEventNamesAsync(Cancellation)).Should()
            .Contain(typeof(CollectorJobCompleted).FullName);
    }

    [Fact]
    public async Task Lease_AJobWhoseDeviceHasBeenRemoved_FailsItAtTheLease()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(host, "core-sw-02", "10.0.0.2", Cancellation);
        Guid jobId = await host.EnqueueAsync(new NewCollectorJob(CollectorJobKind.Poll, deviceId), Cancellation);

        (await host.Client.DeleteAsync($"/api/v1/devices/{deviceId}", Cancellation)).Status.Should().Be(204);

        ApiResponse leased = await host.Collector.GetAsync(Jobs, Cancellation);

        leased.Json.GetProperty("jobs").GetArrayLength().Should()
            .Be(0, "a collector can do nothing with a job naming a device that is gone");

        CollectorJobRow row = await host.JobAsync(jobId, Cancellation);

        row.Status.Should().Be(CollectorJobStatus.Failed);
        row.Detail.Should().Contain("no longer in the inventory");
    }

    [Fact]
    public async Task Lease_RespectsTheLimitAndTheCeiling()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        for (int index = 0; index < 4; index++)
        {
            await host.EnqueueAsync(new NewCollectorJob(CollectorJobKind.Discover), Cancellation);
        }

        ApiResponse leased = await host.Collector.GetAsync($"{Jobs}&limit=2", Cancellation);

        leased.Json.GetProperty("jobs").GetArrayLength().Should().Be(2);

        // Above the configured ceiling is clamped rather than refused: the collector asked for
        // more work than the API is willing to hand one process, which is not an error.
        ApiResponse rest = await host.Collector.GetAsync($"{Jobs}&limit=1000", Cancellation);

        rest.Json.GetProperty("jobs").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Lease_WithNoCollectorNamed_Is400()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse refused = await host.Collector.GetAsync("/internal/collector/jobs", Cancellation);

        refused.Status.Should().Be(400);
        refused.Member("code").Should().Be("collector.name-required");
    }

    [Fact]
    public async Task Enqueue_AJobNamingADeviceThatDoesNotExist_IsRefused()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Func<Task> queueing = () => host.EnqueueAsync(
            new NewCollectorJob(CollectorJobKind.Poll, Guid.NewGuid()),
            Cancellation);

        await queueing.Should().ThrowAsync<InvalidOperationException>(
            "a job pointing at something that is gone can only ever fail");
    }
}
