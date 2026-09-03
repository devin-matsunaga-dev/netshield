using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NetShield.Platform.Auditing;
using NetShield.Platform.Results;

namespace NetShield.Platform.Api;

/// <summary>
/// Serves the built SPA from this process (ARCHITECTURE.md §2).
/// </summary>
/// <remarks>
/// <para>
/// The SPA build lands in <c>wwwroot</c> — <c>src/NetShield.Web.Client</c> writes there directly,
/// so nothing copies it and nothing can copy a stale one. In development the client runs under
/// the Vite dev server instead and proxies <c>/api</c> here, so these routes serve nothing and
/// cost nothing.
/// </para>
/// <para>
/// The API denies by default (ARCHITECTURE.md §8, WP-0.5): an endpoint that declares no policy
/// answers <c>401</c>. Both routes below therefore say <c>AllowAnonymous()</c> in the open —
/// the sign-in page cannot require a session to be reachable.
/// </para>
/// <para>
/// It sits in <c>NetShield.Platform</c> rather than in the composition root only so that a test
/// can exercise it: ARCHITECTURE.md §4 lets nothing reference <c>NetShield.Web.Host</c>, and
/// "the shell is reachable without a session" is exactly the claim worth a test. The decision to
/// serve the SPA at all stays where it belongs, in the composition root that calls this.
/// </para>
/// </remarks>
public static class SpaHosting
{
    /// <summary>Everything the SPA is served from, and everything the API is not.</summary>
    private const string ApiFallbackPattern = "/api/{**path}";

    /// <summary>The document the router boots from.</summary>
    private const string ShellFile = "index.html";

    /// <summary>
    /// Maps the two fallbacks: an API path nothing serves, and every other path, which is a
    /// client-side route and gets the shell.
    /// </summary>
    public static WebApplication MapNetShieldSpa(this WebApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        // Without this, an /api path nothing serves would fall through to the shell and answer
        // 200 with an HTML body — the shape a client is least able to make sense of. It is
        // anonymous and unaudited so that it behaves exactly like the unrouted 404 it replaces
        // (WP-0.5 chose not to audit a request that matched no endpoint); the only thing it adds
        // is the problem-details body CONVENTIONS.md §4 asks every error to carry.
        application.MapFallback(
                ApiFallbackPattern,
                () => Result
                    .Failure(Error.NotFound("api.not-found", "There is no such endpoint."))
                    .ToHttpResult())
            .AllowAnonymous()
            .WithMetadata(new NoAuditAttribute());

        application.MapFallbackToFile(ShellFile).AllowAnonymous();

        return application;
    }
}
