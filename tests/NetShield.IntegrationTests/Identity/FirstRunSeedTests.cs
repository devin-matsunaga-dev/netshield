using FluentAssertions;

using Microsoft.Extensions.Logging;

using NetShield.Contracts.Identity;

using NetShield.Identity.Users;

using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Identity;

/// <summary>
/// The seeded first-run administrator: created once, forced to change its password, and never
/// created twice.
/// </summary>
public sealed class FirstRunSeedTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string SeedPassword = "First-Run-Admin-91";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OnFirstRun_TheAdministratorIsCreatedWithAForcedPasswordChange()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token, SeedPassword);

        IReadOnlyList<User> users = await host.ReadUsersAsync(Token);

        users.Should().ContainSingle();
        users[0].Username.Should().Be("admin");
        users[0].Role.Should().Be(UserRole.Administrator);
        users[0].MustChangePassword.Should().BeTrue();
        users[0].IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task TheSeededAdministrator_CanSignInAndIsToldToChangeItsPassword()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token, SeedPassword);

        ApiResponse response = await host.LoginAsync("admin", SeedPassword, Token);

        response.Status.Should().Be(200);
        response.Member("mustChangePassword").Should().Be("True");
    }

    [Fact]
    public async Task TheSeedPassword_IsStoredOnlyAsAHash()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token, SeedPassword);

        IReadOnlyList<User> users = await host.ReadUsersAsync(Token);

        users[0].PasswordHash.Should().StartWith("$argon2id$").And.NotContain(SeedPassword);
    }

    [Fact]
    public async Task WithNoPasswordConfigured_NoAccountIsCreatedAndTheReasonIsLogged()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);

        (await host.ReadUsersAsync(Token)).Should().BeEmpty();

        host.Logs.Should().Contain(record =>
            record.Level == LogLevel.Warning
            && record.Message.Contains("first-run administrator", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithAPasswordThePolicyRefuses_NoAccountIsCreated()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token, "admin");

        (await host.ReadUsersAsync(Token)).Should().BeEmpty(
            "the one account that administers the system is not the one exempt from the policy");

        host.Logs.Should().Contain(record => record.Level == LogLevel.Error);
    }

    [Fact]
    public async Task WithNoMigrationApplied_TheHostStillStartsAndSaysWhy()
    {
        // Nothing applies migrations at run time yet (STATUS.md). A seeding step must not be the
        // reason the whole API fails to start.
        await using IdentityHost host = await IdentityHost.StartAsync(
            postgres,
            Token,
            SeedPassword,
            applyMigrations: false);

        ApiResponse response = await host.LoginAsync("admin", SeedPassword, Token);

        response.Status.Should().Be(500, "the endpoint fails, but the host is up to say so");

        host.Logs.Should().Contain(record =>
            record.Level == LogLevel.Error
            && record.Message.Contains("does not exist", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenAnAccountAlreadyExists_TheSeederDoesNothing()
    {
        await using IdentityHost first = await IdentityHost.StartAsync(postgres, Token, SeedPassword);

        // The seeded administrator changes its password, as it is made to.
        await first.LoginAsync("admin", SeedPassword, Token);
        await first.Client.PostAsync(
            $"{NetShield.Identity.Endpoints.AuthenticationEndpoints.RoutePrefix}/password",
            new ChangePasswordRequest(SeedPassword, "Changed-It-Already-5"),
            Token);

        IReadOnlyList<User> users = await first.ReadUsersAsync(Token);

        users.Should().ContainSingle();
        users[0].MustChangePassword.Should().BeFalse(
            "a restart must not put the account back to the configured password");
    }
}
