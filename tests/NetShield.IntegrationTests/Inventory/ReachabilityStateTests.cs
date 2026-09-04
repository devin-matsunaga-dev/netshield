using System.Text.Json;

using FluentAssertions;

using NetShield.Contracts.Inventory;
using NetShield.Contracts.Inventory.Events;

using NetShield.IntegrationTests.Collector;
using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// The whole round trip, through the real queue, the real contract and the real outbox: a probe
/// is queued, a collector reports on it, and the device's state moves — or does not.
/// </summary>
/// <remarks>
/// This is also the first proof that <c>CollectorJobCompleted</c> has a subscriber. WP-1.3 built
/// the seam and stored every result without interpreting one; everything below happens because
/// something now reads them.
/// </remarks>
public sealed class ReachabilityStateTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnUnansweredProbeRepeatedToTheThreshold_TakesTheDeviceOfflineOnce()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await NewDeviceAsync(host);

        (await host.DeviceStateAsync(deviceId, Cancellation)).Should().Be(DeviceState.Unknown);

        await ProbeAsync(host, deviceId, sent: 4, received: 0);
        (await host.DeviceStateAsync(deviceId, Cancellation)).Should().Be(DeviceState.Unknown);

        await ProbeAsync(host, deviceId, sent: 4, received: 0);
        (await host.DeviceStateAsync(deviceId, Cancellation)).Should().Be(DeviceState.Unknown);

        // Three consecutive failures is the configured threshold.
        await ProbeAsync(host, deviceId, sent: 4, received: 0);
        (await host.DeviceStateAsync(deviceId, Cancellation)).Should().Be(DeviceState.Offline);

        (await StateChangesAsync(host)).Should().ContainSingle();
    }

    [Fact]
    public async Task ADeviceThatStaysOffline_RaisesNoFurtherEvent()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await NewDeviceAsync(host);

        foreach (int _ in Enumerable.Range(0, 8))
        {
            await ProbeAsync(host, deviceId, sent: 4, received: 0);
        }

        (await host.DeviceStateAsync(deviceId, Cancellation)).Should().Be(DeviceState.Offline);
        (await StateChangesAsync(host)).Should().ContainSingle(
            "a week-long outage is one event, not one per probe");
    }

    [Fact]
    public async Task ADeviceThatRecovers_ComesBackOnlineAtTheSuccessThreshold()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await NewDeviceAsync(host);

        foreach (int _ in Enumerable.Range(0, 3))
        {
            await ProbeAsync(host, deviceId, sent: 4, received: 0);
        }

        (await host.DeviceStateAsync(deviceId, Cancellation)).Should().Be(DeviceState.Offline);

        await ProbeAsync(host, deviceId, sent: 4, received: 4);
        (await host.DeviceStateAsync(deviceId, Cancellation)).Should().Be(DeviceState.Offline);

        await ProbeAsync(host, deviceId, sent: 4, received: 4);
        (await host.DeviceStateAsync(deviceId, Cancellation)).Should().Be(DeviceState.Online);

        IReadOnlyList<DeviceStateChanged> changes = await StateChangesAsync(host);

        changes.Should().HaveCount(2);
        changes[0].State.Should().Be(DeviceState.Offline);
        changes[1].PreviousState.Should().Be(DeviceState.Offline);
        changes[1].State.Should().Be(DeviceState.Online);
        changes[1].Hostname.Should().Be("switch-01");
    }

    [Fact]
    public async Task AFlappingDevice_EmitsNoTransitionAtAll()
    {
        // The WP-1.4 criterion. Twelve probes alternating up and down, and the device never
        // accumulates a run long enough for either observation to be adopted.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await NewDeviceAsync(host);

        foreach (int probe in Enumerable.Range(0, 12))
        {
            await ProbeAsync(host, deviceId, sent: 4, received: probe % 2 == 0 ? 0 : 4);
        }

        (await host.DeviceStateAsync(deviceId, Cancellation)).Should().Be(DeviceState.Unknown);
        (await StateChangesAsync(host)).Should().BeEmpty();
    }

    [Fact]
    public async Task SustainedPartialLoss_TakesTheDeviceToWarning()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await NewDeviceAsync(host);

        await ProbeAsync(host, deviceId, sent: 4, received: 2);
        await ProbeAsync(host, deviceId, sent: 4, received: 2);

        (await host.DeviceStateAsync(deviceId, Cancellation)).Should().Be(DeviceState.Warning);
    }

    [Fact]
    public async Task EveryProbe_RecordsTheRoundTripAndTheLossOnTheReachabilityRow()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await NewDeviceAsync(host);

        await ProbeAsync(host, deviceId, sent: 4, received: 3);

        ReachabilityRow? row = await host.ReachabilityAsync(deviceId, Cancellation);

        row.Should().NotBeNull();
        row!.LastLossPercent.Should().Be(25);
        row.LastRttMilliseconds.Should().Be(4);
        row.LastProbeAt.Should().NotBeNull();
        row.LastError.Should().BeNull();
        row.PendingState.Should().Be(DeviceState.Warning);
        row.PendingObservations.Should().Be(1);
    }

    [Fact]
    public async Task EveryProbe_KeepsItsPerRequestRoundTripsOnTheJobRow()
    {
        // "RTT is recorded per probe". The per-request detail stays on the job row, which is the
        // record of one unit of work, until metric_samples exists to hold a series (WP-3.1).
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await NewDeviceAsync(host);

        await ProbeAsync(host, deviceId, sent: 4, received: 3);

        Guid jobId = (await host.JobIdsForAsync(deviceId, Cancellation))[0];
        CollectorJobRow job = await host.JobAsync(jobId, Cancellation);

        job.Result.Should().NotBeNull();

        using JsonDocument result = JsonDocument.Parse(job.Result!);

        JsonElement replies = result.RootElement.GetProperty("replies");

        replies.GetArrayLength().Should().Be(4);
        replies[3].GetProperty("rttMilliseconds").ValueKind.Should().Be(JsonValueKind.Null);
        replies[0].GetProperty("rttMilliseconds").GetDouble().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AProbeTheCollectorCouldNotPerform_LeavesTheDeviceStateUntouched()
    {
        // The distinction the whole package rests on. A collector without an ICMP socket fails
        // its jobs; it does not get to report the estate as offline.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await NewDeviceAsync(host);

        await ProbeAsync(host, deviceId, sent: 4, received: 4);
        await ProbeAsync(host, deviceId, sent: 4, received: 4);

        (await host.DeviceStateAsync(deviceId, Cancellation)).Should().Be(DeviceState.Online);

        foreach (int _ in Enumerable.Range(0, 5))
        {
            await host.ScheduleReachabilityAsync(Cancellation);
            await ReachabilityFixtures.FailOneAsync(
                host, "No ICMP socket could be opened.", Cancellation);
            await host.MakeDueAsync(deviceId, Cancellation);
        }

        (await host.DeviceStateAsync(deviceId, Cancellation)).Should().Be(
            DeviceState.Online, "the collector's health is not evidence about the device");

        ReachabilityRow? row = await host.ReachabilityAsync(deviceId, Cancellation);

        row!.LastError.Should().Contain("ICMP socket");
        row.PendingState.Should().Be(DeviceState.Online, "the run of observations is untouched too");
    }

    [Fact]
    public async Task ASuccessfulJobCarryingSomethingElse_IsRecordedAsUnreadableRatherThanAsDown()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await NewDeviceAsync(host);

        await host.ScheduleReachabilityAsync(Cancellation);
        await ReachabilityFixtures.CompleteWithAsync(host, """{ "somethingElse": true }""", Cancellation);

        (await host.DeviceStateAsync(deviceId, Cancellation)).Should().Be(DeviceState.Unknown);

        ReachabilityRow? row = await host.ReachabilityAsync(deviceId, Cancellation);

        row!.LastError.Should().NotBeNull();
        row.PendingObservations.Should().Be(0);
    }

    [Fact]
    public async Task AResultDeliveredTwice_IsAppliedOnce()
    {
        // Outbox delivery is at-least-once, and a redelivered probe would otherwise advance a run
        // halfway to a threshold that only one probe supports.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await NewDeviceAsync(host);

        await ProbeAsync(host, deviceId, sent: 4, received: 0);

        ReachabilityRow? once = await host.ReachabilityAsync(deviceId, Cancellation);

        await host.RedeliverOutboxAsync(Cancellation);

        ReachabilityRow? twice = await host.ReachabilityAsync(deviceId, Cancellation);

        twice!.PendingObservations.Should().Be(once!.PendingObservations).And.Be(1);
        twice.LastAppliedJobId.Should().Be(once.LastAppliedJobId);
    }

    [Fact]
    public async Task AStateChange_DoesNotStampTheDevicesUpdatedAt()
    {
        // Nothing an operator maintains has changed, and stamping it would make every probe look
        // like an estate-wide edit in the device list, which sorts by it.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await NewDeviceAsync(host);

        DateTimeOffset created = (await host.Client.GetAsync($"/api/v1/devices/{deviceId}", Cancellation))
            .Json.GetProperty("updatedAt").GetDateTimeOffset();

        await ProbeAsync(host, deviceId, sent: 4, received: 4);
        await ProbeAsync(host, deviceId, sent: 4, received: 4);

        (await host.DeviceStateAsync(deviceId, Cancellation)).Should().Be(DeviceState.Online);

        DateTimeOffset after = (await host.Client.GetAsync($"/api/v1/devices/{deviceId}", Cancellation))
            .Json.GetProperty("updatedAt").GetDateTimeOffset();

        after.Should().Be(created);

        // Where the change is visible instead.
        (await host.ReachabilityAsync(deviceId, Cancellation))!.LastChangedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ATransition_IsVisibleOnTheDeviceThroughTheApi()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await NewDeviceAsync(host);

        await ProbeAsync(host, deviceId, sent: 4, received: 4);
        await ProbeAsync(host, deviceId, sent: 4, received: 4);

        ApiResponse detail = await host.Client.GetAsync($"/api/v1/devices/{deviceId}", Cancellation);

        // Read through the contract type rather than off the JSON, because the five WP-1.1
        // inventory enums are still written as ordinals on the wire — a defect this package
        // found and did not fix, since correcting it changes the API contract and the generated
        // client (recorded in STATUS.md). The claim under test is that the state moved, not how
        // it is spelled.
        DeviceDetail device = JsonSerializer.Deserialize<DeviceDetail>(
            detail.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        device.State.Should().Be(DeviceState.Online);
    }

    private static async Task<Guid> NewDeviceAsync(InventoryHost host) =>
        await CollectorFixtures.CreateDeviceAsync(host, "switch-01", "10.10.0.1", Cancellation);

    /// <summary>Queue a probe, report it, deliver the event, and make the device due again.</summary>
    private static async Task ProbeAsync(InventoryHost host, Guid deviceId, int sent, int received)
    {
        await host.ScheduleReachabilityAsync(Cancellation);
        await ReachabilityFixtures.CompleteOneAsync(host, sent, received, Cancellation);
        await host.MakeDueAsync(deviceId, Cancellation);
    }

    private static async Task<IReadOnlyList<DeviceStateChanged>> StateChangesAsync(InventoryHost host) =>
        await host.OutboxPayloadsAsync<DeviceStateChanged>(Cancellation);
}
