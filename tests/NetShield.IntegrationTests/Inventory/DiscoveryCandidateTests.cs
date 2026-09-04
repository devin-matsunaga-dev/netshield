using System.Text.Json;

using FluentAssertions;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Inventory.Events;

using NetShield.IntegrationTests.Collector;
using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// The review step: promoting a candidate into a device, dismissing one for good, and the
/// permanent ignore list behind the dismissal.
/// </summary>
public sealed class DiscoveryCandidateTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PromotingACandidateCreatesADeviceAtItsAddress()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid candidateId = await CandidateAsync(host, "192.0.2.3", Cancellation);

        ApiResponse promoted = await Promote(host, candidateId, "switch-07", Cancellation);

        promoted.Status.Should().Be(201);
        promoted.Json.GetProperty("hostname").GetString().Should().Be("switch-07");
        promoted.Json.GetProperty("primaryIpAddress").GetString().Should().Be("192.0.2.3");

        // A sweep established neither what it is nor whether it is answering: one echo reply is
        // not the two consecutive observations WP-1.4 wants before it calls a device online.
        //
        // Both are read as ordinals because that is what the API writes today: the five WP-1.1
        // inventory enums carry no type-level converter, which STATUS.md records as a WP-1.1
        // defect for WP-1.7 to fix. This assertion changes shape when it is.
        promoted.Json.GetProperty("vendor").GetInt32().Should().Be((int)DeviceVendor.Unknown);
        promoted.Json.GetProperty("state").GetInt32().Should().Be((int)DeviceState.Unknown);

        JsonElement candidate = await CandidateByIdAsync(host, candidateId, Cancellation);

        candidate.GetProperty("status").GetString().Should().Be("Promoted");
        candidate.GetProperty("promotedDeviceId").GetGuid()
            .Should().Be(promoted.Json.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task PromotingACandidateAnnouncesTheDeviceAndWritesAnAuditRow()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid candidateId = await CandidateAsync(host, "192.0.2.3", Cancellation);

        ApiResponse promoted = await Promote(host, candidateId, "switch-07", Cancellation);

        IReadOnlyList<DeviceCreated> created = await host.OutboxPayloadsAsync<DeviceCreated>(Cancellation);

        created.Should().ContainSingle();
        created[0].DeviceId.Should().Be(promoted.Json.GetProperty("id").GetGuid());

        (await host.AuditRowsAsync(Cancellation)).Should().Contain(row =>
            row.Action == "inventory.discovery-candidate-promote"
            && row.TargetType == "discovery-candidate"
            && row.TargetId == candidateId.ToString());
    }

    [Fact]
    public async Task PromotingACandidateAtAnAddressADeviceAlreadyHolds_IsRefused()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid candidateId = await CandidateAsync(host, "192.0.2.3", Cancellation);

        // Somebody adds the device by hand between the sweep and the review.
        await CollectorFixtures.CreateDeviceAsync(host, "switch-07", "192.0.2.3", Cancellation);

        ApiResponse refused = await Promote(host, candidateId, "switch-08", Cancellation);

        refused.Status.Should().Be(409);
        refused.Json.GetProperty("code").GetString().Should().Be(DiscoveryErrorCodes.DuplicateAddress);
    }

    [Fact]
    public async Task PromotingACandidateTwice_IsRefused()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid candidateId = await CandidateAsync(host, "192.0.2.3", Cancellation);

        await Promote(host, candidateId, "switch-07", Cancellation);

        ApiResponse second = await Promote(host, candidateId, "switch-08", Cancellation);

        second.Status.Should().Be(409);
        second.Json.GetProperty("code").GetString().Should().Be(DiscoveryErrorCodes.CandidateSettled);
    }

    [Fact]
    public async Task PromotingACandidateThatDoesNotExist_IsNotFound()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        (await Promote(host, Guid.CreateVersion7(), "switch-07", Cancellation))
            .Status.Should().Be(404);
    }

    [Fact]
    public async Task PromotingACandidateWithNoHostname_IsRefused()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid candidateId = await CandidateAsync(host, "192.0.2.3", Cancellation);

        (await Promote(host, candidateId, "  ", Cancellation)).Status.Should().Be(400);
    }

    [Fact]
    public async Task APromotedCandidateStopsBeingOfferedForReview()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid candidateId = await CandidateAsync(host, "192.0.2.3", Cancellation);

        await Promote(host, candidateId, "switch-07", Cancellation);

        ApiResponse review = await host.Client.GetAsync(
            "/api/v1/discovery/candidates?status=New",
            Cancellation);

        review.Json.GetProperty("totalCount").GetInt64().Should().Be(0);
    }

    [Fact]
    public async Task IgnoringACandidateAddsItsAddressToTheIgnoreListAndItNeverReappears()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(host, Cancellation, ranges: ["192.0.2.0/29"]);

        await SweepFixtures.SweepAsync(host, seedId, ["192.0.2.3"], Cancellation);

        Guid candidateId = await FirstCandidateIdAsync(host, Cancellation);

        ApiResponse ignored = await host.Client.PostAsync(
            $"/api/v1/discovery/candidates/{candidateId}/ignore",
            new { },
            Cancellation);

        ignored.Status.Should().Be(201);
        ignored.Json.GetProperty("cidr").GetString().Should().Be("192.0.2.3/32");

        Guid rerun = await SweepFixtures.SweepAsync(host, seedId, ["192.0.2.3"], Cancellation);

        (await CandidateByIdAsync(host, candidateId, Cancellation))
            .GetProperty("status").GetString().Should().Be("Ignored");

        (await host.Client.GetAsync($"/api/v1/discovery/runs/{rerun}", Cancellation))
            .Json.GetProperty("ignoredCount").GetInt32().Should().Be(1);

        (await host.Client.GetAsync("/api/v1/discovery/candidates?status=New", Cancellation))
            .Json.GetProperty("totalCount").GetInt64().Should().Be(0);
    }

    [Fact]
    public async Task IgnoringACandidateTwice_IsRefused()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid candidateId = await CandidateAsync(host, "192.0.2.3", Cancellation);

        await host.Client.PostAsync(
            $"/api/v1/discovery/candidates/{candidateId}/ignore",
            new { },
            Cancellation);

        ApiResponse second = await host.Client.PostAsync(
            $"/api/v1/discovery/candidates/{candidateId}/ignore",
            new { },
            Cancellation);

        second.Status.Should().Be(409);
        second.Json.GetProperty("code").GetString().Should().Be(DiscoveryErrorCodes.CandidateSettled);
    }

    [Fact]
    public async Task IgnoringABlockSettlesTheCandidatesInsideItThatAreStillWaiting()
    {
        // An operator who has just said "never offer me anything in this block" should not then
        // have to dismiss the candidates already on the list from inside it.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(host, Cancellation, ranges: ["192.0.2.0/28"]);

        await SweepFixtures.SweepAsync(host, seedId, ["192.0.2.3", "192.0.2.9"], Cancellation);

        (await host.Client.PostAsync(
            "/api/v1/discovery/ignores",
            new CreateDiscoveryIgnoreRequest("192.0.2.0/29", "Printers"),
            Cancellation)).Status.Should().Be(201);

        ApiResponse review = await host.Client.GetAsync(
            "/api/v1/discovery/candidates?status=New",
            Cancellation);

        review.Json.GetProperty("items").EnumerateArray().Single()
            .GetProperty("address").GetString().Should().Be("192.0.2.9");
    }

    [Fact]
    public async Task AnIgnoreEntryIsNormalisedAndCannotBeAddedTwice()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        (await host.Client.PostAsync(
            "/api/v1/discovery/ignores",
            new CreateDiscoveryIgnoreRequest("192.0.2.5", null),
            Cancellation)).Json.GetProperty("cidr").GetString().Should().Be("192.0.2.5/32");

        ApiResponse second = await host.Client.PostAsync(
            "/api/v1/discovery/ignores",
            new CreateDiscoveryIgnoreRequest("192.0.2.5/32", null),
            Cancellation);

        second.Status.Should().Be(409);
        second.Json.GetProperty("code").GetString().Should().Be(DiscoveryErrorCodes.IgnoreExists);
    }

    [Fact]
    public async Task AnIgnoreEntryThatIsNotABlock_IsRefused()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        (await host.Client.PostAsync(
            "/api/v1/discovery/ignores",
            new CreateDiscoveryIgnoreRequest("not-a-block", null),
            Cancellation)).Status.Should().Be(400);
    }

    [Fact]
    public async Task RemovingAnIgnoreEntryLetsTheNextRunOfferTheAddressAgain()
    {
        // The way back. It is deliberately an act somebody takes, and the next sweep is what puts
        // the address back on the review list — one rule decides what is reviewable, not two.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(host, Cancellation, ranges: ["192.0.2.0/29"]);

        await SweepFixtures.SweepAsync(host, seedId, ["192.0.2.3"], Cancellation);

        Guid candidateId = await FirstCandidateIdAsync(host, Cancellation);

        ApiResponse ignored = await host.Client.PostAsync(
            $"/api/v1/discovery/candidates/{candidateId}/ignore",
            new { },
            Cancellation);

        Guid ignoreId = ignored.Json.GetProperty("id").GetGuid();

        (await host.Client.DeleteAsync($"/api/v1/discovery/ignores/{ignoreId}", Cancellation))
            .Status.Should().Be(204);

        await SweepFixtures.SweepAsync(host, seedId, ["192.0.2.3"], Cancellation);

        (await CandidateByIdAsync(host, candidateId, Cancellation))
            .GetProperty("status").GetString().Should().Be("New");
    }

    [Fact]
    public async Task TheIgnoreListIsListedAndAudited()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await host.Client.PostAsync(
            "/api/v1/discovery/ignores",
            new CreateDiscoveryIgnoreRequest("192.0.2.0/29", "Printers"),
            Cancellation);

        ApiResponse list = await host.Client.GetAsync("/api/v1/discovery/ignores", Cancellation);

        list.Json.GetProperty("totalCount").GetInt64().Should().Be(1);
        list.Json.GetProperty("items").EnumerateArray().Single()
            .GetProperty("reason").GetString().Should().Be("Printers");

        (await host.AuditRowsAsync(Cancellation)).Should().Contain(row =>
            row.Action == "inventory.discovery-ignore-create" && row.TargetType == "discovery-ignore");
    }

    [Fact]
    public async Task AnAnalystMayReviewCandidatesAndMayNotPromoteOne()
    {
        // Promotion creates a device, which is InventoryWrite.
        await using InventoryHost administrator = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid candidateId = await CandidateAsync(administrator, "192.0.2.3", Cancellation);

        await using InventoryHost analyst = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            UserRole.Analyst,
            administrator.ConnectionString);

        (await analyst.Client.GetAsync("/api/v1/discovery/candidates", Cancellation))
            .Status.Should().Be(200);

        (await Promote(analyst, candidateId, "switch-07", Cancellation)).Status.Should().Be(403);

        (await analyst.Client.PostAsync(
            $"/api/v1/discovery/candidates/{candidateId}/ignore",
            new { },
            Cancellation)).Status.Should().Be(403);
    }

    private static Task<ApiResponse> Promote(
        InventoryHost host,
        Guid candidateId,
        string hostname,
        CancellationToken cancellationToken) =>
        host.Client.PostAsync(
            $"/api/v1/discovery/candidates/{candidateId}/promote",
            new PromoteDiscoveryCandidateRequest(
                hostname,
                "Lab",
                DeviceRole.Switch,
                CriticalityTier.Medium,
                DeviceEnvironment.Lab,
                Owner: null,
                Tags: null,
                Notes: null),
            cancellationToken);

    /// <summary>Sweeps a range and returns the candidate one responder produced.</summary>
    private static async Task<Guid> CandidateAsync(
        InventoryHost host,
        string address,
        CancellationToken cancellationToken)
    {
        Guid seedId = await SweepFixtures.CreateSeedAsync(
            host,
            cancellationToken,
            ranges: ["192.0.2.0/29"]);

        await SweepFixtures.SweepAsync(host, seedId, [address], cancellationToken);

        return await FirstCandidateIdAsync(host, cancellationToken);
    }

    private static async Task<Guid> FirstCandidateIdAsync(
        InventoryHost host,
        CancellationToken cancellationToken)
    {
        ApiResponse candidates = await host.Client.GetAsync(
            "/api/v1/discovery/candidates",
            cancellationToken);

        return candidates.Json.GetProperty("items").EnumerateArray().First()
            .GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> CandidateByIdAsync(
        InventoryHost host,
        Guid candidateId,
        CancellationToken cancellationToken)
    {
        ApiResponse candidates = await host.Client.GetAsync(
            "/api/v1/discovery/candidates",
            cancellationToken);

        return candidates.Json.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == candidateId);
    }
}
