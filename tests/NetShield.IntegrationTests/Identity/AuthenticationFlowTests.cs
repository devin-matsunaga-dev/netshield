using FluentAssertions;

using NetShield.Contracts.Identity;

using NetShield.Identity.Authentication;
using NetShield.Identity.Endpoints;

using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Identity;

/// <summary>
/// The WP-0.4 "Done when" list, walked end to end: login sets the cookie, a wrong password is a
/// 401 that says nothing about the account, and a session can be read back and ended.
/// </summary>
public sealed class AuthenticationFlowTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Username = "netadmin";
    private const string Password = "Correct-Horse-42";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Login_WithTheRightPassword_SetsBothCookies()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);
        await host.CreateUserAsync(Username, Password, Token);

        ApiResponse response = await host.LoginAsync(Username, Password, Token);

        response.Status.Should().Be(200);

        response.CookieHeader(SessionCookies.SessionCookieName).Should().NotBeNull()
            .And.Contain("httponly").And.Contain("secure").And.Contain("samesite=lax");

        response.CookieHeader(SessionCookies.RefreshCookieName).Should().NotBeNull()
            .And.Contain("httponly").And.Contain("secure").And.Contain("samesite=lax")
            .And.Contain($"path={SessionCookies.RefreshCookiePath}");
    }

    [Fact]
    public async Task Login_WithTheRightPassword_ReturnsTheUserAndNoSecret()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);
        await host.CreateUserAsync(Username, Password, Token, UserRole.Operator);

        ApiResponse response = await host.LoginAsync(Username, Password, Token);

        response.Member("username").Should().Be(Username);
        response.Member("role").Should().Be(nameof(UserRole.Operator));
        response.Member("mustChangePassword").Should().Be("False");

        response.Body.Should().NotContain(Password)
            .And.NotContain("passwordHash")
            .And.NotContain("refreshToken");
    }

    [Fact]
    public async Task Login_IsCaseInsensitiveOnTheUsername()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);
        await host.CreateUserAsync(Username, Password, Token);

        ApiResponse response = await host.LoginAsync("NetAdmin", Password, Token);

        response.Status.Should().Be(200);
    }

    [Fact]
    public async Task Login_WithAWrongPassword_Returns401AndSetsNoCookie()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);
        await host.CreateUserAsync(Username, Password, Token);

        ApiResponse response = await host.LoginAsync(Username, "Wrong-Horse-42", Token);

        response.Status.Should().Be(401);
        response.SetCookies.Should().BeEmpty();
    }

    [Fact]
    public async Task Login_WithAnUnknownUsername_AnswersExactlyAsAWrongPasswordDoes()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);
        await host.CreateUserAsync(Username, Password, Token);

        ApiResponse wrongPassword = await host.LoginAsync(Username, "Wrong-Horse-42", Token);
        ApiResponse unknownUser = await host.LoginAsync("nobody-at-all", "Wrong-Horse-42", Token);

        unknownUser.Status.Should().Be(wrongPassword.Status);
        unknownUser.Member("code").Should().Be(wrongPassword.Member("code"));
        unknownUser.Member("detail").Should().Be(wrongPassword.Member("detail"));
    }

    [Fact]
    public async Task Login_WithADisabledAccount_AnswersExactlyAsAWrongPasswordDoes()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);
        await host.CreateUserAsync(Username, Password, Token);
        await host.DisableAsync(Username, Token);

        ApiResponse response = await host.LoginAsync(Username, Password, Token);

        response.Status.Should().Be(401);
        response.Member("code").Should().Be("identity.invalid-credentials");
    }

    [Fact]
    public async Task Login_WithAMalformedBody_Returns400WithTheFieldNamed()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);

        ApiResponse response = await host.Client.PostAsync(
            $"{AuthenticationEndpoints.RoutePrefix}/login",
            new LoginRequest(string.Empty, string.Empty),
            Token);

        response.Status.Should().Be(400);
        response.Member("code").Should().Be(ValidationFilter<LoginRequest>.RejectionCode);
        response.Body.Should().Contain("username").And.Contain("password");
    }

    [Fact]
    public async Task Me_WithoutASession_Returns401AsProblemDetailsRatherThanARedirect()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);

        ApiResponse response = await host.Client.GetAsync(
            $"{AuthenticationEndpoints.RoutePrefix}/me",
            Token);

        response.Status.Should().Be(401);
        response.Member("traceId").Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Me_WithASession_ReturnsTheSignedInUser()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);
        await host.CreateUserAsync(Username, Password, Token, UserRole.Analyst);
        await host.LoginAsync(Username, Password, Token);

        ApiResponse response = await host.Client.GetAsync(
            $"{AuthenticationEndpoints.RoutePrefix}/me",
            Token);

        response.Status.Should().Be(200);
        response.Member("username").Should().Be(Username);
        response.Member("role").Should().Be(nameof(UserRole.Analyst));
    }

    [Fact]
    public async Task Me_ReadsTheAccountRatherThanTheCookie()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);
        await host.CreateUserAsync(Username, Password, Token);
        await host.LoginAsync(Username, Password, Token);

        await host.DisableAsync(Username, Token);

        ApiResponse response = await host.Client.GetAsync(
            $"{AuthenticationEndpoints.RoutePrefix}/me",
            Token);

        response.Status.Should().Be(401,
            "a cookie minted before the account was disabled cannot know that it was");
    }

    [Fact]
    public async Task Logout_ClearsBothCookiesAndRevokesTheChain()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);
        await host.CreateUserAsync(Username, Password, Token);
        await host.LoginAsync(Username, Password, Token);

        ApiResponse response = await host.Client.PostAsync(
            $"{AuthenticationEndpoints.RoutePrefix}/logout",
            Token);

        response.Status.Should().Be(204);

        host.Client.Cookie(SessionCookies.SessionCookieName).Should().BeNull();
        host.Client.Cookie(SessionCookies.RefreshCookieName).Should().BeNull();

        IReadOnlyList<RefreshToken> tokens = await host.ReadRefreshTokensAsync(Token);
        tokens.Should().OnlyContain(token => token.RevokedAt != null);
    }

    [Fact]
    public async Task Logout_WithoutASession_IsStillNoContent()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);

        ApiResponse response = await host.Client.PostAsync(
            $"{AuthenticationEndpoints.RoutePrefix}/logout",
            Token);

        response.Status.Should().Be(204, "signing out is idempotent and never leaves a client stuck");
    }

    [Fact]
    public async Task Login_StoresOnlyAHashOfTheRefreshToken()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);
        await host.CreateUserAsync(Username, Password, Token);
        await host.LoginAsync(Username, Password, Token);

        string presented = host.Client.Cookie(SessionCookies.RefreshCookieName)!;

        IReadOnlyList<RefreshToken> tokens = await host.ReadRefreshTokensAsync(Token);

        tokens.Should().ContainSingle()
            .Which.TokenHash.Should().NotBe(presented)
            .And.Be(RefreshTokenGenerator.Hash(presented));
    }

    [Fact]
    public async Task Login_StoresOnlyAHashOfThePassword()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);
        await host.CreateUserAsync(Username, Password, Token);

        NetShield.Identity.Users.User user = await host.ReadUserAsync(Username, Token);

        user.PasswordHash.Should().StartWith("$argon2id$").And.NotContain(Password);
    }
}
