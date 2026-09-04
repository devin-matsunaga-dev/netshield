using System.Text.Json;

using FluentAssertions;

using NetShield.Contracts.Inventory;
using NetShield.Contracts.Inventory.Events;

using NetShield.IntegrationTests.Collector;
using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// What happens to a sweep after a collector reports it: the run's counters, the per-host
/// outcomes, the candidates, and the rules that decide which responders become one.
/// </summary>
/// <remarks>
/// The whole round trip runs here — seed, run, lease, report, deliver — against a result payload
/// written the way <c>collector/discovery/executor.py</c> writes it rather than through the API's
/// own type, so that a member renamed on one side fails here rather than quietly stopping being
/// read.
/// </remarks>
public sealed class DiscoverySweepResultTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAddressThatAnsweredBecomesACandidateAwaitingReview()
    {
        // The WP-1.6 criterion: results appear as reviewable candidates rather than as devices.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(host, Cancellation, ranges: ["192.0.2.0/29"]);

        await SweepFixtures.SweepAsync(host, seedId, ["192.0.2.3"], Cancellation);

        ApiResponse candidates = await host.Client.GetAsync(
            "/api/v1/discovery/candidates",
            Cancellation);

        JsonElement candidate = candidates.Json.GetProperty("items").EnumerateArray().Single();

        candidate.GetProperty("address").GetString().Should().Be("192.0.2.3");
        candidate.GetProperty("status").GetString().Should().Be("New");
        candidate.GetProperty("timesSeen").GetInt32().Should().Be(1);
        candidate.GetProperty("promotedDeviceId").ValueKind.Should().Be(JsonValueKind.Null);

        // And no device was created by it.
        ApiResponse devices = await host.Client.GetAsync("/api/v1/devices", Cancellation);

        devices.Json.GetProperty("totalCount").GetInt64().Should().Be(0);
    }

    [Fact]
    public async Task ARunRecordsOneOutcomePerResponderAndCountsWhatItFound()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(host, Cancellation, ranges: ["192.0.2.0/28"]);

        Guid runId = await SweepFixtures.SweepAsync(
            host,
            seedId,
            ["192.0.2.3", "192.0.2.9"],
            Cancellation);

        ApiResponse run = await host.Client.GetAsync($"/api/v1/discovery/runs/{runId}", Cancellation);

        run.Json.GetProperty("status").GetString().Should().Be("Completed");
        run.Json.GetProperty("addressCount").GetInt64().Should().Be(14);
        run.Json.GetProperty("respondedCount").GetInt32().Should().Be(2);
        run.Json.GetProperty("newCandidateCount").GetInt32().Should().Be(2);
        run.Json.GetProperty("completedAt").ValueKind.Should().NotBe(JsonValueKind.Null);

        ApiResponse hosts = await host.Client.GetAsync(
            $"/api/v1/discovery/runs/{runId}/hosts",
            Cancellation);

        // Only responders are rows. The twelve silent addresses are accounted for by the run's
        // own addressCount, not by twelve rows saying nothing happened.
        hosts.Json.GetProperty("totalCount").GetInt64().Should().Be(2);
        hosts.Json.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("outcome").GetString())
            .Should().AllBe("NewCandidate");
    }

    [Fact]
    public async Task ARerunUpdatesTheCandidateRatherThanAddingASecond()
    {
        // The WP-1.6 criterion: a re-run updates rather than duplicates.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(host, Cancellation, ranges: ["192.0.2.0/29"]);

        Guid first = await SweepFixtures.SweepAsync(host, seedId, ["192.0.2.3"], Cancellation);
        Guid second = await SweepFixtures.SweepAsync(host, seedId, ["192.0.2.3"], Cancellation);

        ApiResponse candidates = await host.Client.GetAsync("/api/v1/discovery/candidates", Cancellation);

        candidates.Json.GetProperty("totalCount").GetInt64().Should().Be(1);

        JsonElement candidate = candidates.Json.GetProperty("items").EnumerateArray().Single();

        candidate.GetProperty("timesSeen").GetInt32().Should().Be(2);
        candidate.GetProperty("firstSeenRunId").GetGuid().Should().Be(first);
        candidate.GetProperty("lastSeenRunId").GetGuid().Should().Be(second);
        candidate.GetProperty("firstSeenAt").GetDateTimeOffset()
            .Should().BeOnOrBefore(candidate.GetProperty("lastSeenAt").GetDateTimeOffset());

        // The second run says it saw something it already knew about.
        ApiResponse run = await host.Client.GetAsync($"/api/v1/discovery/runs/{second}", Cancellation);

        run.Json.GetProperty("newCandidateCount").GetInt32().Should().Be(0);
        run.Json.GetProperty("knownCandidateCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ACandidateIsAnnouncedOncePerCandidateAndNotOncePerSighting()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(host, Cancellation, ranges: ["192.0.2.0/29"]);

        await SweepFixtures.SweepAsync(host, seedId, ["192.0.2.3"], Cancellation);
        await SweepFixtures.SweepAsync(host, seedId, ["192.0.2.3"], Cancellation);

        IReadOnlyList<DeviceDiscovered> discovered =
            await host.OutboxPayloadsAsync<DeviceDiscovered>(Cancellation);

        discovered.Should().ContainSingle();
        discovered[0].Address.Should().Be("192.0.2.3");
        discovered[0].SeedId.Should().Be(seedId);
    }

    [Fact]
    public async Task AnAddressThatIsAlreadyADeviceDoesNotBecomeACandidate()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(
            host,
            "switch-01",
            "192.0.2.3",
            Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(host, Cancellation, ranges: ["192.0.2.0/29"]);

        Guid runId = await SweepFixtures.SweepAsync(host, seedId, ["192.0.2.3"], Cancellation);

        (await host.Client.GetAsync("/api/v1/discovery/candidates", Cancellation))
            .Json.GetProperty("totalCount").GetInt64().Should().Be(0);

        JsonElement outcome = (await host.Client.GetAsync(
                $"/api/v1/discovery/runs/{runId}/hosts",
                Cancellation))
            .Json.GetProperty("items").EnumerateArray().Single();

        outcome.GetProperty("outcome").GetString().Should().Be("ExistingDevice");
        outcome.GetProperty("deviceId").GetGuid().Should().Be(deviceId);
    }

    [Fact]
    public async Task AnIgnoredAddressNeverReappearsAsACandidate()
    {
        // The WP-1.6 criterion: an ignored host never reappears.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse ignored = await host.Client.PostAsync(
            "/api/v1/discovery/ignores",
            new CreateDiscoveryIgnoreRequest("192.0.2.0/29", "Printers"),
            Cancellation);

        ignored.Status.Should().Be(201);
        ignored.Json.GetProperty("cidr").GetString().Should().Be("192.0.2.0/29");

        Guid seedId = await SweepFixtures.CreateSeedAsync(host, Cancellation, ranges: ["192.0.2.0/29"]);

        Guid runId = await SweepFixtures.SweepAsync(host, seedId, ["192.0.2.3"], Cancellation);

        (await host.Client.GetAsync("/api/v1/discovery/candidates", Cancellation))
            .Json.GetProperty("totalCount").GetInt64().Should().Be(0);

        ApiResponse run = await host.Client.GetAsync($"/api/v1/discovery/runs/{runId}", Cancellation);

        // Recorded rather than silently dropped: the run still says the address answered.
        run.Json.GetProperty("respondedCount").GetInt32().Should().Be(1);
        run.Json.GetProperty("ignoredCount").GetInt32().Should().Be(1);

        (await host.Client.GetAsync($"/api/v1/discovery/runs/{runId}/hosts", Cancellation))
            .Json.GetProperty("items").EnumerateArray().Single()
            .GetProperty("outcome").GetString().Should().Be("Ignored");
    }

    [Fact]
    public async Task AFailedSweepFailsItsOwnSpanAndTheRunSaysSo()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            discovery: new DiscoverySettings(MaxAddressesPerJob: 8));

        Guid seedId = await SweepFixtures.CreateSeedAsync(host, Cancellation, ranges: ["192.0.2.0/28"]);

        Guid runId = await SweepFixtures.StartRunAsync(host, seedId, Cancellation);

        (await SweepFixtures.FailSweepsAsync(host, Cancellation)).Should().Be(2);

        ApiResponse run = await host.Client.GetAsync($"/api/v1/discovery/runs/{runId}", Cancellation);

        run.Json.GetProperty("status").GetString().Should().Be("Failed");
        run.Json.GetProperty("jobsFailed").GetInt32().Should().Be(2);
        run.Json.GetProperty("respondedCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ARunWhoseJobsHaveNotAllReportedIsStillRunning()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            discovery: new DiscoverySettings(MaxAddressesPerJob: 4));

        Guid seedId = await SweepFixtures.CreateSeedAsync(host, Cancellation, ranges: ["192.0.2.0/28"]);

        Guid runId = await SweepFixtures.StartRunAsync(host, seedId, Cancellation);

        // One of the run's four jobs, reported on its own.
        DiscoveryRunJobRow job = (await host.RunJobsAsync(runId, Cancellation))[0];

        await ReportAsync(host, [job], reported => Responders(reported, "192.0.2.2"), Cancellation);

        ApiResponse run = await host.Client.GetAsync($"/api/v1/discovery/runs/{runId}", Cancellation);

        run.Json.GetProperty("status").GetString().Should().Be("Running");
        run.Json.GetProperty("jobsCompleted").GetInt32().Should().Be(1);
        run.Json.GetProperty("completedAt").ValueKind.Should().Be(JsonValueKind.Null);

        (await host.OutboxPayloadsAsync<DiscoveryRunCompleted>(Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task ARunWithSomeJobsFailedIsPartiallyFailedAndIsAnnounced()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            discovery: new DiscoverySettings(MaxAddressesPerJob: 8));

        Guid seedId = await SweepFixtures.CreateSeedAsync(host, Cancellation, ranges: ["192.0.2.0/28"]);

        Guid runId = await SweepFixtures.StartRunAsync(host, seedId, Cancellation);

        IReadOnlyList<DiscoveryRunJobRow> jobs = await host.RunJobsAsync(runId, Cancellation);

        await ReportAsync(
            host,
            [jobs[0], jobs[1]],
            reported => reported == jobs[0] ? Responders(reported, "192.0.2.2") : null,
            Cancellation);

        ApiResponse run = await host.Client.GetAsync($"/api/v1/discovery/runs/{runId}", Cancellation);

        run.Json.GetProperty("status").GetString().Should().Be("PartiallyFailed");
        run.Json.GetProperty("newCandidateCount").GetInt32().Should().Be(1);

        IReadOnlyList<DiscoveryRunCompleted> completed =
            await host.OutboxPayloadsAsync<DiscoveryRunCompleted>(Cancellation);

        completed.Should().ContainSingle();
        completed[0].Status.Should().Be(DiscoveryRunStatus.PartiallyFailed);
        completed[0].RespondedCount.Should().Be(1);
    }

    [Fact]
    public async Task ASweepResultAppliedTwiceChangesNothingTheSecondTime()
    {
        // Outbox delivery is at-least-once, and every counter here is the kind a redelivery
        // would silently corrupt.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(host, Cancellation, ranges: ["192.0.2.0/29"]);

        Guid runId = await SweepFixtures.SweepAsync(host, seedId, ["192.0.2.3"], Cancellation);

        await host.RedeliverOutboxAsync(Cancellation);

        ApiResponse run = await host.Client.GetAsync($"/api/v1/discovery/runs/{runId}", Cancellation);

        run.Json.GetProperty("jobsCompleted").GetInt32().Should().Be(1);
        run.Json.GetProperty("respondedCount").GetInt32().Should().Be(1);
        run.Json.GetProperty("newCandidateCount").GetInt32().Should().Be(1);

        (await host.Client.GetAsync($"/api/v1/discovery/runs/{runId}/hosts", Cancellation))
            .Json.GetProperty("totalCount").GetInt64().Should().Be(1);

        (await host.Client.GetAsync("/api/v1/discovery/candidates", Cancellation))
            .Json.GetProperty("items").EnumerateArray().Single()
            .GetProperty("timesSeen").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task AFingerprintWalkIsNotReadAsASweep()
    {
        // Both are Discover jobs and sit in the same table. The run-job table is what says which
        // rows belong to a run, and a walk has none.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.WalkAsync(host, deviceId, DiscoveryFixtures.WalkResult(), Cancellation);

        (await host.Client.GetAsync("/api/v1/discovery/candidates", Cancellation))
            .Json.GetProperty("totalCount").GetInt64().Should().Be(0);

        (await host.Client.GetAsync("/api/v1/discovery/runs", Cancellation))
            .Json.GetProperty("totalCount").GetInt64().Should().Be(0);
    }

    [Fact]
    public async Task ASucceededJobCarryingSomethingElseCountsAsAFailedSpan()
    {
        // "Nothing answered here" and "nobody looked here" are different facts, and only the
        // first is evidence.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(host, Cancellation, ranges: ["192.0.2.0/29"]);

        Guid runId = await SweepFixtures.StartRunAsync(host, seedId, Cancellation);

        DiscoveryRunJobRow job = (await host.RunJobsAsync(runId, Cancellation)).Single();

        await ReportAsync(
            host,
            [job],
            reported => SweepFixtures.SweepResult(
                reported.FirstAddress,
                reported.LastAddress,
                ["192.0.2.3"],
                walk: "snmp"),
            Cancellation);

        ApiResponse run = await host.Client.GetAsync($"/api/v1/discovery/runs/{runId}", Cancellation);

        run.Json.GetProperty("status").GetString().Should().Be("Failed");
        run.Json.GetProperty("respondedCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task AResponderReportedTwiceInOneSpanBecomesOneCandidate()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(host, Cancellation, ranges: ["192.0.2.0/29"]);

        Guid runId = await SweepFixtures.StartRunAsync(host, seedId, Cancellation);

        DiscoveryRunJobRow job = (await host.RunJobsAsync(runId, Cancellation)).Single();

        await ReportAsync(
            host,
            [job],
            reported => Responders(reported, "192.0.2.3", "192.0.2.3"),
            Cancellation);

        (await host.Client.GetAsync("/api/v1/discovery/candidates", Cancellation))
            .Json.GetProperty("totalCount").GetInt64().Should().Be(1);

        (await host.Client.GetAsync($"/api/v1/discovery/runs/{runId}", Cancellation))
            .Json.GetProperty("respondedCount").GetInt32().Should().Be(1);
    }

    /// <summary>
    /// Answers part of a run: leases every one of its sweep jobs at once, then reports the ones
    /// named. Leasing is a claim on everything it returns, so a test that reports jobs one at a
    /// time has to hold the tokens from a single lease.
    /// </summary>
    private static async Task ReportAsync(
        InventoryHost host,
        IReadOnlyList<DiscoveryRunJobRow> jobs,
        Func<DiscoveryRunJobRow, string?> payload,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, string> leases =
            await SweepFixtures.LeaseSweepsAsync(host, cancellationToken);

        foreach (DiscoveryRunJobRow job in jobs)
        {
            await SweepFixtures.SubmitAsync(
                host,
                job.CollectorJobId,
                leases[job.CollectorJobId],
                payload(job),
                cancellationToken);
        }
    }

    private static string Responders(DiscoveryRunJobRow job, params string[] addresses) =>
        SweepFixtures.SweepResult(job.FirstAddress, job.LastAddress, addresses);
}
