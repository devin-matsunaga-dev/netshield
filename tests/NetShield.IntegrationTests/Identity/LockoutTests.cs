using FluentAssertions;

using Microsoft.Extensions.Logging;

using NetShield.Identity.Users;

using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Identity;

/// <summary>
/// WP-0.4: five failures lock the account. The lock has to be invisible in the response and
/// visible in the log, which is the whole of the trade the human approved.
/// </summary>
public sealed class LockoutTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Username = "netadmin";
    private const string Password = "Correct-Horse-42";
    private const string WrongPassword = "Wrong-Horse-42";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FiveFailures_LockTheAccount()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);
        await host.CreateUserAsync(Username, Password, Token);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            await host.LoginAsync(Username, WrongPassword, Token);
        }

        User user = await host.ReadUserAsync(Username, Token);

        user.IsLockedOut(host.Time.GetUtcNow()).Should().BeTrue();
    }

    [Fact]
    public async Task FourFailures_DoNotLockTheAccount()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);
        await host.CreateUserAsync(Username, Password, Token);

        for (int attempt = 0; attempt < 4; attempt++)
        {
            await host.LoginAsync(Username, WrongPassword, Token);
        }

        User user = await host.ReadUserAsync(Username, Token);

        user.IsLockedOut(host.Time.GetUtcNow()).Should().BeFalse();
        user.FailedLoginAttempts.Should().Be(4);
    }

    [Fact]
    public async Task OnceLocked_TheRightPasswordIsStillRefused()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);
        await host.CreateUserAsync(Username, Password, Token);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            await host.LoginAsync(Username, WrongPassword, Token);
        }

        ApiResponse response = await host.LoginAsync(Username, Password, Token);

        response.Status.Should().Be(401);
        response.Member("code").Should().Be("identity.invalid-credentials",
            "a locked account and a wrong password are the same 401 to the caller");
        response.SetCookies.Should().BeEmpty();
    }

    [Fact]
    public async Task ALockoutIsRecordedInTheLog_WhereTheCallerCannotSeeIt()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);
        await host.CreateUserAsync(Username, Password, Token);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            await host.LoginAsync(Username, WrongPassword, Token);
        }

        await host.LoginAsync(Username, Password, Token);

        host.Logs.Should().Contain(record =>
            record.Level == LogLevel.Warning && record.Message.Contains("locked", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WhenTheLockoutLapses_TheRightPasswordWorksAgain()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);
        await host.CreateUserAsync(Username, Password, Token);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            await host.LoginAsync(Username, WrongPassword, Token);
        }

        host.Time.Advance(TimeSpan.FromMinutes(16));

        ApiResponse response = await host.LoginAsync(Username, Password, Token);

        response.Status.Should().Be(200);
    }

    [Fact]
    public async Task ASuccessfulSignIn_ClearsTheFailureCount()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);
        await host.CreateUserAsync(Username, Password, Token);

        await host.LoginAsync(Username, WrongPassword, Token);
        await host.LoginAsync(Username, WrongPassword, Token);
        await host.LoginAsync(Username, Password, Token);

        User user = await host.ReadUserAsync(Username, Token);

        user.FailedLoginAttempts.Should().Be(0);
        user.LockedOutUntil.Should().BeNull();
        user.LastLoginAt.Should().Be(host.Time.GetUtcNow());
    }

    [Fact]
    public async Task AnUnknownUsername_NeverCreatesLockoutState()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            await host.LoginAsync("nobody-at-all", WrongPassword, Token);
        }

        IReadOnlyList<User> users = await host.ReadUsersAsync(Token);

        users.Should().BeEmpty("guessing at a name that does not exist must not create anything");
    }
}
