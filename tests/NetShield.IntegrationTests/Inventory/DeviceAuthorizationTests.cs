using FluentAssertions;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

using NetShield.Platform.Auditing;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// Who may do what to a device. ARCHITECTURE.md §8 wants the check at the endpoint and again in
/// the module, and WP-1.1's endpoints are the first production call sites of either.
/// </summary>
public sealed class DeviceAuthorizationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Devices = "/api/v1/devices";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(UserRole.Administrator)]
    [InlineData(UserRole.Operator)]
    [InlineData(UserRole.Analyst)]
    [InlineData(UserRole.ReadOnly)]
    public async Task Read_EveryRole_IsPermitted(UserRole role)
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation, role);

        // Every role holds InventoryRead: an operations console whose inventory some of its users
        // cannot see is not one.
        (await host.Client.GetAsync(Devices, Cancellation)).Status.Should().Be(200);
    }

    [Theory]
    [InlineData(UserRole.Analyst)]
    [InlineData(UserRole.ReadOnly)]
    public async Task Write_ARoleWithoutInventoryWrite_IsRefusedWith403(UserRole role)
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation, role);

        ApiResponse refused = await host.Client.PostAsync(
            Devices,
            new CreateDeviceRequest("core-sw-01", "10.0.0.1"),
            Cancellation);

        refused.Status.Should().Be(403);
    }

    [Theory]
    [InlineData(UserRole.Administrator)]
    [InlineData(UserRole.Operator)]
    public async Task Write_ARoleWithInventoryWrite_IsPermitted(UserRole role)
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation, role);

        ApiResponse created = await host.Client.PostAsync(
            Devices,
            new CreateDeviceRequest("core-sw-01", "10.0.0.1"),
            Cancellation);

        created.Status.Should().Be(201);
    }

    [Fact]
    public async Task Update_ByARoleWithoutInventoryWrite_IsRefused()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse created = await host.Client.PostAsync(
            Devices,
            new CreateDeviceRequest("core-sw-01", "10.0.0.1"),
            Cancellation);

        Guid id = Guid.Parse(created.Member("id")!);

        await host.SignInAsync(UserRole.Analyst, Cancellation);

        (await host.Client.PutAsync(
            $"{Devices}/{id}",
            new UpdateDeviceRequest("core-sw-01", "10.0.0.2"),
            Cancellation)).Status.Should().Be(403);

        (await host.Client.DeleteAsync($"{Devices}/{id}", Cancellation)).Status.Should().Be(403);
    }

    [Fact]
    public async Task AnUnauthenticatedCaller_IsRefusedWith401()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        using HttpClient anonymous = new() { BaseAddress = host.BaseAddress };

        using HttpResponseMessage response = await anonymous.GetAsync(Devices, Cancellation);

        ((int)response.StatusCode).Should().Be(401);
    }

    /// <summary>
    /// WP-0.5 records a refused call as surely as a successful one, and the refusal is the half
    /// an operator reaches for first. A 403 that left no trace would be the gap.
    /// </summary>
    [Fact]
    public async Task ARefusedWrite_IsStillAudited()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            UserRole.ReadOnly);

        await host.Client.PostAsync(Devices, new CreateDeviceRequest("core-sw-01", "10.0.0.1"), Cancellation);

        IReadOnlyList<AuditRow> rows = await host.AuditRowsAsync(Cancellation);

        rows.Should().Contain(row =>
            row.Action == "inventory.device-create" && row.Outcome == AuditOutcome.Denied);
    }

    [Fact]
    public async Task ARefusedWrite_WritesNoDeviceAndNoOutboxRow()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            UserRole.ReadOnly);

        await host.Client.PostAsync(Devices, new CreateDeviceRequest("core-sw-01", "10.0.0.1"), Cancellation);

        (await host.OutboxEventNamesAsync(Cancellation)).Should().BeEmpty();
    }
}
