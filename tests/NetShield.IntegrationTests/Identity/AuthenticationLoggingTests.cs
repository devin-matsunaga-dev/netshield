using FluentAssertions;

using NetShield.Contracts.Identity;

using NetShield.Identity.Authentication;
using NetShield.Identity.Endpoints;

using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Identity;

/// <summary>
/// WP-0.4: no password or token appears in any log at any level.
/// </summary>
/// <remarks>
/// The host records every line at <c>Trace</c> and above, behind the platform's redaction and
/// exactly where a console or OpenTelemetry sink would sit, so this asserts what would actually
/// have been shipped rather than what the default filters happened to drop.
/// </remarks>
public sealed class AuthenticationLoggingTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Username = "netadmin";
    private const string Password = "Correct-Horse-42";
    private const string NewPassword = "Another-Horse-77";
    private const string SeedPassword = "First-Run-Admin-91";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NoPasswordOrToken_ReachesTheLogAtAnyLevel()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token, SeedPassword);

        await host.CreateUserAsync(Username, Password, Token);

        // Every path that has a secret in its hand: a failure, a success, a rotation, a change.
        await host.LoginAsync(Username, "Wrong-Horse-42", Token);
        await host.LoginAsync(Username, Password, Token);

        string refreshToken = host.Client.Cookie(SessionCookies.RefreshCookieName)!;
        string sessionCookie = host.Client.Cookie(SessionCookies.SessionCookieName)!;

        await host.Client.PostAsync($"{AuthenticationEndpoints.RoutePrefix}/refresh", Token);
        await host.Client.PostAsync(
            $"{AuthenticationEndpoints.RoutePrefix}/password",
            new ChangePasswordRequest(Password, NewPassword),
            Token);
        await host.Client.GetAsync($"{AuthenticationEndpoints.RoutePrefix}/me", Token);
        await host.Client.PostAsync($"{AuthenticationEndpoints.RoutePrefix}/logout", Token);

        IReadOnlyList<string> secrets =
        [
            Password,
            NewPassword,
            SeedPassword,
            "Wrong-Horse-42",
            refreshToken,
            sessionCookie
        ];

        IReadOnlyList<string> written = [.. host.Logs.SelectMany(Rendered)];

        written.Should().NotBeEmpty("the sign-in path is expected to say something");

        foreach (string secret in secrets)
        {
            written.Should().NotContain(
                line => line.Contains(secret, StringComparison.Ordinal),
                $"no log line at any level may carry a credential (SPEC.md §5)");
        }
    }

    [Fact]
    public async Task TheStoredPasswordHash_NeverReachesTheLogEither()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);

        await host.CreateUserAsync(Username, Password, Token);
        await host.LoginAsync(Username, Password, Token);

        string hash = (await host.ReadUserAsync(Username, Token)).PasswordHash;

        host.Logs.SelectMany(Rendered).Should().NotContain(line => line.Contains(hash, StringComparison.Ordinal));
    }

    /// <summary>The message and every structured value, as a sink would have written them.</summary>
    private static IEnumerable<string> Rendered(RecordedLog record) => [record.Message, .. record.Values];
}
