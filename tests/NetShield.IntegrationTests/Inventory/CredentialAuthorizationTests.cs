using FluentAssertions;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

using NetShield.Platform.Auditing;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// Who may do anything at all with a credential profile.
/// </summary>
/// <remarks>
/// WP-0.5 gave <see cref="Permission.CredentialsManage"/> to the Administrator alone, because
/// credential lifecycle is the highest-blast-radius privilege in the system. WP-1.2 puts reads
/// behind it too: there is no <c>CredentialsRead</c> member to gate them with, and a profile's
/// username is half of an SSH credential while the list of names says exactly which accounts
/// NetShield holds passwords for.
/// </remarks>
public sealed class CredentialAuthorizationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Profiles = "/api/v1/credential-profiles";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(UserRole.Operator)]
    [InlineData(UserRole.Analyst)]
    [InlineData(UserRole.ReadOnly)]
    public async Task Read_ARoleWithoutCredentialsManage_IsRefusedWith403(UserRole role)
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation, role);

        (await host.Client.GetAsync(Profiles, Cancellation)).Status.Should().Be(403);
    }

    [Theory]
    [InlineData(UserRole.Operator)]
    [InlineData(UserRole.Analyst)]
    [InlineData(UserRole.ReadOnly)]
    public async Task Create_ARoleWithoutCredentialsManage_IsRefusedWith403(UserRole role)
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation, role);

        ApiResponse refused = await host.Client.PostAsync(
            Profiles,
            new CreateCredentialProfileRequest(
                "Core community",
                CredentialKind.SnmpV2c,
                new CredentialMaterial(Community: "read-only")),
            Cancellation);

        refused.Status.Should().Be(403);
    }

    /// <summary>
    /// The Operator may change a device and may not change what it is reached with. That split is
    /// the point of a separate permission.
    /// </summary>
    [Fact]
    public async Task Assign_AnOperatorWhoMayEditTheDevice_IsStillRefusedTheCredentials()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            UserRole.Operator);

        ApiResponse device = await host.Client.PostAsync(
            "/api/v1/devices",
            new CreateDeviceRequest("core-sw-01", "10.0.0.1"),
            Cancellation);

        device.Status.Should().Be(201, "an Operator holds InventoryWrite");

        ApiResponse refused = await host.Client.PutAsync(
            $"/api/v1/devices/{device.Json.GetProperty("id").GetGuid()}/credential-profiles",
            new SetDeviceCredentialProfilesRequest([]),
            Cancellation);

        refused.Status.Should().Be(403);
    }

    [Fact]
    public async Task Create_AnAdministrator_IsPermitted()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse created = await host.Client.PostAsync(
            Profiles,
            new CreateCredentialProfileRequest(
                "Core community",
                CredentialKind.SnmpV2c,
                new CredentialMaterial(Community: "read-only")),
            Cancellation);

        created.Status.Should().Be(201);
    }

    /// <summary>
    /// A refused write is still recorded. WP-0.5's middleware writes the row after the call, so
    /// it exists for a request the handler never saw — which is the half of an audit log that
    /// matters most when something has gone wrong.
    /// </summary>
    [Fact]
    public async Task ARefusedWrite_StillWritesAnAuditRow_WithNothingOfTheMaterialInIt()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            UserRole.Analyst);

        await host.Client.PostAsync(
            Profiles,
            new CreateCredentialProfileRequest(
                "Core community",
                CredentialKind.SnmpV2c,
                new CredentialMaterial(Community: "zzq-refused-community")),
            Cancellation);

        IReadOnlyList<AuditRow> rows = await host.AuditRowsAsync(Cancellation);

        rows.Should().Contain(row =>
            row.Action == "inventory.credential-profile-create" && row.Outcome == AuditOutcome.Denied);

        host.RecordedLogs().Should().AllSatisfy(line => line.Should().NotContain("zzq-refused-community"));
    }

    /// <summary>An unauthenticated caller learns nothing, including whether the route exists.</summary>
    [Fact]
    public async Task Read_WithNoSession_Is401()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        using HttpClient anonymous = new() { BaseAddress = host.BaseAddress };

        using HttpResponseMessage response = await anonymous.GetAsync(new Uri(Profiles, UriKind.Relative), Cancellation);

        ((int)response.StatusCode).Should().Be(401);
    }
}
