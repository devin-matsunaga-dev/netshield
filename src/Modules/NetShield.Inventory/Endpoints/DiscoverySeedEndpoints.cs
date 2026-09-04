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
using NetShield.Platform.Validation;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// The discovery seed endpoints, under <c>/api/v1/discovery/seeds</c> (CONVENTIONS.md §4).
/// </summary>
/// <remarks>
/// Reading is <see cref="Permission.InventoryRead"/> and writing is
/// <see cref="Permission.PoliciesWrite"/>, whose own definition names discovery schedules. The
/// split is the one SPEC.md §2 already draws: what the estate is, and what the platform does on
/// its own.
/// </remarks>
public static class DiscoverySeedEndpoints
{
    /// <summary>The group every route below hangs from.</summary>
    public const string RoutePrefix = "/api/v1/discovery/seeds";

    /// <summary>What an audit row from these routes says it acted on.</summary>
    private const string TargetType = "discovery-seed";

    /// <summary>
    /// Maps the seed endpoints. Called by <see cref="InventoryEndpoints.MapInventoryEndpoints"/>,
    /// the module's single registration point (CONVENTIONS.md §2).
    /// </summary>
    public static IEndpointRouteBuilder MapDiscoverySeedEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup(RoutePrefix).WithTags("Discovery");

        group.MapGet("/", ListAsync)
            .RequirePermission(Permission.InventoryRead)
            .WithName("ListDiscoverySeeds")
            .Produces<CursorPage<DiscoverySeedSummary>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(Permission.InventoryRead)
            .WithName("GetDiscoverySeed")
            .Produces<DiscoverySeedDetail>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .AddEndpointFilter<ValidationFilter<CreateDiscoverySeedRequest>>()
            .RequirePermission(Permission.PoliciesWrite)
            .Audits("inventory.discovery-seed-create", TargetType)
            .WithName("CreateDiscoverySeed")
            .Produces<DiscoverySeedDetail>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}", UpdateAsync)
            .AddEndpointFilter<ValidationFilter<UpdateDiscoverySeedRequest>>()
            .RequirePermission(Permission.PoliciesWrite)
            .Audits("inventory.discovery-seed-update", TargetType)
            .WithName("UpdateDiscoverySeed")
            .Produces<DiscoverySeedDetail>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequirePermission(Permission.PoliciesWrite)
            .Audits("inventory.discovery-seed-delete", TargetType)
            .WithName("DeleteDiscoverySeed")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        GetDiscoverySeedListHandler handler,
        CancellationToken cancellationToken,
        string? cursor = null,
        int? limit = null,
        bool? enabled = null)
    {
        Result<PageRequest> page = PageRequest.Create(cursor, limit);

        if (!page.IsSuccess)
        {
            return Result<CursorPage<DiscoverySeedSummary>>.Failure(page.Error).ToHttpResult();
        }

        return (await handler.HandleAsync(new DiscoverySeedListQuery(page.Value, enabled), cancellationToken))
            .ToHttpResult();
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        GetDiscoverySeedHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();

    private static async Task<IResult> CreateAsync(
        CreateDiscoverySeedRequest request,
        CreateDiscoverySeedHandler handler,
        CancellationToken cancellationToken)
    {
        Result<DiscoverySeedDetail> result = await handler.HandleAsync(request, cancellationToken);

        // 201 with a Location header (CONVENTIONS.md §4). ToHttpResult answers 200, which is
        // right everywhere else and wrong here.
        return result.IsSuccess
            ? TypedResults.Created($"{RoutePrefix}/{result.Value.Id}", result.Value)
            : result.ToHttpResult();
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateDiscoverySeedRequest request,
        UpdateDiscoverySeedHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToHttpResult();

    private static async Task<IResult> DeleteAsync(
        Guid id,
        DeleteDiscoverySeedHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();
}
