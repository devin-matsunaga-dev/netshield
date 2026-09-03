using FluentAssertions;

using NetShield.Contracts.Identity;

using NetShield.Identity.Authentication;
using NetShield.Identity.Endpoints;
using NetShield.Identity.Passwords;
using NetShield.Identity.Users;

using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Identity;

/// <summary>
/// The way out of a forced first-run change, and the one endpoint that can replace a stored hash.
/// </summary>
public sealed class ChangePasswordTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Username = "netadmin";
    private const string Password = "Correct-Horse-42";
    private const string NewPassword = "Another-Horse-77";
    private const string PasswordPath = $"{AuthenticationEndpoints.RoutePrefix}/password";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ChangePassword_ClearsTheForcedChangeAndLetsTheNewPasswordSignIn()
    {
        await using IdentityHost host = await StartSignedInAsync(mustChangePassword: true);

        ApiResponse changed = await host.Client.PostAsync(
            PasswordPath,
            new ChangePasswordRequest(Password, NewPassword),
            Token);

        changed.Status.Should().Be(200);
        changed.Member("mustChangePassword").Should().Be("False");

        await host.Client.PostAsync($"{AuthenticationEndpoints.RoutePrefix}/logout", Token);

        ApiResponse signedIn = await host.LoginAsync(Username, NewPassword, Token);
        signedIn.Status.Should().Be(200);
    }

    [Fact]
    public async Task ChangePassword_RefusesTheOldPasswordAfterwards()
    {
        await using IdentityHost host = await StartSignedInAsync();

        await host.Client.PostAsync(PasswordPath, new ChangePasswordRequest(Password, NewPassword), Token);
        await host.Client.PostAsync($"{AuthenticationEndpoints.RoutePrefix}/logout", Token);

        ApiResponse response = await host.LoginAsync(Username, Password, Token);

        response.Status.Should().Be(401);
    }

    [Fact]
    public async Task ChangePassword_WithoutASession_Is401()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);

        ApiResponse response = await host.Client.PostAsync(
            PasswordPath,
            new ChangePasswordRequest(Password, NewPassword),
            Token);

        response.Status.Should().Be(401);
    }

    [Fact]
    public async Task ChangePassword_WithTheWrongCurrentPassword_Is422RatherThan401()
    {
        await using IdentityHost host = await StartSignedInAsync();

        ApiResponse response = await host.Client.PostAsync(
            PasswordPath,
            new ChangePasswordRequest("Not-The-Password-9", NewPassword),
            Token);

        response.Status.Should().Be(422,
            "the session is valid; answering 401 would make a typo look like an expired session");
        response.Member("code").Should().Be("identity.current-password-invalid");
    }

    [Fact]
    public async Task ChangePassword_WithAPasswordThePolicyRefuses_Is422AndListsTheRules()
    {
        await using IdentityHost host = await StartSignedInAsync();

        ApiResponse response = await host.Client.PostAsync(
            PasswordPath,
            new ChangePasswordRequest(Password, "short"),
            Token);

        response.Status.Should().Be(422);
        response.Member("code").Should().Be(PasswordPolicy.RejectionCode);
        response.Body.Should().Contain("newPassword");
    }

    [Fact]
    public async Task ChangePassword_ToTheSamePassword_IsRefused()
    {
        await using IdentityHost host = await StartSignedInAsync();

        ApiResponse response = await host.Client.PostAsync(
            PasswordPath,
            new ChangePasswordRequest(Password, Password),
            Token);

        response.Status.Should().Be(422);
        response.Member("code").Should().Be("identity.password-unchanged");
    }

    [Fact]
    public async Task ChangePassword_RevokesEveryOtherSessionAndKeepsTheCallersOwn()
    {
        await using IdentityHost host = await StartSignedInAsync();

        // A second sign-in, as the same account on another browser.
        await host.LoginAsync(Username, Password, Token);
        string elsewhere = host.Client.Cookie(SessionCookies.RefreshCookieName)!;

        await host.LoginAsync(Username, Password, Token);

        ApiResponse changed = await host.Client.PostAsync(
            PasswordPath,
            new ChangePasswordRequest(Password, NewPassword),
            Token);

        changed.Status.Should().Be(200);

        // The caller's own new session works.
        ApiResponse mine = await host.Client.PostAsync($"{AuthenticationEndpoints.RoutePrefix}/refresh", Token);
        mine.Status.Should().Be(200);

        // The other browser's does not.
        host.Client.SetCookie(SessionCookies.RefreshCookieName, elsewhere);
        ApiResponse theirs = await host.Client.PostAsync($"{AuthenticationEndpoints.RoutePrefix}/refresh", Token);
        theirs.Status.Should().Be(401);
    }

    [Fact]
    public async Task ChangePassword_RecordsWhenTheHashWasReplaced()
    {
        await using IdentityHost host = await StartSignedInAsync();

        // Inside the session lifetime: advancing past it would expire the cookie first.
        host.Time.Advance(TimeSpan.FromMinutes(5));

        await host.Client.PostAsync(PasswordPath, new ChangePasswordRequest(Password, NewPassword), Token);

        User user = await host.ReadUserAsync(Username, Token);

        user.PasswordChangedAt.Should().Be(host.Time.GetUtcNow());
        user.PasswordHash.Should().StartWith("$argon2id$").And.NotContain(NewPassword);
    }

    private async Task<IdentityHost> StartSignedInAsync(bool mustChangePassword = false)
    {
        IdentityHost host = await IdentityHost.StartAsync(postgres, Token);

        await host.CreateUserAsync(
            Username,
            Password,
            Token,
            UserRole.Administrator,
            mustChangePassword);

        await host.LoginAsync(Username, Password, Token);

        return host;
    }
}
