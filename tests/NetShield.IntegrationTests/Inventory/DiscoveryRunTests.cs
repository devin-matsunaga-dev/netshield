using System.Text.Json;

using FluentAssertions;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Inventory.Events;

using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// Starting a discovery run: what it queues, what it refuses, and what the schedule does on its
/// own. Everything after a collector reports is <see cref="DiscoverySweepResultTests"/>.
/// </summary>
public sealed class DiscoveryRunTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARunIsQueuedAndAcceptedWithTheWorkItWillDo()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            discovery: new DiscoverySettings(MaxAddressesPerJob: 4));

        Guid seedId = await SweepFixtures.CreateSeedAsync(
            host,
            Cancellation,
            ranges: ["192.0.2.0/28"]);

        ApiResponse queued = await SweepFixtures.RequestRunAsync(host, seedId, Cancellation);

        queued.Status.Should().Be(202);
        queued.Json.GetProperty("seedId").GetGuid().Should().Be(seedId);

        // A /28 is 14 probeable addresses, which is four jobs at four addresses each.
        queued.Json.GetProperty("addressCount").GetInt64().Should().Be(14);
        queued.Json.GetProperty("jobCount").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task TheQueuedJobsCarryTheSweepParametersTheCollectorReads()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            discovery: new DiscoverySettings(MaxAddressesPerJob: 8));

        Guid seedId = await SweepFixtures.CreateSeedAsync(
            host,
            Cancellation,
            ranges: ["192.0.2.0/28"],
            exclusions: ["192.0.2.4/30"]);

        Guid runId = await SweepFixtures.StartRunAsync(host, seedId, Cancellation);

        IReadOnlyList<DiscoveryRunJobRow> jobs = await host.RunJobsAsync(runId, Cancellation);

        jobs.Should().HaveCount(2);
        jobs[0].FirstAddress.Should().Be("192.0.2.1");
        jobs[^1].LastAddress.Should().Be("192.0.2.14");

        string? parameters = await host.JobParametersAsync(jobs[0].CollectorJobId, Cancellation);

        using JsonDocument document = JsonDocument.Parse(parameters!);

        document.RootElement.GetProperty("walk").GetString().Should().Be("sweep");
        document.RootElement.GetProperty("firstAddress").GetString().Should().Be("192.0.2.1");
        document.RootElement.GetProperty("exclusions").EnumerateArray()
            .Single().GetString().Should().Be("192.0.2.4/30");
        document.RootElement.GetProperty("concurrency").GetInt32().Should().Be(64);
    }

    [Fact]
    public async Task ASweepJobNamesNoDeviceAndNoCredential()
    {
        // The whole point of it: a sweep is looking for hosts that are not devices yet, and an
        // echo request authenticates to nothing.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(host, Cancellation, ranges: ["192.0.2.0/30"]);
        Guid runId = await SweepFixtures.StartRunAsync(host, seedId, Cancellation);

        Guid jobId = (await host.RunJobsAsync(runId, Cancellation)).Single().CollectorJobId;

        CollectorJobRow job = await host.JobAsync(jobId, Cancellation);

        job.CredentialProfileId.Should().BeNull();
    }

    [Fact]
    public async Task ASecondRunOfASeedWithOneInFlight_IsRefused()
    {
        // A person clicking twice must not become two runs sweeping one range and interleaving
        // their candidates — the rule the on-demand walk applies to a device, at seed scale.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(host, Cancellation);

        await SweepFixtures.StartRunAsync(host, seedId, Cancellation);

        ApiResponse second = await SweepFixtures.RequestRunAsync(host, seedId, Cancellation);

        second.Status.Should().Be(409);
        second.Json.GetProperty("code").GetString().Should().Be(DiscoveryErrorCodes.RunInFlight);
    }

    [Fact]
    public async Task ARunOfASeedWhoseAddressesAreAllExcluded_IsRefused()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(
            host,
            Cancellation,
            ranges: ["192.0.2.0/29"],
            exclusions: ["192.0.2.0/29"]);

        ApiResponse refused = await SweepFixtures.RequestRunAsync(host, seedId, Cancellation);

        refused.Status.Should().Be(422);
        refused.Json.GetProperty("code").GetString().Should().Be(DiscoveryErrorCodes.NothingToSweep);
    }

    [Fact]
    public async Task ARunOfASeedThatDoesNotExist_IsNotFound()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse refused = await SweepFixtures.RequestRunAsync(host, Guid.CreateVersion7(), Cancellation);

        refused.Status.Should().Be(404);
    }

    [Fact]
    public async Task ADisabledSeedCanStillBeRunOnDemand()
    {
        // The switch governs the schedule. A person asking has said what they want, and refusing
        // them would make "sweep this once" impossible to express.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(host, Cancellation, enabled: false);

        (await SweepFixtures.RequestRunAsync(host, seedId, Cancellation)).Status.Should().Be(202);
    }

    [Fact]
    public async Task StartingARunAnnouncesItAndWritesAnAuditRow()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(host, Cancellation, name: "Lab");
        Guid runId = await SweepFixtures.StartRunAsync(host, seedId, Cancellation);

        IReadOnlyList<DiscoveryRunStarted> started =
            await host.OutboxPayloadsAsync<DiscoveryRunStarted>(Cancellation);

        started.Should().ContainSingle();
        started[0].RunId.Should().Be(runId);
        started[0].SeedName.Should().Be("Lab");
        started[0].Trigger.Should().Be(DiscoveryRunTrigger.OnDemand);

        (await host.AuditRowsAsync(Cancellation))
            .Should().Contain(row =>
                row.Action == "inventory.discovery-run-start" && row.TargetId == runId.ToString());
    }

    [Fact]
    public async Task ARunKeepsItsOwnCopyOfWhatItSwept()
    {
        // A seed is editable and a run's history is not: "254 addresses" has to keep saying which.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(
            host,
            Cancellation,
            name: "Lab",
            ranges: ["192.0.2.0/29"]);

        Guid runId = await SweepFixtures.StartRunAsync(host, seedId, Cancellation);

        await host.Client.PutAsync(
            $"/api/v1/discovery/seeds/{seedId}",
            new UpdateDiscoverySeedRequest("Renamed", null, true, ["198.51.100.0/30"], null, 60),
            Cancellation);

        ApiResponse read = await host.Client.GetAsync($"/api/v1/discovery/runs/{runId}", Cancellation);

        read.Json.GetProperty("seedName").GetString().Should().Be("Lab");
        read.Json.GetProperty("ranges").EnumerateArray().Single().GetString().Should().Be("192.0.2.0/29");
    }

    [Fact]
    public async Task ARunIsCappedAtTheJobsOneRunMayQueue()
    {
        // The ceiling that stops one run from filling the queue with work no collector will
        // reach for hours. The run reports what it actually queued rather than what it wanted.
        await using InventoryHost host = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            discovery: new DiscoverySettings(MaxAddressesPerJob: 2, MaxJobsPerRun: 3));

        Guid seedId = await SweepFixtures.CreateSeedAsync(host, Cancellation, ranges: ["192.0.2.0/28"]);

        ApiResponse queued = await SweepFixtures.RequestRunAsync(host, seedId, Cancellation);

        queued.Json.GetProperty("jobCount").GetInt32().Should().Be(3);
        queued.Json.GetProperty("addressCount").GetInt64().Should().Be(6);
    }

    [Fact]
    public async Task TheScheduleStartsARunForASeedThatHasFallenDue()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(host, Cancellation, intervalMinutes: 30);

        (await host.ScheduleDiscoveryAsync(Cancellation)).Should().Be(1);

        ApiResponse runs = await host.Client.GetAsync(
            $"/api/v1/discovery/runs?seedId={seedId}",
            Cancellation);

        runs.Json.GetProperty("items").EnumerateArray().Single()
            .GetProperty("trigger").GetString().Should().Be("Scheduled");

        // Moved one interval out, so the next pass leaves it alone.
        (await host.SeedNextRunAtAsync(seedId, Cancellation))
            .Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(25));
    }

    [Fact]
    public async Task TheScheduleSkipsASeedThatIsNotDue()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await SweepFixtures.CreateSeedAsync(host, Cancellation);

        await host.ScheduleDiscoveryAsync(Cancellation);

        (await host.ScheduleDiscoveryAsync(Cancellation)).Should().Be(0);
    }

    [Fact]
    public async Task TheScheduleSkipsADisabledSeedAndARemovedOne()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await SweepFixtures.CreateSeedAsync(host, Cancellation, name: "Off", enabled: false);

        Guid removed = await SweepFixtures.CreateSeedAsync(host, Cancellation, name: "Gone");
        await host.Client.DeleteAsync($"/api/v1/discovery/seeds/{removed}", Cancellation);

        (await host.ScheduleDiscoveryAsync(Cancellation)).Should().Be(0);
    }

    [Fact]
    public async Task TheScheduleSkipsASeedWithARunAlreadyInFlight()
    {
        // Without it, a collector outage would leave one run per seed per interval accumulating,
        // each one a fan-out of many jobs.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(host, Cancellation);

        await SweepFixtures.StartRunAsync(host, seedId, Cancellation);
        await host.MakeSeedDueAsync(seedId, Cancellation);

        (await host.ScheduleDiscoveryAsync(Cancellation)).Should().Be(0);
    }

    [Fact]
    public async Task TheScheduleStartsNoMoreThanItsPerPassCeiling()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            discovery: new DiscoverySettings(MaxRunsPerScan: 1));

        await SweepFixtures.CreateSeedAsync(host, Cancellation, name: "One", ranges: ["192.0.2.0/30"]);
        await SweepFixtures.CreateSeedAsync(host, Cancellation, name: "Two", ranges: ["192.0.2.4/30"]);

        (await host.ScheduleDiscoveryAsync(Cancellation)).Should().Be(1);
        (await host.ScheduleDiscoveryAsync(Cancellation)).Should().Be(1);
    }

    [Fact]
    public async Task ASeedThatCannotRunIsRescheduledRatherThanRetriedEveryPass()
    {
        // A seed whose addresses are all excluded cannot start a run. Leaving next_run_at in the
        // past would have the pass find it, refuse it and log it on every scan for ever.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(
            host,
            Cancellation,
            ranges: ["192.0.2.0/29"],
            exclusions: ["192.0.2.0/29"]);

        (await host.ScheduleDiscoveryAsync(Cancellation)).Should().Be(0);

        (await host.SeedNextRunAtAsync(seedId, Cancellation))
            .Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task AnAnalystMayReadTheRunHistoryAndMayNotStartARun()
    {
        // Starting one is DiscoveryRun — the same permission the on-demand fingerprint walk
        // carries, because both make NetShield reach into the estate outside its schedule.
        await using InventoryHost administrator = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(administrator, Cancellation);

        await using InventoryHost analyst = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            UserRole.Analyst,
            administrator.ConnectionString);

        (await analyst.Client.GetAsync("/api/v1/discovery/runs", Cancellation)).Status.Should().Be(200);

        (await SweepFixtures.RequestRunAsync(analyst, seedId, Cancellation)).Status.Should().Be(403);
    }

    [Fact]
    public async Task AnOperatorMayStartARun()
    {
        await using InventoryHost administrator = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(administrator, Cancellation);

        await using InventoryHost operatorHost = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            UserRole.Operator,
            administrator.ConnectionString);

        (await SweepFixtures.RequestRunAsync(operatorHost, seedId, Cancellation)).Status.Should().Be(202);
    }
}
