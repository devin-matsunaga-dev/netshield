using System.Text.Json;

using FluentAssertions;

using NetShield.IntegrationTests.Collector;
using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// The fourth producer of collector work: the pass that decides which devices are due for a
/// topology read and what to ask them.
/// </summary>
/// <remarks>
/// The loop that drives it is a <c>BackgroundService</c> registered by an opt-in call from the
/// composition root and is deliberately not registered here — what these exercise is the pass,
/// which is separated from the timer for exactly that reason.
/// </remarks>
public sealed class TopologyScheduleTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task APass_QueuesAWalkForEveryDeviceThatHasNeverBeenRead()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation, "sw-1", "10.20.0.1");
        await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation, "sw-2", "10.20.0.2");

        (await host.ScheduleTopologyAsync(Cancellation)).Should().Be(2);
    }

    [Fact]
    public async Task APass_QueuesTheNeighbourWalkFirstWhenBothAreDue()
    {
        // The neighbour read is the primary source for adjacency; the routing read supplements
        // it. One SNMP conversation per device at a time, so the route walk takes the next pass.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await host.ScheduleTopologyAsync(Cancellation);

        IReadOnlyList<CollectorJobParametersRow> jobs = await host.CollectorJobsAsync(Cancellation);

        jobs.Should().ContainSingle();
        Walk(jobs[0]).Should().Be("neighbors");
    }

    [Fact]
    public async Task APass_QueuesTheRouteWalkOnceTheNeighbourWalkIsNotDue()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        // The first pass queues and reschedules the neighbour walk; reporting it clears the
        // outstanding job so the next pass can see the device again.
        await host.ScheduleTopologyAsync(Cancellation);

        TopologyFixtures.LeasedWalk walk = await TopologyFixtures.LeaseAsync(host, Cancellation);

        await TopologyFixtures.ReportAsync(
            host,
            walk,
            TopologyFixtures.NeighborResult(lldpSupported: true),
            Cancellation);

        (await host.ScheduleTopologyAsync(Cancellation)).Should().Be(1);

        IReadOnlyList<CollectorJobParametersRow> jobs = await host.CollectorJobsAsync(Cancellation);

        jobs.Should().HaveCount(2);
        jobs.Select(Walk).Should().BeEquivalentTo(["neighbors", "routes"]);
    }

    [Fact]
    public async Task APass_SkipsADeviceThatAlreadyHasAWalkOutstanding()
    {
        // A collector outage must not build a backlog nobody will ever run, and two walks of one
        // device applied in either order would have each withdrawing the other's edges.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        (await host.ScheduleTopologyAsync(Cancellation)).Should().Be(1);
        (await host.ScheduleTopologyAsync(Cancellation)).Should().Be(0);
    }

    [Fact]
    public async Task APass_SkipsADeviceWithNoSnmpCredentialAndDoesNotKeepFindingIt()
    {
        // A device without a credential would otherwise produce one failed job per interval for
        // ever, each recording the same sentence. It is skipped and its due times move on.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(
            host,
            "no-credential",
            "10.20.0.9",
            Cancellation);

        (await host.ScheduleTopologyAsync(Cancellation)).Should().Be(0);
        (await host.CollectorJobsAsync(Cancellation)).Should().BeEmpty();

        TopologyScanRow? scan = await host.TopologyScanAsync(deviceId, Cancellation);

        scan.Should().NotBeNull();
        scan!.NextNeighborWalkAt.Should().BeAfter(DateTimeOffset.UtcNow);
        scan.NextRouteWalkAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task APass_SkipsADeviceThatHasBeenRemoved()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        (await host.Client.DeleteAsync($"/api/v1/devices/{deviceId}", Cancellation))
            .Status.Should().Be(204);

        (await host.ScheduleTopologyAsync(Cancellation)).Should().Be(0);
    }

    [Fact]
    public async Task APass_SkipsADeviceWithAFingerprintWalkOutstanding()
    {
        // Deliberately stricter than it needs to be: a fingerprint walk and a neighbour walk open
        // an SNMP session to the same agent with the same credential, and running both at once is
        // two conversations a small device has no reason to be asked to hold.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        (await DiscoveryFixtures.RequestWalkAsync(host, deviceId, Cancellation))
            .Status.Should().Be(202);

        (await host.ScheduleTopologyAsync(Cancellation)).Should().Be(0);
    }

    [Fact]
    public async Task APass_QueuesNothingWhenTopologyCollectionIsDisabled()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            topology: new TopologySettings(Enabled: false));

        await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        (await host.ScheduleTopologyAsync(Cancellation)).Should().Be(0);
    }

    [Fact]
    public async Task APass_IsBoundedByItsCeiling()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            topology: new TopologySettings(MaxJobsPerScan: 2));

        for (int index = 1; index <= 4; index++)
        {
            await DiscoveryFixtures.CreateWalkableDeviceAsync(
                host,
                Cancellation,
                $"sw-{index}",
                $"10.20.1.{index}");
        }

        (await host.ScheduleTopologyAsync(Cancellation)).Should().Be(2);
    }

    [Fact]
    public async Task ADueTimeIsSpreadByTheDevicesOwnId()
    {
        // An estate created by one discovery run would otherwise fall due in the same second for
        // ever. Derived from the id rather than a random source so it survives a restart.
        //
        // The scan interval is what the spread is taken modulo, so it has to be a realistic one:
        // at the one-second interval the rest of this suite runs at there is no room to spread
        // into, and every device would legitimately fall due together.
        await using InventoryHost host = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            topology: new TopologySettings(ScanIntervalSeconds: 60));

        for (int index = 1; index <= 6; index++)
        {
            await DiscoveryFixtures.CreateWalkableDeviceAsync(
                host,
                Cancellation,
                $"sw-{index}",
                $"10.20.2.{index}");
        }

        await host.ScheduleTopologyAsync(Cancellation);

        List<DateTimeOffset> due = [];

        foreach (Guid deviceId in await host.DeviceIdsAsync(Cancellation))
        {
            TopologyScanRow? scan = await host.TopologyScanAsync(deviceId, Cancellation);

            if (scan is not null)
            {
                due.Add(scan.NextNeighborWalkAt);
            }
        }

        due.Should().HaveCount(6);
        due.Distinct().Should().HaveCountGreaterThan(1);
    }

    /// <summary>Which walk a queued job's parameters name.</summary>
    /// <remarks>
    /// Parsed rather than matched as a substring: the column is <c>json</c>, PostgreSQL stores it
    /// with its own spacing, and a test that depended on that spacing would be testing the
    /// database's formatter.
    /// </remarks>
    private static string Walk(CollectorJobParametersRow job)
    {
        using JsonDocument document = JsonDocument.Parse(job.Parameters);

        return document.RootElement.GetProperty("walk").GetString()!;
    }
}
