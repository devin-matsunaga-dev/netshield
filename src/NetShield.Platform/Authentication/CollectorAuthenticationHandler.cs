using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

using NetShield.Platform.Results;

namespace NetShield.Platform.Authentication;

/// <summary>
/// Authenticates <c>netshield-collector</c> by the shared secret it presents as a bearer token
/// (ARCHITECTURE.md §7 and §8).
/// </summary>
/// <remarks>
/// <para>
/// A scheme of its own rather than a filter on the routes, so that the collector arrives at
/// authorization as a principal like any other caller and the platform's deny-by-default
/// fallback policy keeps meaning what it says. The principal it mints carries no role: nothing
/// in <c>RolePermissions</c> applies to it, so a collector cannot reach a permission-gated
/// endpoint even if one were to name a policy by mistake, and equally a signed-in administrator
/// cannot reach the internal contract — the two credentials open disjoint sets of routes.
/// </para>
/// <para>
/// The comparison is over SHA-256 digests rather than the secrets themselves. Digests are the
/// same length whatever was presented, so a fixed-time comparison of them leaks neither the
/// secret nor its length, which a fixed-time comparison of raw bytes still would.
/// </para>
/// </remarks>
internal sealed class CollectorAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptionsMonitor<CollectorAuthenticationOptions> collector)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>The authentication scheme named in the <c>WWW-Authenticate</c> challenge.</summary>
    private const string BearerScheme = "Bearer";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderNames.Authorization, out StringValues header))
        {
            // NoResult rather than Fail: the request presented nothing, which is not the same as
            // presenting something wrong, and only the second is worth a log line.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string? presented = ReadBearerToken(header.ToString());

        if (presented is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!Matches(presented, collector.CurrentValue.SharedSecret))
        {
            // The secret itself is never in this line, at any level. What an operator needs is
            // that a wrong one arrived and from where; the value is the thing they must not be
            // able to read back out of the log (SPEC.md §5).
            Logger.LogWarning(
                "A collector request presented an incorrect shared secret from {SourceIp}.",
                Context.Connection.RemoteIpAddress?.ToString());

            return Task.FromResult(AuthenticateResult.Fail("The collector shared secret is not correct."));
        }

        ClaimsIdentity identity = new(
            [new Claim(ClaimTypes.Name, CollectorIdentity.PrincipalName)],
            CollectorIdentity.Scheme);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), CollectorIdentity.Scheme)));
    }

    /// <summary>
    /// Answers a challenge with the problem shape the rest of the API uses, not with a redirect
    /// and not with an empty body (CONVENTIONS.md §4). The <c>WWW-Authenticate</c> header is what
    /// tells the collector which credential was expected.
    /// </summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.Append(HeaderNames.WWWAuthenticate, BearerScheme);

        return Result.Failure(
                Error.Unauthenticated(
                    "collector.not-authenticated",
                    "The collector shared secret is missing or not correct."))
            .ToHttpResult()
            .ExecuteAsync(Context);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        Result.Failure(
                Error.Forbidden(
                    "collector.forbidden",
                    "That credential does not open the collector contract."))
            .ToHttpResult()
            .ExecuteAsync(Context);

    /// <summary>The token out of an <c>Authorization: Bearer &lt;token&gt;</c> header.</summary>
    private static string? ReadBearerToken(string header)
    {
        if (!header.StartsWith(BearerScheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string token = header[BearerScheme.Length..].Trim();

        return token.Length == 0 ? null : token;
    }

    /// <summary>
    /// Whether the presented token is the configured secret, compared in time that does not
    /// depend on how much of it was right.
    /// </summary>
    private static bool Matches(string presented, string configured)
    {
        if (configured.Length == 0)
        {
            // Unreachable while the options validator runs at startup, and false here anyway: an
            // empty configured secret must never be the one an empty presented token matches.
            return false;
        }

        Span<byte> presentedDigest = stackalloc byte[SHA256.HashSizeInBytes];
        Span<byte> configuredDigest = stackalloc byte[SHA256.HashSizeInBytes];

        SHA256.HashData(Encoding.UTF8.GetBytes(presented), presentedDigest);
        SHA256.HashData(Encoding.UTF8.GetBytes(configured), configuredDigest);

        return CryptographicOperations.FixedTimeEquals(presentedDigest, configuredDigest);
    }
}
