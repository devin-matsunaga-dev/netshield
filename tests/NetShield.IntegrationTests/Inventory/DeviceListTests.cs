using System.Text.Json;
using System.Text.Json.Serialization;

using FluentAssertions;

using NetShield.Contracts.Inventory;
using NetShield.Contracts.Paging;

using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

using NetShield.Platform.Paging;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// The device list: keyset pagination, the filters, and the sort. Every assertion here needs the
/// real database — the collation the keyset compares under and the <c>text[]</c> containment the
/// tag filter uses are not things an in-memory provider has.
/// </summary>
public sealed class DeviceListTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Devices = "/api/v1/devices";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task List_MoreThanOnePage_WalksEveryDeviceExactlyOnce()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        for (int index = 0; index < 25; index++)
        {
            await CreateAsync(host, $"sw-{index:D2}", $"10.0.0.{index + 1}");
        }

        IReadOnlyList<Guid> walked = await WalkAsync(host, $"{Devices}?limit=10");

        walked.Should().HaveCount(25);
        walked.Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// The WP-1.1 criterion. A keyset walks values rather than offsets, so a device inserted
    /// while a caller is paging cannot shift the page under them — with offsets, an insert ahead
    /// of the cursor repeats a row and an delete skips one.
    /// </summary>
    [Fact]
    public async Task List_WithInsertsBetweenPages_RepeatsNothingAndSkipsNothing()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        for (int index = 0; index < 15; index++)
        {
            await CreateAsync(host, $"sw-{index:D2}", $"10.0.0.{index + 1}");
        }

        List<Guid> walked = [];
        string? next = null;

        do
        {
            CursorPage<DeviceSummary> page = await PageAsync(
                host,
                next is null ? $"{Devices}?limit=5" : $"{Devices}?limit=5&cursor={Uri.EscapeDataString(next)}");

            walked.AddRange(page.Items.Select(item => item.Id));
            next = page.NextCursor;

            // A device added by somebody else, mid-walk. It sorts after everything already
            // walked, so it may or may not be seen — but nothing already seen may come back.
            if (next is not null)
            {
                await CreateAsync(host, $"late-{walked.Count}", $"10.9.0.{walked.Count}");
            }
        }
        while (next is not null);

        walked.Should().OnlyHaveUniqueItems();
        walked.Should().Contain(await OriginalIdsAsync(host));
    }

    [Fact]
    public async Task List_ALimitOverTheMaximum_IsRefusedRatherThanClamped()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse refused = await host.Client.GetAsync($"{Devices}?limit={PageRequest.MaxLimit + 1}", Cancellation);

        refused.Status.Should().Be(400);
        refused.Member("code").Should().Be(PageRequest.InvalidLimitCode);
    }

    [Fact]
    public async Task List_ACursorItDidNotIssue_Returns400()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse refused = await host.Client.GetAsync($"{Devices}?cursor=not-a-cursor", Cancellation);

        refused.Status.Should().Be(400);
    }

    [Fact]
    public async Task List_ASortFieldItDoesNotOffer_Returns400NamingTheOnesItDoes()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse refused = await host.Client.GetAsync($"{Devices}?sort=serialNumber", Cancellation);

        // Not a silent fall back to the default: a caller who misspells a field and is served a
        // differently ordered page has no way to notice.
        refused.Status.Should().Be(400);
        refused.Member("code").Should().Be("device.unknown-sort");
    }

    [Fact]
    public async Task List_SortedByHostname_OrdersAlphabeticallyAndPagesStably()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        string[] hostnames = ["delta", "alpha", "charlie", "bravo", "echo"];

        for (int index = 0; index < hostnames.Length; index++)
        {
            await CreateAsync(host, hostnames[index], $"10.1.0.{index + 1}");
        }

        CursorPage<DeviceSummary> first = await PageAsync(host, $"{Devices}?sort=hostname&limit=2");

        first.Items.Select(item => item.Hostname).Should().Equal("alpha", "bravo");

        CursorPage<DeviceSummary> second = await PageAsync(
            host,
            $"{Devices}?sort=hostname&limit=2&cursor={Uri.EscapeDataString(first.NextCursor!)}");

        second.Items.Select(item => item.Hostname).Should().Equal("charlie", "delta");
    }

    [Fact]
    public async Task List_SortedByHostnameDescending_ReversesTheOrder()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        string[] hostnames = ["alpha", "bravo", "charlie"];

        for (int index = 0; index < hostnames.Length; index++)
        {
            await CreateAsync(host, hostnames[index], $"10.2.0.{index + 1}");
        }

        CursorPage<DeviceSummary> page = await PageAsync(host, $"{Devices}?sort=hostname&descending=true");

        page.Items.Select(item => item.Hostname).Should().Equal("charlie", "bravo", "alpha");
    }

    /// <summary>
    /// Hostname is not unique, so a page boundary can fall between two devices of the same name.
    /// The id in the cursor is what keeps that from repeating or skipping one.
    /// </summary>
    [Fact]
    public async Task List_SortedByADuplicatedHostname_StillWalksEachDeviceOnce()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        for (int index = 0; index < 6; index++)
        {
            await CreateAsync(host, "switch", $"10.3.0.{index + 1}");
        }

        IReadOnlyList<Guid> walked = await WalkAsync(host, $"{Devices}?sort=hostname&limit=2");

        walked.Should().HaveCount(6);
        walked.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task List_FilteredByEnumeratedAttributes_ReturnsOnlyMatches()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await CreateAsync(host, "core-sw-01", "10.4.0.1", vendor: DeviceVendor.CiscoIos,
            role: DeviceRole.Switch, criticality: CriticalityTier.Critical);
        await CreateAsync(host, "edge-fw-01", "10.4.0.2", vendor: DeviceVendor.FortinetFortiOs,
            role: DeviceRole.Firewall, criticality: CriticalityTier.Low);

        await AssertSingleAsync(host, $"{Devices}?vendor=CiscoIos", "core-sw-01");
        await AssertSingleAsync(host, $"{Devices}?role=Firewall", "edge-fw-01");
        await AssertSingleAsync(host, $"{Devices}?criticality=Critical", "core-sw-01");
        await AssertSingleAsync(host, $"{Devices}?state=Unknown&vendor=FortinetFortiOs", "edge-fw-01");
    }

    [Fact]
    public async Task List_FilteredBySite_IgnoresCase()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await CreateAsync(host, "core-sw-01", "10.5.0.1", site: "HQ");
        await CreateAsync(host, "branch-sw-01", "10.5.0.2", site: "Branch");

        await AssertSingleAsync(host, $"{Devices}?site=hq", "core-sw-01");
    }

    [Fact]
    public async Task List_FilteredByTag_MatchesTheNormalisedValueAndNotASubstring()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await CreateAsync(host, "core-sw-01", "10.6.0.1", tags: ["Core"]);
        await CreateAsync(host, "edge-sw-01", "10.6.0.2", tags: ["core-adjacent"]);

        // Stored as text[], so this is a containment test rather than a LIKE that would also
        // match the tag inside the other tag.
        await AssertSingleAsync(host, $"{Devices}?tag=CORE", "core-sw-01");
    }

    [Fact]
    public async Task List_SearchedByHostnamePrefix_MatchesIgnoringCase()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await CreateAsync(host, "core-sw-01", "10.7.0.1");
        await CreateAsync(host, "edge-fw-01", "10.7.0.2");

        await AssertSingleAsync(host, $"{Devices}?search=CORE", "core-sw-01");
    }

    [Fact]
    public async Task List_SearchedByAddress_MatchesExactlyRatherThanByPrefix()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await CreateAsync(host, "core-sw-01", "10.8.0.1");
        await CreateAsync(host, "core-sw-10", "10.8.0.10");

        // Searching for 10.8.0.1 must not also return 10.8.0.10.
        await AssertSingleAsync(host, $"{Devices}?search=10.8.0.1", "core-sw-01");
    }

    [Fact]
    public async Task List_SearchedWithAWildcard_TreatsItAsText()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await CreateAsync(host, "core-sw-01", "10.10.0.1");

        // A caller typing % must not get every device back.
        CursorPage<DeviceSummary> page = await PageAsync(host, $"{Devices}?search=%25");

        page.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task List_Always_CountsEveryMatchRatherThanThePage()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        for (int index = 0; index < 7; index++)
        {
            await CreateAsync(host, $"sw-{index:D2}", $"10.11.0.{index + 1}");
        }

        CursorPage<DeviceSummary> page = await PageAsync(host, $"{Devices}?limit=3");

        page.Items.Should().HaveCount(3);
        page.TotalCount.Should().Be(7);
    }

    private static async Task AssertSingleAsync(InventoryHost host, string path, string expected)
    {
        CursorPage<DeviceSummary> page = await PageAsync(host, path);

        page.Items.Should().ContainSingle(because: $"{path} should match one device")
            .Which.Hostname.Should().Be(expected);
    }

    private static async Task<IReadOnlyList<Guid>> WalkAsync(InventoryHost host, string firstPage)
    {
        List<Guid> walked = [];
        string? next = null;

        do
        {
            string path = next is null
                ? firstPage
                : $"{firstPage}&cursor={Uri.EscapeDataString(next)}";

            CursorPage<DeviceSummary> page = await PageAsync(host, path);

            walked.AddRange(page.Items.Select(item => item.Id));
            next = page.NextCursor;
        }
        while (next is not null);

        return walked;
    }

    private static async Task<CursorPage<DeviceSummary>> PageAsync(InventoryHost host, string path)
    {
        ApiResponse response = await host.Client.GetAsync(path, Cancellation);

        response.Status.Should().Be(200, "the list should have answered: {0}", response.Body);

        return JsonSerializer.Deserialize<CursorPage<DeviceSummary>>(response.Body, Json)!;
    }

    private static async Task<IReadOnlyList<Guid>> OriginalIdsAsync(InventoryHost host)
    {
        CursorPage<DeviceSummary> page = await PageAsync(host, $"{Devices}?limit=200&search=sw-");

        return [.. page.Items.Select(item => item.Id)];
    }

    private static async Task CreateAsync(
        InventoryHost host,
        string hostname,
        string address,
        DeviceVendor vendor = DeviceVendor.Unknown,
        DeviceRole role = DeviceRole.Other,
        CriticalityTier criticality = CriticalityTier.Medium,
        string? site = null,
        IReadOnlyList<string>? tags = null)
    {
        ApiResponse created = await host.Client.PostAsync(
            Devices,
            new CreateDeviceRequest(hostname, address, vendor, Site: site, Role: role,
                Criticality: criticality, Tags: tags),
            Cancellation);

        created.Status.Should().Be(201, "the fixture device has to exist: {0}", created.Body);
    }

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}
