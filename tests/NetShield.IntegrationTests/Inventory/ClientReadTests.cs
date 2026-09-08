using System.Text.Json;

using FluentAssertions;

using NetShield.Contracts.Identity;

using NetShield.IntegrationTests.Collector;
using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// The five client routes: what they answer, what they refuse, and who may reach them.
/// </summary>
/// <remarks>
/// Every one is behind <see cref="Permission.InventoryRead"/>, whose own definition already reads
/// "devices, <em>clients</em>, sites and their manual attributes" — so this package grants
/// nothing new, and the tests below are what say so rather than the comment.
/// </remarks>
public sealed class ClientReadTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private const string Laptop = "AA:BB:CC:00:00:21";
    private const string Desktop = "AE:BB:CC:00:00:42";

    /// <summary>
    /// A fixed instant, deliberately in the past. The resolve route refuses a moment that has
    /// not happened — every open interval covers every future instant, so answering one would
    /// dress the current holder up as a fact about the future — and a fixture dated "today at
    /// noon" is in the future for anybody who runs the suite in the morning.
    /// </summary>
    private static readonly DateTimeOffset Noon = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TheList_CarriesEachClientWithItsCurrentAddressAndPort()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await ClientFixtures.WalkAsync(
            host,
            deviceId,
            ClientFixtures.WalkResult(
                neighbors: [ClientFixtures.Neighbor("10.10.0.21", Laptop)],
                forwarding: [ClientFixtures.Forwarding(Laptop, ifIndex: 1, vlanId: 10)]),
            Cancellation);

        ApiResponse list = await host.Client.GetAsync("/api/v1/clients", Cancellation);

        list.Status.Should().Be(200);

        JsonElement client = list.Json.GetProperty("items")[0];

        client.GetProperty("macAddress").GetString().Should().Be(Laptop);
        client.GetProperty("ipAddress").GetString().Should().Be("10.10.0.21");
        client.GetProperty("deviceId").GetGuid().Should().Be(deviceId);
        client.GetProperty("ifIndex").GetInt32().Should().Be(1);
        client.GetProperty("vlanId").GetInt32().Should().Be(10);
        client.GetProperty("oui").GetString().Should().Be("AA:BB:CC");
        client.GetProperty("locallyAdministered").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task TheList_ShowsThePortThatLearnedTheFewestAddresses()
    {
        // A MAC is learned by every bridge on the path to it, so the list has to choose one for
        // its column. Fewest-learned is the evidence ordered by how much of it there is — the
        // access port learned one address and the uplink learned everything behind it.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid access = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host, Cancellation, "access-sw-01", "10.10.0.1");
        Guid distribution = await DiscoveryFixtures.CreateWalkableDeviceAsync(
            host, Cancellation, "dist-sw-01", "10.10.0.2");

        await ClientFixtures.WalkAsync(
            host,
            distribution,
            ClientFixtures.WalkResult(
                forwarding: [ClientFixtures.Forwarding(Laptop, ifIndex: 48, macCountOnPort: 214)]),
            Cancellation);

        await ClientFixtures.WalkAsync(
            host,
            access,
            ClientFixtures.WalkResult(
                forwarding: [ClientFixtures.Forwarding(Laptop, ifIndex: 1, macCountOnPort: 1)]),
            Cancellation);

        ApiResponse list = await host.Client.GetAsync("/api/v1/clients", Cancellation);

        list.Json.GetProperty("items")[0].GetProperty("deviceId").GetGuid().Should().Be(access);
    }

    [Fact]
    public async Task TheList_NarrowsByASearchTermWhateverKindOfValueItIs()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await ClientFixtures.WalkAsync(
            host,
            deviceId,
            ClientFixtures.WalkResult(
                neighbors:
                [
                    ClientFixtures.Neighbor("10.10.0.21", Laptop),
                    ClientFixtures.Neighbor("10.10.0.42", Desktop)
                ]),
            Cancellation);

        // A MAC in a spelling nobody stored it under.
        ApiResponse byMac = await host.Client.GetAsync(
            "/api/v1/clients?search=aabb.cc00.0021", Cancellation);

        byMac.Json.GetProperty("items").GetArrayLength().Should().Be(1);
        byMac.Json.GetProperty("items")[0].GetProperty("macAddress").GetString().Should().Be(Laptop);

        ApiResponse byAddress = await host.Client.GetAsync(
            "/api/v1/clients?search=10.10.0.42", Cancellation);

        byAddress.Json.GetProperty("items").GetArrayLength().Should().Be(1);
        byAddress.Json.GetProperty("items")[0].GetProperty("macAddress").GetString().Should().Be(Desktop);
    }

    [Fact]
    public async Task TheList_NarrowsByDeviceVlanAndWhetherAnAddressIsHeld()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await ClientFixtures.WalkAsync(
            host,
            deviceId,
            ClientFixtures.WalkResult(
                neighbors: [ClientFixtures.Neighbor("10.10.0.21", Laptop)],
                forwarding:
                [
                    ClientFixtures.Forwarding(Laptop, ifIndex: 1, vlanId: 10),
                    ClientFixtures.Forwarding(Desktop, ifIndex: 2, vlanId: 20)
                ]),
            Cancellation);

        (await Count(host, $"/api/v1/clients?deviceId={deviceId}")).Should().Be(2);
        (await Count(host, "/api/v1/clients?vlanId=20")).Should().Be(1);
        (await Count(host, "/api/v1/clients?onlyActive=true")).Should().Be(1,
            "only one of the two has ever been seen holding an address");
    }

    [Fact]
    public async Task TheList_IsPagedByACursorItIssued()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await ClientFixtures.WalkAsync(
            host,
            deviceId,
            ClientFixtures.WalkResult(
                forwarding:
                [
                    ClientFixtures.Forwarding(Laptop, ifIndex: 1),
                    ClientFixtures.Forwarding(Desktop, ifIndex: 2),
                    ClientFixtures.Forwarding("A2:BB:CC:00:00:03", ifIndex: 3)
                ]),
            Cancellation);

        ApiResponse first = await host.Client.GetAsync("/api/v1/clients?limit=2", Cancellation);

        first.Json.GetProperty("items").GetArrayLength().Should().Be(2);
        first.Json.GetProperty("totalCount").GetInt64().Should().Be(3);

        string cursor = first.Json.GetProperty("nextCursor").GetString()!;

        ApiResponse second = await host.Client.GetAsync(
            $"/api/v1/clients?limit=2&cursor={Uri.EscapeDataString(cursor)}", Cancellation);

        second.Json.GetProperty("items").GetArrayLength().Should().Be(1);
        second.Json.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task TheList_RefusesACursorItDidNotIssue()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse list = await host.Client.GetAsync("/api/v1/clients?cursor=not-a-cursor", Cancellation);

        list.Status.Should().Be(400);
    }

    [Fact]
    public async Task TheDetail_CarriesEveryOpenBinding()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await ClientFixtures.WalkAsync(
            host,
            deviceId,
            ClientFixtures.WalkResult(
                neighbors:
                [
                    ClientFixtures.Neighbor("10.10.0.21", Laptop),
                    // The same endpoint on IPv6 as well, which is an ordinary thing to hold.
                    ClientFixtures.Neighbor("2001:db8::21", Laptop)
                ],
                forwarding: [ClientFixtures.Forwarding(Laptop, ifIndex: 1)]),
            Cancellation);

        Guid clientId = (await host.ClientsAsync(Cancellation))[0].Id;

        ApiResponse detail = await host.Client.GetAsync($"/api/v1/clients/{clientId}", Cancellation);

        detail.Status.Should().Be(200);
        detail.Json.GetProperty("ipBindings").GetArrayLength().Should().Be(2);
        detail.Json.GetProperty("portBindings").GetArrayLength().Should().Be(1);
        detail.Json.GetProperty("portBindings")[0].GetProperty("deviceHostname")
            .GetString().Should().Be("switch-01");
    }

    [Fact]
    public async Task TheDetail_OfAClientThatIsNotThere_IsNotFound()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse detail = await host.Client.GetAsync(
            $"/api/v1/clients/{Guid.CreateVersion7()}", Cancellation);

        detail.Status.Should().Be(404);
        detail.Body.Should().Contain("client.not-found");
    }

    [Fact]
    public async Task TheAddressHistory_ReadsNewestFirstAndSaysWhichIntervalIsOpen()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid clientId = await host.SeedBindingAsync(
            Laptop, "10.10.0.9", Noon.AddHours(-3), Noon, Cancellation);

        await host.SeedBindingAsync(Laptop, "10.10.0.21", Noon, observedTo: null, Cancellation);

        ApiResponse history = await host.Client.GetAsync(
            $"/api/v1/clients/{clientId}/ip-history", Cancellation);

        history.Status.Should().Be(200);

        JsonElement items = history.Json.GetProperty("items");

        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("ipAddress").GetString().Should().Be("10.10.0.21");
        items[0].GetProperty("observedTo").ValueKind.Should().Be(JsonValueKind.Null);
        items[1].GetProperty("ipAddress").GetString().Should().Be("10.10.0.9");
        items[1].GetProperty("observedTo").GetDateTimeOffset().Should().Be(Noon);
    }

    [Fact]
    public async Task ThePortHistory_NamesTheInterfaceWhenAWalkHasRecordedOne()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        // The fingerprint walk is what fills in device_interfaces, so a port has a name only
        // once one has run. Before that it is an ifIndex and nothing more, which is honest.
        await DiscoveryFixtures.WalkAsync(
            host, deviceId, DiscoveryFixtures.WalkResult(interfaces: [1, 2]), Cancellation);

        await ClientFixtures.WalkAsync(
            host,
            deviceId,
            ClientFixtures.WalkResult(forwarding: [ClientFixtures.Forwarding(Laptop, ifIndex: 1)]),
            Cancellation);

        Guid clientId = (await host.ClientsAsync(Cancellation))[0].Id;

        ApiResponse history = await host.Client.GetAsync(
            $"/api/v1/clients/{clientId}/port-history", Cancellation);

        history.Json.GetProperty("items")[0].GetProperty("interfaceName")
            .GetString().Should().Be("Gi0/1");
    }

    [Fact]
    public async Task TheHistoryOfAClientThatIsNotThere_IsNotFoundRatherThanAnEmptyPage()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid missing = Guid.CreateVersion7();

        (await host.Client.GetAsync($"/api/v1/clients/{missing}/ip-history", Cancellation))
            .Status.Should().Be(404);
        (await host.Client.GetAsync($"/api/v1/clients/{missing}/port-history", Cancellation))
            .Status.Should().Be(404);
    }

    // --- The resolve route.

    [Fact]
    public async Task TheResolveRoute_AnswersWhatHeldTheAddressAtAnInstant()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid first = await host.SeedBindingAsync(
            Laptop, "10.10.0.21", Noon.AddHours(-3), Noon, Cancellation);
        Guid second = await host.SeedBindingAsync(
            Desktop, "10.10.0.21", Noon, observedTo: null, Cancellation);

        ApiResponse before = await Resolve(host, "10.10.0.21", Noon.AddMinutes(-1));

        before.Status.Should().Be(200);
        before.Json.GetProperty("kind").GetString().Should().Be("Client");
        before.Json.GetProperty("clientId").GetGuid().Should().Be(first);

        ApiResponse after = await Resolve(host, "10.10.0.21", Noon.AddMinutes(1));

        after.Json.GetProperty("clientId").GetGuid().Should().Be(second);
    }

    [Fact]
    public async Task TheResolveRoute_WithNoInstant_AnswersAboutNow()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid clientId = await host.SeedBindingAsync(
            Laptop, "10.10.0.21", DateTimeOffset.UtcNow.AddHours(-1), observedTo: null, Cancellation);

        ApiResponse resolved = await host.Client.GetAsync(
            "/api/v1/clients/resolve?ipAddress=10.10.0.21", Cancellation);

        resolved.Status.Should().Be(200);
        resolved.Json.GetProperty("clientId").GetGuid().Should().Be(clientId);
    }

    [Fact]
    public async Task TheResolveRoute_ForAnAddressNothingHasEverHeld_IsUnresolvedRatherThanNotFound()
    {
        // An address outside the estate is the ordinary case, and ARCHITECTURE.md §6 wants
        // enrichment to land an event with a null asset reference rather than to fail.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse resolved = await host.Client.GetAsync(
            "/api/v1/clients/resolve?ipAddress=203.0.113.9", Cancellation);

        resolved.Status.Should().Be(200);
        resolved.Json.GetProperty("kind").GetString().Should().Be("Unresolved");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("10.10.0.999")]
    public async Task TheResolveRoute_WithSomethingThatIsNotAnAddress_IsRefused(string address)
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse resolved = await host.Client.GetAsync(
            $"/api/v1/clients/resolve?ipAddress={Uri.EscapeDataString(address)}", Cancellation);

        resolved.Status.Should().Be(400);
        resolved.Body.Should().Contain("client.invalid-address");
    }

    [Fact]
    public async Task TheResolveRoute_ForAnInstantThatHasNotHappened_IsRefused()
    {
        // Every open interval covers every future instant, so the query would answer happily —
        // with the current holder, dressed as a fact about a moment that has not happened.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse resolved = await Resolve(host, "10.10.0.21", DateTimeOffset.UtcNow.AddDays(1));

        resolved.Status.Should().Be(400);
        resolved.Body.Should().Contain("client.future-instant");
    }

    // --- Authorization.

    [Theory]
    [InlineData(UserRole.ReadOnly)]
    [InlineData(UserRole.Analyst)]
    [InlineData(UserRole.Operator)]
    public async Task EveryRoleThatHoldsInventoryRead_MayReadClients(UserRole role)
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation, role);

        (await host.Client.GetAsync("/api/v1/clients", Cancellation)).Status.Should().Be(200);
        (await host.Client.GetAsync("/api/v1/clients/resolve?ipAddress=10.0.0.1", Cancellation))
            .Status.Should().Be(200);
    }

    [Theory]
    [InlineData(UserRole.ReadOnly)]
    [InlineData(UserRole.Analyst)]
    public async Task ARoleThatCannotStartADiscoveryRun_MayNotAskForAClientWalk(UserRole role)
    {
        // The walk is gated on DiscoveryRun rather than on InventoryRead, because it makes
        // NetShield open a credential and read a device outside its schedule.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await host.SignInAsync(role, Cancellation);

        (await ClientFixtures.RequestWalkAsync(host, deviceId, Cancellation))
            .Status.Should().Be(403);
    }

    [Fact]
    public async Task AnUnauthenticatedCaller_ReachesNothing()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        using HttpClient anonymous = new() { BaseAddress = host.BaseAddress };

        using HttpResponseMessage list = await anonymous.GetAsync(
            new Uri("/api/v1/clients", UriKind.Relative), Cancellation);

        ((int)list.StatusCode).Should().Be(401);
    }

    [Fact]
    public async Task AWalkRequest_WritesAnAuditRow()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await ClientFixtures.RequestWalkAsync(host, deviceId, Cancellation);

        IReadOnlyList<AuditRow> rows = await host.AuditRowsAsync(Cancellation);

        rows.Should().Contain(row =>
            row.Action == "inventory.device-client-walk" && row.TargetId == deviceId.ToString());
    }

    private static async Task<long> Count(InventoryHost host, string path)
    {
        ApiResponse list = await host.Client.GetAsync(path, Cancellation);

        list.Status.Should().Be(200);

        return list.Json.GetProperty("items").GetArrayLength();
    }

    private static Task<ApiResponse> Resolve(InventoryHost host, string address, DateTimeOffset at) =>
        host.Client.GetAsync(
            $"/api/v1/clients/resolve?ipAddress={address}&at={Uri.EscapeDataString(at.ToString("O"))}",
            Cancellation);
}
