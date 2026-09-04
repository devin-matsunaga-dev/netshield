using FluentAssertions;

using NetShield.Contracts.Identity;

using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Inventory;
using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Collector;

/// <summary>
/// The shared secret, and the fact that it and a session cookie open disjoint sets of routes.
/// </summary>
/// <remarks>
/// This is what makes "the decrypt path is reachable only from the collector-job endpoint" a
/// property of the running system rather than of the source. A signed-in administrator cannot
/// reach the internal contract however many permissions they hold, and a collector cannot reach
/// the API the SPA uses however correct its secret is.
/// </remarks>
public sealed class CollectorAuthenticationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Jobs = "/internal/collector/jobs?collector=collector-test";
    private const string Heartbeat = "/internal/collector/heartbeat";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lease_WithTheSharedSecret_Succeeds()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse leased = await host.Collector.GetAsync(Jobs, Cancellation);

        leased.Status.Should().Be(200);
    }

    [Fact]
    public async Task Lease_WithNoCredential_Is401()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse refused = await host.Collector.WithSecretAsync(secret: null, Jobs, Cancellation);

        refused.Status.Should().Be(401);
    }

    [Fact]
    public async Task Lease_WithAWrongSecret_Is401()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse refused = await host.Collector.WithSecretAsync(
            "not-the-secret-but-exactly-as-long-0000000",
            Jobs,
            Cancellation);

        refused.Status.Should().Be(401);
    }

    [Fact]
    public async Task Lease_WithASecretOfADifferentLength_Is401()
    {
        // The comparison is over digests, so it takes the same time whatever arrives. What this
        // asserts is only that a short guess is refused like any other.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse refused = await host.Collector.WithSecretAsync("x", Jobs, Cancellation);

        refused.Status.Should().Be(401);
    }

    [Fact]
    public async Task Lease_WithAnAdministratorSession_Is401()
    {
        // The most privileged session in the system, and it does not open this door.
        await using InventoryHost host = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            UserRole.Administrator);

        ApiResponse refused = await host.Client.GetAsync(Jobs, Cancellation);

        refused.Status.Should().Be(401);
    }

    [Fact]
    public async Task TheApi_DoesNotAcceptTheCollectorSecret()
    {
        // And the other direction: the collector's credential is not a session.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse refused = await host.Collector.GetAsync("/api/v1/devices", Cancellation);

        refused.Status.Should().Be(401);
    }

    [Fact]
    public async Task ARefusedLease_IsAProblemDocumentAndNotARedirect()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse refused = await host.Collector.WithSecretAsync(secret: null, Jobs, Cancellation);

        refused.Member("type").Should().NotBeNull("CONVENTIONS.md §4 gives the API one error shape");
        refused.Member("traceId").Should().NotBeNull();
    }

    [Fact]
    public async Task ARefusedLease_DoesNotEchoTheSecretIntoTheLog()
    {
        const string presented = "wrong-secret-that-should-never-be-logged-000";

        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await host.Collector.WithSecretAsync(presented, Jobs, Cancellation);

        host.RecordedLogs().Should().NotContain(line => line.Contains(presented, StringComparison.Ordinal));
        host.RecordedLogs().Should()
            .NotContain(line => line.Contains(InventoryHost.CollectorSharedSecret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AHostWithADifferentSecret_RefusesThisClient()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            collectorSecret: "a-different-secret-of-a-sufficient-length-00");

        ApiResponse refused = await host.Collector.GetAsync(Jobs, Cancellation);

        refused.Status.Should().Be(401);

        // And the heartbeat is behind the same policy, not only the lease.
        ApiResponse heartbeat = await host.Collector.PostAsync(
            Heartbeat,
            new { name = "collector-test", version = "0.1.0", capacity = 4, running = 0 },
            Cancellation);

        heartbeat.Status.Should().Be(401);
    }
}
