using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NetShield.Contracts.Identity;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Results;

namespace NetShield.IntegrationTests.Authorization;

/// <summary>
/// Stand-in endpoints for the module routes Phase 1 has not built yet.
/// </summary>
/// <remarks>
/// WP-0.5 has to show that an Analyst is refused a write with 403, and the only endpoints in the
/// system at this point are the authentication ones. These routes exist so the policies, the
/// fallback, the resource guard and the audit middleware are exercised over real HTTP rather
/// than asserted against in isolation. They live in the test project and are mapped only by the
/// test host.
/// </remarks>
internal static class ProbeEndpoints
{
    /// <summary>The group every probe route hangs from.</summary>
    public const string RoutePrefix = "/api/v1/probe";

    internal static IEndpointRouteBuilder MapProbeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup(RoutePrefix);

        // The endpoint-level check: the role must hold the permission to be routed at all.
        group.MapPost("/inventory", () => TypedResults.Ok())
            .RequirePermission(Permission.InventoryWrite)
            .Audits("inventory.write", "device");

        group.MapGet("/inventory", () => TypedResults.Ok())
            .RequirePermission(Permission.InventoryRead);

        // The module-level check: authenticated at the endpoint, permission resolved in the
        // handler, which is what a resource-scoped operation does (ARCHITECTURE.md §8).
        group.MapPost("/guarded", (IResourceGuard guard) =>
                guard.Require(Permission.InventoryWrite, "device", "probe-1").ToHttpResult())
            .RequireAuthorization()
            .Audits("inventory.guarded-write", "device");

        // Declares no authorization at all. The fallback policy is what answers it.
        group.MapPost("/unpoliced", () => TypedResults.Ok());

        // Anonymous and unaudited, the shape WP-1.3's collector endpoints will take.
        group.MapPost("/anonymous", () => TypedResults.Ok())
            .AllowAnonymous()
            .SkipAudit();

        // Anonymous but audited, so a row is written for a caller with no session.
        group.MapPost("/open", () => TypedResults.Ok())
            .AllowAnonymous()
            .Audits("probe.open");

        // Throws, so the middleware's "record it anyway, then rethrow" path is covered.
        group.MapPost("/broken", () =>
            {
                throw new InvalidOperationException("Deliberate failure, to prove a 500 is audited.");
            })
            .AllowAnonymous()
            .Audits("probe.broken");

        return endpoints;
    }
}
