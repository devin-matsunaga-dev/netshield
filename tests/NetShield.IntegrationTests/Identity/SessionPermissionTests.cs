using FluentAssertions;

using NetShield.Contracts.Identity;

using NetShield.Identity.Endpoints;

using NetShield.IntegrationTests.Platform;

using NetShield.Platform.Authorization;

namespace NetShield.IntegrationTests.Identity;

/// <summary>
/// What a session is told it may do, over HTTP. The SPA hides a nav entry and a write control
/// on this list (WP-0.7), so it has to arrive on every response that describes a session and it
/// has to agree with the table authorization actually consults.
/// </summary>
public sealed class SessionPermissionTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Username = "netadmin";
    private const string Password = "Correct-Horse-42";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(UserRole.Administrator)]
    [InlineData(UserRole.Operator)]
    [InlineData(UserRole.Analyst)]
    [InlineData(UserRole.ReadOnly)]
    public async Task Login_ReturnsExactlyThePermissionsTheRoleTableGrants(UserRole role)
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);
        await host.CreateUserAsync(Username, Password, Token, role);

        ApiResponse response = await host.LoginAsync(Username, Password, Token);

        PermissionsIn(response).Should().BeEquivalentTo(RolePermissions.For(role));
    }

    [Fact]
    public async Task CurrentUser_ReturnsThePermissionsToo()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);
        await host.CreateUserAsync(Username, Password, Token, UserRole.Analyst);
        await host.LoginAsync(Username, Password, Token);

        ApiResponse response = await host.Client.GetAsync(
            $"{AuthenticationEndpoints.RoutePrefix}/me",
            Token);

        response.Status.Should().Be(200);
        PermissionsIn(response).Should().BeEquivalentTo(RolePermissions.For(UserRole.Analyst));
    }

    [Fact]
    public async Task Refresh_ReturnsThePermissionsToo()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);
        await host.CreateUserAsync(Username, Password, Token, UserRole.Operator);
        await host.LoginAsync(Username, Password, Token);

        ApiResponse response = await host.Client.PostAsync(
            $"{AuthenticationEndpoints.RoutePrefix}/refresh",
            Token);

        response.Status.Should().Be(200);
        PermissionsIn(response).Should().BeEquivalentTo(RolePermissions.For(UserRole.Operator));
    }

    [Fact]
    public async Task Login_AsAReadOnlyUser_ReturnsNoWritePermission()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);
        await host.CreateUserAsync(Username, Password, Token, UserRole.ReadOnly);

        ApiResponse response = await host.LoginAsync(Username, Password, Token);

        PermissionsIn(response).Should().NotContain(
        [
            Permission.InventoryWrite,
            Permission.PoliciesWrite,
            Permission.AuditRead,
            Permission.SystemAdminister
        ]);
    }

    [Fact]
    public async Task Login_SerialisesAPermissionAsItsNameRatherThanItsOrdinal()
    {
        // An ordinal would renumber every stored response and every generated client the moment
        // a member is inserted into Permission (WP-0.4 settled the same for UserRole).
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);
        await host.CreateUserAsync(Username, Password, Token, UserRole.ReadOnly);

        ApiResponse response = await host.LoginAsync(Username, Password, Token);

        response.Body.Should().Contain($"\"{nameof(Permission.InventoryRead)}\"");
    }

    private static IReadOnlyList<Permission> PermissionsIn(ApiResponse response) =>
    [
        .. response.Json.GetProperty("permissions").EnumerateArray()
            .Select(element => Enum.Parse<Permission>(element.GetString() ?? string.Empty))
    ];
}
