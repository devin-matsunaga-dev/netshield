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
/// Result submission: idempotent by job id and lease token, and one outbox event per job that
/// actually finished.
/// </summary>
public sealed class CollectorResultTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Jobs = "/internal/collector/jobs?collector=collector-test";
    private const string Results = "/internal/collector/results";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Submit_AResultUnderTheCurrentToken_RecordsItAndRaisesTheEvent()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        (Guid jobId, string token) = await LeaseOneAsync(host);

        ApiResponse acknowledged = await SubmitAsync(host, jobId, token, "Succeeded", new { reachable = true });

        acknowledged.Status.Should().Be(200);
        acknowledged.Json.GetProperty("accepted").GetArrayLength().Should().Be(1);
        acknowledged.Json.GetProperty("duplicates").GetArrayLength().Should().Be(0);
        acknowledged.Json.GetProperty("rejected").GetArrayLength().Should().Be(0);

        CollectorJobRow row = await host.JobAsync(jobId, Cancellation);

        row.Status.Should().Be(CollectorJobStatus.Succeeded);
        row.Outcome.Should().Be(CollectorJobOutcome.Succeeded);
        row.Result.Should().Contain("reachable");

        (await host.OutboxEventNamesAsync(Cancellation)).Should()
            .ContainSingle(name => name == typeof(CollectorJobCompleted).FullName);
    }

    [Fact]
    public async Task Submit_TheSameResultTwice_IsANoOpAndRaisesOneEvent()
    {
        // The retry a collector makes when it never saw the answer to the first submission.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        (Guid jobId, string token) = await LeaseOneAsync(host);

        await SubmitAsync(host, jobId, token, "Succeeded", new { reachable = true });

        ApiResponse again = await SubmitAsync(host, jobId, token, "Succeeded", new { reachable = false });

        again.Status.Should().Be(200);
        again.Json.GetProperty("accepted").GetArrayLength().Should().Be(0);
        again.Json.GetProperty("duplicates")[0].GetGuid().Should().Be(jobId);

        CollectorJobRow row = await host.JobAsync(jobId, Cancellation);

        row.Result.Should().Contain("true", "the second submission changed nothing");

        (await host.OutboxEventNamesAsync(Cancellation)).Should()
            .ContainSingle(name => name == typeof(CollectorJobCompleted).FullName);
    }

    [Fact]
    public async Task Submit_UnderAnExpiredLease_IsRejectedRatherThanOverwriting()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        (Guid jobId, string firstToken) = await LeaseOneAsync(host);

        await host.ExpireLeaseAsync(jobId, Cancellation);

        // Somebody else picks it up, which is what the visibility timeout is for.
        ApiResponse released = await host.Collector.GetAsync(Jobs, Cancellation);
        string secondToken = released.Json.GetProperty("jobs")[0].GetProperty("leaseToken").GetString()!;

        ApiResponse late = await SubmitAsync(host, jobId, firstToken, "Succeeded", new { stale = true });

        late.Json.GetProperty("rejected")[0].GetProperty("reason").GetString().Should().Be("stale-lease");

        CollectorJobRow row = await host.JobAsync(jobId, Cancellation);

        row.Status.Should().Be(CollectorJobStatus.Leased, "the current holder still has it");
        row.LeaseToken.Should().Be(secondToken);

        // And the holder's own result is still accepted.
        ApiResponse current = await SubmitAsync(host, jobId, secondToken, "Succeeded", new { stale = false });

        current.Json.GetProperty("accepted").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Submit_ForAJobThatDoesNotExist_IsRejectedAndTheRestOfTheBatchLands()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        (Guid jobId, string token) = await LeaseOneAsync(host);

        ApiResponse acknowledged = await host.Collector.PostAsync(
            Results,
            new
            {
                collector = "collector-test",
                results = new object[]
                {
                    new { jobId = Guid.NewGuid(), leaseToken = "nope", outcome = "Failed" },
                    new { jobId, leaseToken = token, outcome = "Succeeded" }
                }
            },
            Cancellation);

        acknowledged.Status.Should().Be(200);
        acknowledged.Json.GetProperty("rejected")[0].GetProperty("reason").GetString().Should().Be("unknown-job");
        acknowledged.Json.GetProperty("accepted")[0].GetGuid().Should()
            .Be(jobId, "one bad report must not refuse the good ones beside it");
    }

    [Fact]
    public async Task Submit_AFailure_RecordsTheDetailWithTheCredentialTakenOut()
    {
        // A device error message is where a community string ends up, and the collector's own
        // redaction is not the only thing standing between it and the database (SPEC.md §5).
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        (Guid jobId, string token) = await LeaseOneAsync(host);

        await host.Collector.PostAsync(
            Results,
            new
            {
                collector = "collector-test",
                results = new[]
                {
                    new
                    {
                        jobId,
                        leaseToken = token,
                        outcome = "Failed",
                        detail = "snmp auth failed (community=s3cr3t-value)"
                    }
                }
            },
            Cancellation);

        CollectorJobRow row = await host.JobAsync(jobId, Cancellation);

        row.Status.Should().Be(CollectorJobStatus.Failed);
        row.Detail.Should().NotContain("s3cr3t-value");
        row.Detail.Should().Contain("[REDACTED]");
    }

    [Fact]
    public async Task Submit_WritesNoAuditRow()
    {
        // The decision this package settled: result ingestion is data-plane traffic, and a row
        // per batch would bury the rows that describe what a person did (WP-0.5).
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        (Guid jobId, string token) = await LeaseOneAsync(host);

        IReadOnlyList<AuditRow> before = await host.AuditRowsAsync(Cancellation);

        await SubmitAsync(host, jobId, token, "Succeeded", new { reachable = true });

        (await host.AuditRowsAsync(Cancellation)).Should().HaveCount(before.Count);
    }

    [Fact]
    public async Task Submit_AMalformedBatch_Is400()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse refused = await host.Collector.PostAsync(
            Results,
            new { collector = "", results = Array.Empty<object>() },
            Cancellation);

        refused.Status.Should().Be(400);
    }

    private static async Task<(Guid JobId, string Token)> LeaseOneAsync(InventoryHost host)
    {
        await host.EnqueueAsync(new NewCollectorJob(CollectorJobKind.Discover), Cancellation);

        ApiResponse leased = await host.Collector.GetAsync(Jobs, Cancellation);

        JsonElement job = leased.Json.GetProperty("jobs")[0];

        return (job.GetProperty("jobId").GetGuid(), job.GetProperty("leaseToken").GetString()!);
    }

    private static Task<ApiResponse> SubmitAsync(
        InventoryHost host,
        Guid jobId,
        string token,
        string outcome,
        object data) =>
        host.Collector.PostAsync(
            Results,
            new
            {
                collector = "collector-test",
                results = new[] { new { jobId, leaseToken = token, outcome, data } }
            },
            Cancellation);
}
