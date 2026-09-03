using FluentAssertions;

using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

using NetShield.Platform.Auditing;

namespace NetShield.IntegrationTests.Hosting;

/// <summary>
/// The two fallbacks that let one process serve both the SPA and the API (ARCHITECTURE.md §2).
/// </summary>
/// <remarks>
/// The claim worth testing is not that a file comes back — a test host has no SPA build — but
/// that the API still denies by default while the shell does not. WP-0.5 made an endpoint with
/// no policy answer <c>401</c>, and the sign-in page is the one thing that cannot.
/// </remarks>
public sealed class SpaHostingTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const int NotFound = 404;
    private const int Unauthorized = 401;
    private const int Forbidden = 403;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AClientRoute_IsReachableWithoutASession()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token, mapSpa: true);

        ApiResponse response = await host.Client.GetAsync("/devices", Token);

        // 404 here only because no SPA build exists in a test host. The point is what it is not.
        response.Status.Should().NotBe(Unauthorized,
            "the shell renders the sign-in page, so it cannot itself require a session");
        response.Status.Should().NotBe(Forbidden);
    }

    [Fact]
    public async Task AnApiPathNothingServes_AnswersProblemDetails_RatherThanTheShell()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token, mapSpa: true);

        ApiResponse response = await host.Client.GetAsync("/api/v1/no-such-thing", Token);

        response.Status.Should().Be(NotFound);
        response.Member("status").Should().Be("404",
            "an API client reads status codes and problem documents, not HTML (CONVENTIONS.md §4)");
        response.Member("traceId").Should().NotBeNullOrEmpty("every problem response carries one");
    }

    [Fact]
    public async Task AStateChangingCallToAnApiPathNothingServes_WritesNoAuditRow()
    {
        await using IdentityHost host = await IdentityHost.StartAsync(postgres, Token, mapSpa: true);

        ApiResponse response = await host.Client.PostAsync("/api/v1/no-such-thing", Token);

        response.Status.Should().Be(NotFound);

        IReadOnlyList<AuditEntry> entries = await host.ReadAuditEntriesAsync(Token);

        entries.Should().BeEmpty(
            "WP-0.5 chose not to audit a request that matched no endpoint, and giving the API "
            + "path a fallback of its own must not quietly change that");
    }
}
