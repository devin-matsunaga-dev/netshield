using FluentAssertions;

using NetShield.Identity.Authentication;
using NetShield.Identity.Endpoints;

using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Identity;

/// <summary>
/// WP-0.4: refresh rotates and invalidates the prior token. Rotation is what makes a stolen
/// refresh cookie detectable rather than merely long-lived.
/// </summary>
public sealed class RefreshRotationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Username = "netadmin";
    private const string Password = "Correct-Horse-42";
    private const string RefreshPath = $"{AuthenticationEndpoints.RoutePrefix}/refresh";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Refresh_IssuesANewTokenAndRevokesTheOne_ItWasGiven()
    {
        await using IdentityHost host = await StartSignedInAsync();

        string first = host.Client.Cookie(SessionCookies.RefreshCookieName)!;

        ApiResponse response = await host.Client.PostAsync(RefreshPath, Token);

        response.Status.Should().Be(200);

        string second = host.Client.Cookie(SessionCookies.RefreshCookieName)!;
        second.Should().NotBe(first);

        IReadOnlyList<RefreshToken> tokens = await host.ReadRefreshTokensAsync(Token);

        tokens.Should().HaveCount(2);
        tokens[0].RevokedAt.Should().NotBeNull("the presented token is spent by the rotation");
        tokens[0].ReplacedByTokenId.Should().Be(tokens[1].Id);
        tokens[1].RevokedAt.Should().BeNull();
        tokens[1].SessionId.Should().Be(tokens[0].SessionId, "a rotation continues the same session");
    }

    [Fact]
    public async Task Refresh_IssuesAFreshSessionCookieToo()
    {
        await using IdentityHost host = await StartSignedInAsync();

        ApiResponse response = await host.Client.PostAsync(RefreshPath, Token);

        response.CookieHeader(SessionCookies.SessionCookieName).Should().NotBeNull()
            .And.Contain("httponly").And.Contain("secure").And.Contain("samesite=lax");
    }

    [Fact]
    public async Task ThePriorToken_IsRefusedAfterARotation()
    {
        await using IdentityHost host = await StartSignedInAsync();

        string first = host.Client.Cookie(SessionCookies.RefreshCookieName)!;

        await host.Client.PostAsync(RefreshPath, Token);

        host.Client.SetCookie(SessionCookies.RefreshCookieName, first);

        ApiResponse replay = await host.Client.PostAsync(RefreshPath, Token);

        replay.Status.Should().Be(401);
    }

    [Fact]
    public async Task ReplayingASpentToken_RevokesTheWholeChain()
    {
        await using IdentityHost host = await StartSignedInAsync();

        string first = host.Client.Cookie(SessionCookies.RefreshCookieName)!;

        await host.Client.PostAsync(RefreshPath, Token);

        string live = host.Client.Cookie(SessionCookies.RefreshCookieName)!;

        // The attacker presents the copy they took before the legitimate holder rotated.
        host.Client.SetCookie(SessionCookies.RefreshCookieName, first);
        await host.Client.PostAsync(RefreshPath, Token);

        // The legitimate holder's own token is gone too, which is the point.
        host.Client.SetCookie(SessionCookies.RefreshCookieName, live);
        ApiResponse response = await host.Client.PostAsync(RefreshPath, Token);

        response.Status.Should().Be(401);

        IReadOnlyList<RefreshToken> tokens = await host.ReadRefreshTokensAsync(Token);
        tokens.Should().OnlyContain(token => token.RevokedAt != null);
    }

    [Fact]
    public async Task Refresh_WithoutACookie_Is401AndSetsNothing()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);

        ApiResponse response = await host.Client.PostAsync(RefreshPath, Token);

        response.Status.Should().Be(401);
        response.Member("code").Should().Be("identity.no-session");
    }

    [Fact]
    public async Task Refresh_WithAnUnknownToken_ClearsTheCookiesRatherThanLeavingThemToRetry()
    {
        await using IdentityHost host = await StartSignedInAsync();

        host.Client.SetCookie(SessionCookies.RefreshCookieName, "a-token-that-was-never-issued");

        ApiResponse response = await host.Client.PostAsync(RefreshPath, Token);

        response.Status.Should().Be(401);
        host.Client.Cookie(SessionCookies.RefreshCookieName).Should().BeNull();
        host.Client.Cookie(SessionCookies.SessionCookieName).Should().BeNull();
    }

    [Fact]
    public async Task AnExpiredToken_IsRefused()
    {
        await using IdentityHost host = await StartSignedInAsync();

        host.Time.Advance(TimeSpan.FromDays(15));

        ApiResponse response = await host.Client.PostAsync(RefreshPath, Token);

        response.Status.Should().Be(401, "the default refresh lifetime is fourteen days");
    }

    [Fact]
    public async Task AfterALogout_TheRefreshTokenIsRefused()
    {
        await using IdentityHost host = await StartSignedInAsync();

        string refresh = host.Client.Cookie(SessionCookies.RefreshCookieName)!;

        await host.Client.PostAsync($"{AuthenticationEndpoints.RoutePrefix}/logout", Token);

        host.Client.SetCookie(SessionCookies.RefreshCookieName, refresh);

        ApiResponse response = await host.Client.PostAsync(RefreshPath, Token);

        response.Status.Should().Be(401);
    }

    [Fact]
    public async Task ARefreshedSession_CanStillReadWhoItIs()
    {
        await using IdentityHost host = await StartSignedInAsync();

        await host.Client.PostAsync(RefreshPath, Token);

        ApiResponse response = await host.Client.GetAsync(
            $"{AuthenticationEndpoints.RoutePrefix}/me",
            Token);

        response.Status.Should().Be(200);
        response.Member("username").Should().Be(Username);
    }

    private async Task<IdentityHost> StartSignedInAsync()
    {
        IdentityHost host = await IdentityHost.StartAsync(postgres, Token);

        await host.CreateUserAsync(Username, Password, Token);
        await host.LoginAsync(Username, Password, Token);

        return host;
    }
}
