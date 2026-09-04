using System.Text.Json;

using FluentAssertions;

using NetShield.Contracts.Collector;
using NetShield.Contracts.Inventory;

using NetShield.IntegrationTests.Collector;
using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// The collector queue's first producer.
/// </summary>
/// <remarks>
/// Until WP-1.4 nothing in the running system put a row in <c>collector_jobs</c>: the queue was
/// built, tested by resolving it out of the container, and never fed. These tests are about the
/// thing that feeds it — which devices it picks, what it queues for them, and the two ways it
/// avoids queueing work that can only pile up.
/// </remarks>
public sealed class ReachabilityScheduleTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Jobs = "/internal/collector/jobs?collector=collector-test";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Schedule_ADeviceThatHasNeverBeenProbed_QueuesAnIcmpPollForIt()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(
            host, "switch-01", "10.10.0.1", Cancellation);

        int queued = await host.ScheduleReachabilityAsync(Cancellation);

        queued.Should().Be(1);

        IReadOnlyList<Guid> jobIds = await host.JobIdsForAsync(deviceId, Cancellation);

        jobIds.Should().ContainSingle();

        // The parameters are the contract with the collector: the discriminator that says which
        // probe this is, and the three values SPEC.md makes configurable.
        string? parameters = await host.JobParametersAsync(jobIds[0], Cancellation);

        parameters.Should().NotBeNull();

        using JsonDocument document = JsonDocument.Parse(parameters!);

        document.RootElement.GetProperty("probe").GetString().Should().Be("icmp");
        document.RootElement.GetProperty("count").GetInt32().Should().BeGreaterThan(0);
        document.RootElement.TryGetProperty("timeoutSeconds", out _).Should().BeTrue();
        document.RootElement.TryGetProperty("intervalSeconds", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Schedule_AQueuedProbe_CarriesNoCredential()
    {
        // An ICMP echo authenticates to nothing. A job naming a profile would have the lease open
        // one for no reason and write an audit row claiming a credential was released.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await CollectorFixtures.CreateDeviceAsync(host, "switch-01", "10.10.0.1", Cancellation);
        await host.ScheduleReachabilityAsync(Cancellation);

        ApiResponse leased = await host.Collector.GetAsync(Jobs, Cancellation);

        JsonElement job = leased.Json.GetProperty("jobs")[0];

        job.GetProperty("kind").GetString().Should().Be(nameof(CollectorJobKind.Poll));
        job.GetProperty("credential").ValueKind.Should().Be(JsonValueKind.Null);
        job.GetProperty("device").GetProperty("ipAddress").GetString().Should().Be("10.10.0.1");

        (await host.AuditRowsAsync(Cancellation)).Should()
            .NotContain(row => row.TargetType == "credential-profile");
    }

    [Fact]
    public async Task Schedule_RunTwiceWithNothingHavingFallenDue_QueuesNothingTheSecondTime()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(
            host, "switch-01", "10.10.0.1", Cancellation);

        (await host.ScheduleReachabilityAsync(Cancellation)).Should().Be(1);
        (await host.ScheduleReachabilityAsync(Cancellation)).Should().Be(0);

        (await host.JobIdsForAsync(deviceId, Cancellation)).Should().ContainSingle();
    }

    [Fact]
    public async Task Schedule_ADeviceWithAProbeStillOutstanding_QueuesNoSecondOne()
    {
        // The bound on the queue. Without it, a collector that stopped answering would leave one
        // job per device per interval accumulating for as long as the outage lasted.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(
            host, "switch-01", "10.10.0.1", Cancellation);

        await host.ScheduleReachabilityAsync(Cancellation);

        await host.MakeDueAsync(deviceId, Cancellation);

        (await host.ScheduleReachabilityAsync(Cancellation)).Should().Be(0);
        (await host.JobIdsForAsync(deviceId, Cancellation)).Should().ContainSingle();
    }

    [Fact]
    public async Task Schedule_AfterTheOutstandingProbeFinishes_QueuesTheNextOne()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(
            host, "switch-01", "10.10.0.1", Cancellation);

        await host.ScheduleReachabilityAsync(Cancellation);
        await ReachabilityFixtures.CompleteOneAsync(host, sent: 4, received: 4, Cancellation);

        await host.MakeDueAsync(deviceId, Cancellation);

        (await host.ScheduleReachabilityAsync(Cancellation)).Should().Be(1);
        (await host.JobIdsForAsync(deviceId, Cancellation)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Schedule_ASoftDeletedDevice_IsNotProbed()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(
            host, "switch-01", "10.10.0.1", Cancellation);

        ApiResponse removed = await host.Client.DeleteAsync($"/api/v1/devices/{deviceId}", Cancellation);
        removed.Status.Should().Be(204);

        (await host.ScheduleReachabilityAsync(Cancellation)).Should().Be(0);
        (await host.JobIdsForAsync(deviceId, Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task Schedule_MoreDevicesThanTheCeiling_QueuesTheCeilingAndLeavesTheRest()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            reachability: new ReachabilitySettings(MaxJobsPerScan: 2));

        foreach (int index in Enumerable.Range(1, 5))
        {
            await CollectorFixtures.CreateDeviceAsync(
                host, $"switch-{index:00}", $"10.10.0.{index}", Cancellation);
        }

        (await host.ScheduleReachabilityAsync(Cancellation)).Should().Be(2);

        // The devices the first pass did not reach are still due, so the next pass takes the next
        // two rather than starving them.
        (await host.ScheduleReachabilityAsync(Cancellation)).Should().Be(2);
        (await host.ScheduleReachabilityAsync(Cancellation)).Should().Be(1);
        (await host.ScheduleReachabilityAsync(Cancellation)).Should().Be(0);
    }

    [Fact]
    public async Task Schedule_Always_MovesTheNextProbeAnIntervalOut()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            reachability: new ReachabilitySettings(PollIntervalSeconds: 60, ScanIntervalSeconds: 15));

        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(
            host, "switch-01", "10.10.0.1", Cancellation);

        DateTimeOffset before = DateTimeOffset.UtcNow;

        await host.ScheduleReachabilityAsync(Cancellation);

        ReachabilityRow? row = await host.ReachabilityAsync(deviceId, Cancellation);

        row.Should().NotBeNull();

        // One interval out, plus a spread of less than one scan derived from the device's own id
        // so that an estate imported in one go does not fall due in one second for ever after.
        row!.NextProbeAt.Should().BeAfter(before.AddSeconds(60));
        row.NextProbeAt.Should().BeBefore(before.AddSeconds(60 + 15 + 5));
    }

    [Fact]
    public async Task Schedule_ADeviceNothingIsKnownAbout_IsPreferredOverOneAlreadyProbed()
    {
        // A device nobody has ever asked about is the most urgent thing in the estate, so it
        // sorts ahead of every device that already has a row.
        await using InventoryHost host = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            reachability: new ReachabilitySettings(MaxJobsPerScan: 1));

        Guid known = await CollectorFixtures.CreateDeviceAsync(
            host, "switch-01", "10.10.0.1", Cancellation);

        await host.ScheduleReachabilityAsync(Cancellation);
        await ReachabilityFixtures.CompleteOneAsync(host, sent: 4, received: 4, Cancellation);
        await host.MakeDueAsync(known, Cancellation);

        Guid unknown = await CollectorFixtures.CreateDeviceAsync(
            host, "switch-02", "10.10.0.2", Cancellation);

        await host.ScheduleReachabilityAsync(Cancellation);

        (await host.JobIdsForAsync(unknown, Cancellation)).Should().ContainSingle();
        (await host.JobIdsForAsync(known, Cancellation)).Should().ContainSingle(
            "the device already probed once was not reached by this pass");
    }
}
