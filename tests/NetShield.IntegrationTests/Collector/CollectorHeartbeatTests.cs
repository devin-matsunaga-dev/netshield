using FluentAssertions;

using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Inventory;
using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Collector;

/// <summary>
/// Liveness: one row per collector, and the pacing the API hands back on every beat.
/// </summary>
public sealed class CollectorHeartbeatTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Heartbeat = "/internal/collector/heartbeat";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Heartbeat_RecordsTheCollectorAndAnswersWithThePacing()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation, leaseSeconds: 120);

        ApiResponse acknowledged = await BeatAsync(host, capacity: 8, running: 3);

        acknowledged.Status.Should().Be(200);
        acknowledged.Json.GetProperty("leaseSeconds").GetInt32().Should().Be(120);
        acknowledged.Json.GetProperty("pollSeconds").GetInt32().Should().Be(15);
        acknowledged.Json.GetProperty("maxJobsPerLease").GetInt32().Should().Be(25);

        CollectorNodeRow? node = await host.CollectorNodeAsync("collector-test", Cancellation);

        node.Should().NotBeNull();
        node!.Capacity.Should().Be(8);
        node.Running.Should().Be(3);
        node.Version.Should().Be("0.1.0");
    }

    [Fact]
    public async Task Heartbeat_Twice_UpdatesTheOneRowRatherThanAppending()
    {
        // A row every fifteen seconds would answer "is anything collecting" slowly and need a
        // retention policy of its own.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await BeatAsync(host, capacity: 8, running: 0);
        await BeatAsync(host, capacity: 8, running: 5);

        CollectorNodeRow? node = await host.CollectorNodeAsync("collector-test", Cancellation);

        node!.Running.Should().Be(5);

        // Distinguishable from "two rows and we read one" only by the unique index, which is what
        // makes the second beat an update rather than a duplicate-key failure.
        ApiResponse third = await BeatAsync(host, capacity: 8, running: 1);

        third.Status.Should().Be(200);
    }

    [Fact]
    public async Task Heartbeat_WritesNoAuditRow()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        IReadOnlyList<AuditRow> before = await host.AuditRowsAsync(Cancellation);

        await BeatAsync(host, capacity: 8, running: 0);

        (await host.AuditRowsAsync(Cancellation)).Should().HaveCount(before.Count);
    }

    [Fact]
    public async Task Heartbeat_WithANonsenseCapacity_Is400()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse refused = await host.Collector.PostAsync(
            Heartbeat,
            new { name = "collector-test", version = "0.1.0", capacity = -1, running = 0 },
            Cancellation);

        refused.Status.Should().Be(400);
    }

    private static Task<ApiResponse> BeatAsync(InventoryHost host, int capacity, int running) =>
        host.Collector.PostAsync(
            Heartbeat,
            new { name = "collector-test", version = "0.1.0", capacity, running },
            Cancellation);
}
