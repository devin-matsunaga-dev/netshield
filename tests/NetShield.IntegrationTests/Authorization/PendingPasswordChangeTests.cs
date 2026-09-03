using FluentAssertions;

using NetShield.Contracts.Identity;

using NetShield.Identity.Endpoints;

using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Authorization;

/// <summary>
/// Closes the gap STATUS.md recorded against WP-0.4: nothing enforced <c>must_change_password</c>
/// globally, because the refusal belongs with the authorization pipeline. This is that refusal.
/// </summary>
public sealed class PendingPasswordChangeTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Username = "netadmin";
    private const string Password = "Correct-Horse-42";
    private const string NewPassword = "Battery-Staple-91";
    private const string WritePath = $"{ProbeEndpoints.RoutePrefix}/inventory";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AUserOwingAPasswordChange_IsRefusedEverywhereElse()
    {
        await using IdentityHost host = await SignedInOwingAChange();

        (await host.Client.PostAsync(WritePath, Token)).Status.Should().Be(403);
        (await host.Client.GetAsync(WritePath, Token)).Status.Should().Be(403,
            "a read is refused too — the account is not usable until the change is made");
    }

    [Fact]
    public async Task AUserOwingAPasswordChange_MayStillAskWhoTheyAre()
    {
        await using IdentityHost host = await SignedInOwingAChange();

        ApiResponse response = await host.Client.GetAsync(
            $"{AuthenticationEndpoints.RoutePrefix}/me",
            Token);

        response.Status.Should().Be(200);
        response.Json.GetProperty("mustChangePassword").GetBoolean().Should().BeTrue(
            "this is how WP-0.7 knows to send the user to the change screen");
    }

    [Fact]
    public async Task AUserOwingAPasswordChange_MayChangeIt_AndIsThenLetThrough()
    {
        await using IdentityHost host = await SignedInOwingAChange();

        ApiResponse changed = await host.Client.PostAsync(
            $"{AuthenticationEndpoints.RoutePrefix}/password",
            new ChangePasswordRequest(Password, NewPassword),
            Token);

        changed.Status.Should().Be(200);

        // The change re-issues the session, so the claim that was refusing every request is gone
        // without the user having to sign in again.
        (await host.Client.PostAsync(WritePath, Token)).Status.Should().Be(200);
    }

    [Fact]
    public async Task AUserOwingNothing_IsNotAffected()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);

        await host.CreateUserAsync(Username, Password, Token, UserRole.Administrator);
        await host.LoginAsync(Username, Password, Token);

        (await host.Client.PostAsync(WritePath, Token)).Status.Should().Be(200);
    }

    private async Task<IdentityHost> SignedInOwingAChange()
    {
        IdentityHost host = await IdentityHost.StartAsync(postgres, Token);

        await host.CreateUserAsync(
            Username,
            Password,
            Token,
            UserRole.Administrator,
            mustChangePassword: true);

        (await host.LoginAsync(Username, Password, Token)).Status.Should().Be(200,
            "signing in is how the user reaches the change screen at all");

        return host;
    }
}
