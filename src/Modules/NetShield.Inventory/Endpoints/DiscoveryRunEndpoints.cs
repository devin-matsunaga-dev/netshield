using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Paging;

using NetShield.Inventory.Discovery.Handlers;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// The discovery run endpoints, under <c>/api/v1/discovery/runs</c> (CONVENTIONS.md §4).
/// </summary>
/// <remarks>
/// <para>
/// Starting a run is gated on <see cref="Permission.DiscoveryRun"/> — the permission that has
/// said "start a discovery run outside its schedule" since WP-0.5, and the same one the
/// on-demand fingerprint walk carries. Both make NetShield reach into the estate outside its
/// schedule; telling a sweep from a walk would be a distinction neither the RBAC table nor
/// SPEC.md §2 draws.
/// </para>
/// <para>
/// It answers <c>202</c>, because the API schedules and a collector performs
/// (ARCHITECTURE.md §7). The run is what a caller watches.
/// </para>
/// </remarks>
public static class DiscoveryRunEndpoints
{
    /// <summary>The group every route below hangs from.</summary>
    public const string RoutePrefix = "/api/v1/discovery/runs";

    /// <summary>What an audit row from these routes says it acted on.</summary>
    private const string TargetType = "discovery-run";

    /// <summary>
    /// Maps the run endpoints. Called by <see cref="InventoryEndpoints.MapInventoryEndpoints"/>,
    /// the module's single registration point (CONVENTIONS.md §2).
    /// </summary>
    public static IEndpointRouteBuilder MapDiscoveryRunEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup(RoutePrefix).WithTags("Discovery");

        group.MapGet("/", ListAsync)
            .RequirePermission(Permission.InventoryRead)
            .WithName("ListDiscoveryRuns")
            .Produces<CursorPage<DiscoveryRunSummary>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(Permission.InventoryRead)
            .WithName("GetDiscoveryRun")
            .Produces<DiscoveryRunDetail>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/hosts", ListHostsAsync)
            .RequirePermission(Permission.InventoryRead)
            .WithName("ListDiscoveryRunHosts")
            .Produces<CursorPage<DiscoveryRunHostResult>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", StartAsync)
            .RequirePermission(Permission.DiscoveryRun)
            .Audits("inventory.discovery-run-start", TargetType)
            .WithName("StartDiscoveryRun")
            .Produces<DiscoveryRunQueued>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        GetDiscoveryRunListHandler handler,
        CancellationToken cancellationToken,
        string? cursor = null,
        int? limit = null,
        Guid? seedId = null,
        DiscoveryRunStatus? status = null)
    {
        Result<PageRequest> page = PageRequest.Create(cursor, limit);

        if (!page.IsSuccess)
        {
            return Result<CursorPage<DiscoveryRunSummary>>.Failure(page.Error).ToHttpResult();
        }

        DiscoveryRunListQuery query = new(page.Value, seedId, status);

        return (await handler.HandleAsync(query, cancellationToken)).ToHttpResult();
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        GetDiscoveryRunHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();

    private static async Task<IResult> ListHostsAsync(
        Guid id,
        GetDiscoveryRunHostListHandler handler,
        CancellationToken cancellationToken,
        string? cursor = null,
        int? limit = null,
        DiscoveryHostOutcome? outcome = null)
    {
        Result<PageRequest> page = PageRequest.Create(cursor, limit);

        if (!page.IsSuccess)
        {
            return Result<CursorPage<DiscoveryRunHostResult>>.Failure(page.Error).ToHttpResult();
        }

        return (await handler.HandleAsync(id, page.Value, outcome, cancellationToken)).ToHttpResult();
    }

    /// <summary>
    /// Starts a run of one seed. The seed is named in the query string rather than in a body,
    /// because it is the whole of the request and a body of one member is a shape to version.
    /// </summary>
    private static async Task<IResult> StartAsync(
        Guid seedId,
        StartDiscoveryRunHandler handler,
        CancellationToken cancellationToken)
    {
        Result<DiscoveryRunQueued> result = await handler.HandleAsync(seedId, cancellationToken);

        // 202, because nothing has been swept yet — the same shape the on-demand walk uses.
        return result.IsSuccess
            ? TypedResults.Accepted($"{RoutePrefix}/{result.Value.RunId}", result.Value)
            : result.ToHttpResult();
    }
}
