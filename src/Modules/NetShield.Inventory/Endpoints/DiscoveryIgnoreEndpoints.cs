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
/// The permanent ignore list, under <c>/api/v1/discovery/ignores</c> (CONVENTIONS.md §4).
/// </summary>
/// <remarks>
/// An entry is what makes "an ignored host never reappears" true: a sweep that sees the address
/// answer records the observation against the run and creates no candidate. Removing an entry is
/// the way back, and it is deliberately an act somebody takes rather than something a re-run can
/// undo.
/// </remarks>
public static class DiscoveryIgnoreEndpoints
{
    /// <summary>The group every route below hangs from.</summary>
    public const string RoutePrefix = "/api/v1/discovery/ignores";

    /// <summary>What an audit row from these routes says it acted on.</summary>
    private const string TargetType = "discovery-ignore";

    /// <summary>
    /// Maps the ignore endpoints. Called by
    /// <see cref="InventoryEndpoints.MapInventoryEndpoints"/>, the module's single registration
    /// point (CONVENTIONS.md §2).
    /// </summary>
    public static IEndpointRouteBuilder MapDiscoveryIgnoreEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup(RoutePrefix).WithTags("Discovery");

        group.MapGet("/", ListAsync)
            .RequirePermission(Permission.InventoryRead)
            .WithName("ListDiscoveryIgnores")
            .Produces<CursorPage<DiscoveryIgnoreEntry>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/", CreateAsync)
            .AddEndpointFilter<ValidationFilter<CreateDiscoveryIgnoreRequest>>()
            .RequirePermission(Permission.InventoryWrite)
            .Audits("inventory.discovery-ignore-create", TargetType)
            .WithName("CreateDiscoveryIgnore")
            .Produces<DiscoveryIgnoreEntry>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequirePermission(Permission.InventoryWrite)
            .Audits("inventory.discovery-ignore-delete", TargetType)
            .WithName("DeleteDiscoveryIgnore")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        GetDiscoveryIgnoreListHandler handler,
        CancellationToken cancellationToken,
        string? cursor = null,
        int? limit = null)
    {
        Result<PageRequest> page = PageRequest.Create(cursor, limit);

        if (!page.IsSuccess)
        {
            return Result<CursorPage<DiscoveryIgnoreEntry>>.Failure(page.Error).ToHttpResult();
        }

        return (await handler.HandleAsync(page.Value, cancellationToken)).ToHttpResult();
    }

    private static async Task<IResult> CreateAsync(
        CreateDiscoveryIgnoreRequest request,
        CreateDiscoveryIgnoreHandler handler,
        CancellationToken cancellationToken)
    {
        Result<DiscoveryIgnoreEntry> result = await handler.HandleAsync(request, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"{RoutePrefix}/{result.Value.Id}", result.Value)
            : result.ToHttpResult();
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        DeleteDiscoveryIgnoreHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();
}
