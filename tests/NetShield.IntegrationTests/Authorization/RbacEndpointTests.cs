using FluentAssertions;

using NetShield.Contracts.Identity;

using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

using NetShield.Platform.Auditing;

namespace NetShield.IntegrationTests.Authorization;

/// <summary>
/// WP-0.5: an Analyst is refused a write with 403, and the refusal is on the record.
/// </summary>
/// <remarks>
/// Over real HTTP against the probe endpoints, because a policy that only holds in a hand-built
/// <c>AuthorizationHandlerContext</c> is a policy that has not been tested. The routes stand in
/// for the module endpoints Phase 1 has not built yet — see <see cref="ProbeEndpoints"/>.
/// </remarks>
public sealed class RbacEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Password = "Correct-Horse-42";
    private const string WritePath = $"{ProbeEndpoints.RoutePrefix}/inventory";
    private const string GuardedPath = $"{ProbeEndpoints.RoutePrefix}/guarded";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(UserRole.Analyst)]
    [InlineData(UserRole.ReadOnly)]
    public async Task ARoleWithoutTheWritePermission_IsRefusedWith403(UserRole role)
    {
        await using IdentityHost host = await SignedInAs(role);

        ApiResponse response = await host.Client.PostAsync(WritePath, Token);

        response.Status.Should().Be(403);
        response.Member("title").Should().NotBeNullOrEmpty("a refusal is problem details like any other failure");
        response.Member("traceId").Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData(UserRole.Administrator)]
    [InlineData(UserRole.Operator)]
    public async Task ARoleHoldingTheWritePermission_IsAllowed(UserRole role)
    {
        await using IdentityHost host = await SignedInAs(role);

        (await host.Client.PostAsync(WritePath, Token)).Status.Should().Be(200);
    }

    [Fact]
    public async Task EveryRole_MayStillRead()
    {
        foreach (UserRole role in Enum.GetValues<UserRole>())
        {
            await using IdentityHost host = await SignedInAs(role);

            (await host.Client.GetAsync(WritePath, Token)).Status.Should().Be(
                200,
                "every role holds InventoryRead");
        }
    }

    [Fact]
    public async Task AnAnonymousCaller_IsRefusedWith401()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);

        (await host.Client.PostAsync(WritePath, Token)).Status.Should().Be(401);
    }

    [Fact]
    public async Task AnEndpointDeclaringNoPolicy_IsRefusedRatherThanPublished()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);

        // Deny by default. A route added without a policy fails closed, which is a 401 in
        // development rather than an open endpoint in production.
        (await host.Client.PostAsync($"{ProbeEndpoints.RoutePrefix}/unpoliced", Token))
            .Status.Should().Be(401);
    }

    [Fact]
    public async Task TheResourceGuard_RefusesTheSameRoleTheEndpointCheckWould()
    {
        await using IdentityHost host = await SignedInAs(UserRole.Analyst);

        // The endpoint here asks only for a session; the handler makes the permission check for
        // itself, which is the module-level half of ARCHITECTURE.md §8.
        (await host.Client.PostAsync(GuardedPath, Token)).Status.Should().Be(403);
    }

    [Fact]
    public async Task TheResourceGuard_AllowsARoleThatHoldsThePermission()
    {
        await using IdentityHost host = await SignedInAs(UserRole.Operator);

        // 204: the probe hands the guard's own Result to the endpoint mapper, and a bare
        // successful Result carries no body (CONVENTIONS.md §4).
        (await host.Client.PostAsync(GuardedPath, Token)).Status.Should().Be(204);
    }

    [Fact]
    public async Task ARefusedWrite_IsRecordedAsDenied()
    {
        await using IdentityHost host = await SignedInAs(UserRole.Analyst);

        await host.Client.PostAsync(WritePath, Token);

        AuditEntry denial = (await host.ReadAuditEntriesAsync(Token))
            .Last(entry => entry.Action == "inventory.write");

        denial.Outcome.Should().Be(AuditOutcome.Denied);
        denial.StatusCode.Should().Be(403);
        denial.ActorRole.Should().Be(UserRole.Analyst);
        denial.ActorUsername.Should().Be(UserRole.Analyst.ToString());
        denial.SourceIp.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task TheGuardsRefusal_NamesTheResourceItRefused()
    {
        await using IdentityHost host = await SignedInAs(UserRole.Analyst);

        await host.Client.PostAsync(GuardedPath, Token);

        AuditEntry denial = (await host.ReadAuditEntriesAsync(Token))
            .Last(entry => entry.Action == "inventory.guarded-write");

        denial.Outcome.Should().Be(AuditOutcome.Denied);
        denial.TargetType.Should().Be("device");
        denial.TargetId.Should().Be("probe-1", "a refusal that does not say what was refused is a row nobody can act on");
    }

    /// <summary>A host with one account in <paramref name="role"/>, already signed in.</summary>
    private async Task<IdentityHost> SignedInAs(UserRole role)
    {
        IdentityHost host = await IdentityHost.StartAsync(postgres, Token);

        string username = role.ToString();

        await host.CreateUserAsync(username, Password, Token, role);
        (await host.LoginAsync(username, Password, Token)).Status.Should().Be(200);

        return host;
    }
}
