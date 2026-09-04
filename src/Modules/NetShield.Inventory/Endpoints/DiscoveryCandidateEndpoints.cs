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
/// The discovery candidate endpoints, under <c>/api/v1/discovery/candidates</c>
/// (CONVENTIONS.md §4).
/// </summary>
/// <remarks>
/// The review step. Everything a sweep found arrives here and stays here until somebody promotes
/// it or dismisses it — nothing in the result path creates a device, which is the WP-1.6
/// criterion these two routes exist to satisfy.
/// </remarks>
public static class DiscoveryCandidateEndpoints
{
    /// <summary>The group every route below hangs from.</summary>
    public const string RoutePrefix = "/api/v1/discovery/candidates";

    /// <summary>What an audit row from these routes says it acted on.</summary>
    private const string TargetType = "discovery-candidate";

    /// <summary>
    /// Maps the candidate endpoints. Called by
    /// <see cref="InventoryEndpoints.MapInventoryEndpoints"/>, the module's single registration
    /// point (CONVENTIONS.md §2).
    /// </summary>
    public static IEndpointRouteBuilder MapDiscoveryCandidateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup(RoutePrefix).WithTags("Discovery");

        group.MapGet("/", ListAsync)
            .RequirePermission(Permission.InventoryRead)
            .WithName("ListDiscoveryCandidates")
            .Produces<CursorPage<DiscoveryCandidateSummary>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/{id:guid}/promote", PromoteAsync)
            .AddEndpointFilter<ValidationFilter<PromoteDiscoveryCandidateRequest>>()
            .RequirePermission(Permission.InventoryWrite)
            .Audits("inventory.discovery-candidate-promote", TargetType)
            .WithName("PromoteDiscoveryCandidate")
            .Produces<DeviceDetail>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/ignore", IgnoreAsync)
            .RequirePermission(Permission.InventoryWrite)
            .Audits("inventory.discovery-candidate-ignore", TargetType)
            .WithName("IgnoreDiscoveryCandidate")
            .Produces<DiscoveryIgnoreEntry>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        GetDiscoveryCandidateListHandler handler,
        CancellationToken cancellationToken,
        string? cursor = null,
        int? limit = null,
        DiscoveryCandidateStatus? status = null)
    {
        Result<PageRequest> page = PageRequest.Create(cursor, limit);

        if (!page.IsSuccess)
        {
            return Result<CursorPage<DiscoveryCandidateSummary>>.Failure(page.Error).ToHttpResult();
        }

        DiscoveryCandidateListQuery query = new(page.Value, status);

        return (await handler.HandleAsync(query, cancellationToken)).ToHttpResult();
    }

    private static async Task<IResult> PromoteAsync(
        Guid id,
        PromoteDiscoveryCandidateRequest request,
        PromoteDiscoveryCandidateHandler handler,
        CancellationToken cancellationToken)
    {
        Result<DeviceDetail> result = await handler.HandleAsync(id, request, cancellationToken);

        // 201 and a Location on the device, not on the candidate: what this call created is a
        // device, and that is where the caller should look next.
        return result.IsSuccess
            ? TypedResults.Created($"{DeviceEndpoints.RoutePrefix}/{result.Value.Id}", result.Value)
            : result.ToHttpResult();
    }

    private static async Task<IResult> IgnoreAsync(
        Guid id,
        IgnoreDiscoveryCandidateHandler handler,
        CancellationToken cancellationToken)
    {
        Result<DiscoveryIgnoreEntry> result = await handler.HandleAsync(id, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created(
                $"{DiscoveryIgnoreEndpoints.RoutePrefix}/{result.Value.Id}",
                result.Value)
            : result.ToHttpResult();
    }
}
