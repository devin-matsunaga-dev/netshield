using System.Text.Json;

using FluentAssertions;

using NetShield.Contracts.Identity;

using NetShield.Identity.Endpoints;

using NetShield.IntegrationTests.Authorization;
using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

using NetShield.Platform.Auditing;
using NetShield.Platform.Logging;

namespace NetShield.IntegrationTests.Auditing;

/// <summary>
/// SPEC.md §5: every state-changing API call is recorded with actor, source IP, target, and
/// before/after where applicable — automatically, without the endpoint asking.
/// </summary>
public sealed class AuditRecordingTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Username = "netadmin";
    private const string Password = "Correct-Horse-42";
    private const string NewPassword = "Battery-Staple-91";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASuccessfulSignIn_IsRecordedWithItsActorAndItsSource()
    {
        await using IdentityHost host = await HostWithAccount();

        await host.LoginAsync(Username, Password, Token);

        AuditEntry entry = (await host.ReadAuditEntriesAsync(Token)).Should().ContainSingle().Subject;

        entry.Action.Should().Be("identity.login");
        entry.Outcome.Should().Be(AuditOutcome.Succeeded);
        entry.StatusCode.Should().Be(200);
        entry.HttpMethod.Should().Be("POST");
        entry.Path.Should().Be($"{AuthenticationEndpoints.RoutePrefix}/login");
        entry.ActorUsername.Should().Be(Username);
        entry.ActorUserId.Should().NotBeNull();
        entry.ActorRole.Should().Be(UserRole.Administrator);
        entry.TargetType.Should().Be("user");
        entry.TargetId.Should().Be(entry.ActorUserId!.Value.ToString());
        entry.SourceIp.Should().NotBeNullOrEmpty();
        entry.TraceId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AFailedSignIn_IsRecordedAsDenied_AndNamesTheAccountItWasAgainst()
    {
        await using IdentityHost host = await HostWithAccount();

        await host.LoginAsync(Username, "Wrong-Horse-42", Token);

        AuditEntry entry = (await host.ReadAuditEntriesAsync(Token)).Should().ContainSingle().Subject;

        entry.Outcome.Should().Be(AuditOutcome.Denied);
        entry.StatusCode.Should().Be(401);
        entry.ActorUsername.Should().Be(Username);
    }

    [Fact]
    public async Task ASignInAgainstAUsernameNobodyHolds_IsRecordedWithoutATarget()
    {
        await using IdentityHost host = await HostWithAccount();

        await host.LoginAsync("nobody-at-all", Password, Token);

        AuditEntry entry = (await host.ReadAuditEntriesAsync(Token)).Should().ContainSingle().Subject;

        entry.Outcome.Should().Be(AuditOutcome.Denied);

        // The username field is where a password gets typed by mistake, and an append-only table
        // is the wrong place for one to land.
        entry.ActorUsername.Should().BeNull();
        entry.TargetId.Should().BeNull();
    }

    [Fact]
    public async Task ALockout_IsVisibleInTheBeforeAndAfterOfTheAttemptThatCausedIt()
    {
        await using IdentityHost host = await HostWithAccount();

        for (int attempt = 0; attempt < 5; attempt++)
        {
            await host.LoginAsync(Username, "Wrong-Horse-42", Token);
        }

        AuditEntry entry = (await host.ReadAuditEntriesAsync(Token)).Last();

        Snapshot(entry.Before).GetProperty("failedLoginAttempts").GetInt32().Should().Be(4);
        Snapshot(entry.After).GetProperty("failedLoginAttempts").GetInt32().Should().Be(
            0,
            "the counter is cleared when the lockout is applied");

        Snapshot(entry.Before).GetProperty("lockedOutUntil").ValueKind.Should().Be(JsonValueKind.Null);
        Snapshot(entry.After).GetProperty("lockedOutUntil").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task APasswordChange_RecordsWhatChanged_AndNeverTheCredential()
    {
        await using IdentityHost host = await HostWithAccount(mustChangePassword: true);
        await host.LoginAsync(Username, Password, Token);

        await host.Client.PostAsync(
            $"{AuthenticationEndpoints.RoutePrefix}/password",
            new ChangePasswordRequest(Password, NewPassword),
            Token);

        AuditEntry entry = (await host.ReadAuditEntriesAsync(Token))
            .Last(candidate => candidate.Action == "identity.password-change");

        entry.Outcome.Should().Be(AuditOutcome.Succeeded);
        Snapshot(entry.Before).GetProperty("changeRequired").GetBoolean().Should().BeTrue();
        Snapshot(entry.After).GetProperty("changeRequired").GetBoolean().Should().BeFalse();

        entry.Before.Should().NotContain(Password);
        entry.After.Should().NotContain(NewPassword);
    }

    [Fact]
    public async Task ALogout_IsRecorded()
    {
        await using IdentityHost host = await HostWithAccount();
        await host.LoginAsync(Username, Password, Token);

        await host.Client.PostAsync($"{AuthenticationEndpoints.RoutePrefix}/logout", Token);

        (await host.ReadAuditEntriesAsync(Token))
            .Should().Contain(entry => entry.Action == "identity.logout");
    }

    [Fact]
    public async Task ARead_IsNotRecorded()
    {
        await using IdentityHost host = await HostWithAccount();
        await host.LoginAsync(Username, Password, Token);

        await host.Client.GetAsync($"{AuthenticationEndpoints.RoutePrefix}/me", Token);

        // At the scale in SPEC.md §1 a row per query would bury the rows that describe a change.
        (await host.ReadAuditEntriesAsync(Token))
            .Should().ContainSingle().Which.Action.Should().Be("identity.login");
    }

    [Fact]
    public async Task ARouteThatOptedOut_IsNotRecorded()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);

        await host.Client.PostAsync($"{ProbeEndpoints.RoutePrefix}/anonymous", Token);

        (await host.ReadAuditEntriesAsync(Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task AnAnonymousCallToAnAuditedRoute_IsRecordedWithNoActor()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);

        await host.Client.PostAsync($"{ProbeEndpoints.RoutePrefix}/open", Token);

        AuditEntry entry = (await host.ReadAuditEntriesAsync(Token)).Should().ContainSingle().Subject;

        entry.Action.Should().Be("probe.open");
        entry.Outcome.Should().Be(AuditOutcome.Succeeded);
        entry.ActorUserId.Should().BeNull();
    }

    [Fact]
    public async Task ACallThatThrew_IsStillRecorded()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);

        (await host.Client.PostAsync($"{ProbeEndpoints.RoutePrefix}/broken", Token))
            .Status.Should().Be(500);

        AuditEntry entry = (await host.ReadAuditEntriesAsync(Token)).Should().ContainSingle().Subject;

        entry.Action.Should().Be("probe.broken");
        entry.Outcome.Should().Be(AuditOutcome.Failed);
        entry.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task AnUnroutedRequest_IsNotRecorded()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token);

        await host.Client.PostAsync("/api/v1/nothing-serves-this", Token);

        // A 404 for a path nothing serves is a scanner, not an act.
        (await host.ReadAuditEntriesAsync(Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task NoAuditRow_EverContainsAPassword()
    {
        await using IdentityHost host = await HostWithAccount(mustChangePassword: true);

        await host.LoginAsync(Username, "Wrong-Horse-42", Token);
        await host.LoginAsync(Username, Password, Token);
        await host.Client.PostAsync(
            $"{AuthenticationEndpoints.RoutePrefix}/password",
            new ChangePasswordRequest(Password, NewPassword),
            Token);

        IReadOnlyList<AuditEntry> entries = await host.ReadAuditEntriesAsync(Token);

        entries.Should().NotBeEmpty();

        foreach (AuditEntry entry in entries)
        {
            string row = string.Join('|', entry.Action, entry.Before, entry.After, entry.TargetId, entry.Path);

            row.Should().NotContain(Password).And.NotContain(NewPassword).And.NotContain("Wrong-Horse-42");
        }
    }

    [Fact]
    public async Task ASnapshotMemberNamedAfterACredential_IsStoredRedacted()
    {
        await using IdentityHost host = await HostWithAccount();
        await host.LoginAsync(Username, Password, Token);

        AuditEntry entry = (await host.ReadAuditEntriesAsync(Token)).Should().ContainSingle().Subject;

        // The successful sign-in snapshot deliberately says "hashWasUpgraded" rather than
        // anything with "password" in it, precisely because the redactor blanks by name.
        entry.After.Should().Contain("hashWasUpgraded").And.NotContain(SecretRedactor.Placeholder);
    }

    /// <summary>
    /// A stored snapshot, parsed. Read back rather than string-matched, because <c>jsonb</c>
    /// stores a normalised document and hands it back formatted its own way.
    /// </summary>
    private static JsonElement Snapshot(string? payload)
    {
        payload.Should().NotBeNull();

        return JsonDocument.Parse(payload!).RootElement;
    }

    private async Task<IdentityHost> HostWithAccount(bool mustChangePassword = false)
    {
        IdentityHost host = await IdentityHost.StartAsync(postgres, Token);

        await host.CreateUserAsync(
            Username,
            Password,
            Token,
            UserRole.Administrator,
            mustChangePassword);

        return host;
    }
}
