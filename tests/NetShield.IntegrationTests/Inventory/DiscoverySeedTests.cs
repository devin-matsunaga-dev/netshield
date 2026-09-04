using System.Text.Json;

using FluentAssertions;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// Discovery seeds: what NetShield will sweep, who may change it, and what it refuses to be told.
/// </summary>
public sealed class DiscoverySeedTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASeedIsCreatedWithItsRangesNormalisedAndItsAddressesCounted()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse created = await host.Client.PostAsync(
            "/api/v1/discovery/seeds",
            new CreateDiscoverySeedRequest(
                "Lab",
                "The lab VLANs",
                Enabled: true,

                // Host bits set on the first, a bare address on the second: both are read to the
                // block they mean rather than refused.
                ["10.0.0.5/24", "10.0.1.7"],
                ["10.0.0.128/25"],
                IntervalMinutes: 60),
            Cancellation);

        created.Status.Should().Be(201);
        created.Json.GetProperty("ranges").EnumerateArray()
            .Select(range => range.GetString()).Should().Equal("10.0.0.0/24", "10.0.1.7/32");
        created.Json.GetProperty("exclusions").EnumerateArray()
            .Single().GetString().Should().Be("10.0.0.128/25");

        // 127 probeable addresses in the half of the /24 that is left, plus the single host.
        created.Json.GetProperty("addressCount").GetInt64().Should().Be(128);
        created.Json.GetProperty("nextRunAt").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task ADisabledSeedIsNotScheduledAndSaysSo()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(host, Cancellation, enabled: false);

        ApiResponse read = await host.Client.GetAsync($"/api/v1/discovery/seeds/{seedId}", Cancellation);

        read.Json.GetProperty("enabled").GetBoolean().Should().BeFalse();
        read.Json.GetProperty("nextRunAt").ValueKind.Should().Be(JsonValueKind.Null);
        (await host.SeedNextRunAtAsync(seedId, Cancellation)).Should().BeNull();
    }

    [Fact]
    public async Task ASeedIsReplacedWholeAndAppearsInTheList()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(host, Cancellation, name: "Lab");

        ApiResponse updated = await host.Client.PutAsync(
            $"/api/v1/discovery/seeds/{seedId}",
            new UpdateDiscoverySeedRequest(
                "Lab and DMZ",
                Description: null,
                Enabled: true,
                ["10.0.0.0/30", "10.0.1.0/30"],
                Exclusions: null,
                IntervalMinutes: 120),
            Cancellation);

        updated.Status.Should().Be(200);
        updated.Json.GetProperty("name").GetString().Should().Be("Lab and DMZ");
        updated.Json.GetProperty("intervalMinutes").GetInt32().Should().Be(120);
        updated.Json.GetProperty("addressCount").GetInt64().Should().Be(4);

        ApiResponse list = await host.Client.GetAsync("/api/v1/discovery/seeds", Cancellation);

        list.Json.GetProperty("totalCount").GetInt64().Should().Be(1);
        list.Json.GetProperty("items").EnumerateArray().Single()
            .GetProperty("rangeCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task ARemovedSeedIsGoneFromEveryReadAndReleasesItsName()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(host, Cancellation, name: "Lab");

        (await host.Client.DeleteAsync($"/api/v1/discovery/seeds/{seedId}", Cancellation))
            .Status.Should().Be(204);

        (await host.Client.GetAsync($"/api/v1/discovery/seeds/{seedId}", Cancellation))
            .Status.Should().Be(404);

        // The name is free again: a removed seed must not hold one for ever.
        Guid replacement = await SweepFixtures.CreateSeedAsync(host, Cancellation, name: "Lab");

        replacement.Should().NotBe(seedId);
    }

    [Fact]
    public async Task ASecondSeedWithTheSameName_IsRefusedWhateverTheCase()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await SweepFixtures.CreateSeedAsync(host, Cancellation, name: "Lab");

        ApiResponse second = await host.Client.PostAsync(
            "/api/v1/discovery/seeds",
            new CreateDiscoverySeedRequest("lab", null, true, ["10.9.0.0/30"], null, 60),
            Cancellation);

        second.Status.Should().Be(409);
        second.Json.GetProperty("code").GetString().Should().Be(DiscoveryErrorCodes.SeedNameTaken);
    }

    [Fact]
    public async Task ASeedNameHoldingALikeWildcard_DoesNotCollideWithEveryNameBeginningWithIt()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await SweepFixtures.CreateSeedAsync(host, Cancellation, name: "Core");

        // "Core%" is a LIKE pattern matching "Core"; the uniqueness check compares text, not
        // patterns, so this is a different name and is accepted.
        ApiResponse second = await host.Client.PostAsync(
            "/api/v1/discovery/seeds",
            new CreateDiscoverySeedRequest("Core%", null, true, ["10.9.0.0/30"], null, 60),
            Cancellation);

        second.Status.Should().Be(201);
    }

    [Theory]
    [InlineData("not-a-block")]
    [InlineData("10.0.0.0/33")]
    public async Task ARangeThatIsNotABlock_IsRefusedWithTheFieldNamed(string range)
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse created = await host.Client.PostAsync(
            "/api/v1/discovery/seeds",
            new CreateDiscoverySeedRequest("Lab", null, true, [range], null, 60),
            Cancellation);

        created.Status.Should().Be(400);
        created.Body.Should().Contain("ranges[0]");
    }

    [Fact]
    public async Task ASeedWithNoRanges_IsRefused()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse created = await host.Client.PostAsync(
            "/api/v1/discovery/seeds",
            new CreateDiscoverySeedRequest("Lab", null, true, [], null, 60),
            Cancellation);

        created.Status.Should().Be(400);
    }

    [Fact]
    public async Task TwoRangesThatOverlap_AreRefused()
    {
        // The address count is a sum, which is only right when the ranges are disjoint.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse created = await host.Client.PostAsync(
            "/api/v1/discovery/seeds",
            new CreateDiscoverySeedRequest("Lab", null, true, ["10.0.0.0/24", "10.0.0.0/25"], null, 60),
            Cancellation);

        created.Status.Should().Be(400);
        created.Body.Should().Contain("overlap");
    }

    [Fact]
    public async Task RangesLargerThanOneRunMaySweep_AreRefusedWhenTheyAreSaved()
    {
        // A typing mistake becomes a rejection at the endpoint rather than sixty-five thousand
        // queued jobs at the next scan.
        await using InventoryHost host = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            discovery: new DiscoverySettings(MaxAddressesPerRun: 16));

        ApiResponse created = await host.Client.PostAsync(
            "/api/v1/discovery/seeds",
            new CreateDiscoverySeedRequest("Lab", null, true, ["10.0.0.0/24"], null, 60),
            Cancellation);

        created.Status.Should().Be(400);
        created.Body.Should().Contain("more addresses than one discovery run may sweep");
    }

    [Fact]
    public async Task AnExclusionCanBringASeedBackUnderTheCeiling()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            discovery: new DiscoverySettings(MaxAddressesPerRun: 16));

        ApiResponse created = await host.Client.PostAsync(
            "/api/v1/discovery/seeds",
            new CreateDiscoverySeedRequest("Lab", null, true, ["10.0.0.0/24"], ["10.0.0.16/28", "10.0.0.32/27",
                "10.0.0.64/26", "10.0.0.128/25"], 60),
            Cancellation);

        created.Status.Should().Be(201);
        created.Json.GetProperty("addressCount").GetInt64().Should().Be(15);
    }

    [Fact]
    public async Task AnOperatorMayMaintainASeed()
    {
        // PoliciesWrite is the Operator's, by the WP-0.5 table: "retention policies, notification
        // routing, discovery schedules and maintenance windows" is routine operation of the
        // platform rather than administration of it.
        await using InventoryHost administrator = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(administrator, Cancellation);

        await using InventoryHost operatorHost = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            UserRole.Operator,
            administrator.ConnectionString);

        (await operatorHost.Client.GetAsync($"/api/v1/discovery/seeds/{seedId}", Cancellation))
            .Status.Should().Be(200);

        (await operatorHost.Client.DeleteAsync($"/api/v1/discovery/seeds/{seedId}", Cancellation))
            .Status.Should().Be(204);
    }

    [Theory]
    [InlineData(UserRole.Analyst)]
    [InlineData(UserRole.ReadOnly)]
    public async Task AUserWithoutPoliciesWrite_MayReadASeedAndMayNotChangeOne(UserRole role)
    {
        // Reading is InventoryRead: a seed says which addresses NetShield believes are its
        // estate, which everyone who can see the device list can already see.
        await using InventoryHost administrator = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(administrator, Cancellation);

        await using InventoryHost reader = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            role,
            administrator.ConnectionString);

        (await reader.Client.GetAsync($"/api/v1/discovery/seeds/{seedId}", Cancellation))
            .Status.Should().Be(200);

        ApiResponse refused = await reader.Client.PostAsync(
            "/api/v1/discovery/seeds",
            new CreateDiscoverySeedRequest("Other", null, true, ["10.0.0.0/30"], null, 60),
            Cancellation);

        refused.Status.Should().Be(403);
    }

    [Fact]
    public async Task EveryMutationWritesAnAuditRow()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid seedId = await SweepFixtures.CreateSeedAsync(host, Cancellation);

        await host.Client.PutAsync(
            $"/api/v1/discovery/seeds/{seedId}",
            new UpdateDiscoverySeedRequest("Lab 2", null, true, ["10.0.0.0/30"], null, 30),
            Cancellation);

        await host.Client.DeleteAsync($"/api/v1/discovery/seeds/{seedId}", Cancellation);

        IReadOnlyList<AuditRow> rows = await host.AuditRowsAsync(Cancellation);

        rows.Select(row => row.Action).Should().Contain(
        [
            "inventory.discovery-seed-create",
            "inventory.discovery-seed-update",
            "inventory.discovery-seed-delete"
        ]);

        rows.Where(row => row.Action.StartsWith("inventory.discovery-seed", StringComparison.Ordinal))
            .Should().AllSatisfy(row =>
            {
                row.TargetType.Should().Be("discovery-seed");
                row.TargetId.Should().Be(seedId.ToString());
            });
    }
}

/// <summary>The problem codes these tests branch on, spelled once.</summary>
internal static class DiscoveryErrorCodes
{
    internal const string SeedNameTaken = "discovery.seed-name-taken";
    internal const string RunInFlight = "discovery.run-in-flight";
    internal const string NothingToSweep = "discovery.nothing-to-sweep";
    internal const string CandidateSettled = "discovery.candidate-settled";
    internal const string IgnoreExists = "discovery.ignore-exists";
    internal const string DuplicateAddress = "device.duplicate-primary-ip";
}
