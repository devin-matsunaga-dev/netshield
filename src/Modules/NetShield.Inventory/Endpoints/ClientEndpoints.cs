using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Paging;

using NetShield.Inventory.Clients.Handlers;

using NetShield.Platform.Authorization;
using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// The tracked clients, under <c>/api/v1/clients</c>.
/// </summary>
/// <remarks>
/// Every route here is behind <see cref="Permission.InventoryRead"/>, whose own definition reads
/// "devices, <em>clients</em>, sites and their manual attributes" — so nothing new is being
/// granted and the RBAC table is untouched.
///
/// There is no write route and there is not meant to be. A client is something NetShield
/// observed rather than something an operator maintains: every column is reconstructible by
/// reading the estate's tables again, and a hand-edited binding would be a claim about the past
/// with no evidence behind it.
/// </remarks>
public static class ClientEndpoints
{
    /// <summary>Where the client routes live.</summary>
    public const string RoutePrefix = "/api/v1/clients";

    /// <summary>
    /// Maps the client routes. Called by
    /// <see cref="InventoryEndpoints.MapInventoryEndpoints"/>, the module's single registration
    /// point (CONVENTIONS.md §2).
    /// </summary>
    public static IEndpointRouteBuilder MapClientEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup(RoutePrefix).WithTags("Clients");

        group.MapGet("/", ListAsync)
            .RequirePermission(Permission.InventoryRead)
            .WithName("ListClients")
            .Produces<CursorPage<ClientSummary>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // Before "/{id:guid}" in the file for a reader's sake rather than the router's — the
        // route constraint already keeps "resolve" from being read as an id.
        group.MapGet("/resolve", ResolveAsync)
            .RequirePermission(Permission.InventoryRead)
            .WithName("ResolveAsset")
            .Produces<AssetResolution>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(Permission.InventoryRead)
            .WithName("GetClient")
            .Produces<ClientDetail>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/ip-history", IpHistoryAsync)
            .RequirePermission(Permission.InventoryRead)
            .WithName("ListClientIpHistory")
            .Produces<CursorPage<ClientIpBindingSummary>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/port-history", PortHistoryAsync)
            .RequirePermission(Permission.InventoryRead)
            .WithName("ListClientPortHistory")
            .Produces<CursorPage<ClientPortBindingSummary>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        GetClientListHandler handler,
        CancellationToken cancellationToken,
        string? search = null,
        Guid? deviceId = null,
        int? vlanId = null,
        DateTimeOffset? seenSince = null,
        bool? onlyActive = null,
        string? cursor = null,
        int? limit = null)
    {
        Result<PageRequest> page = PageRequest.Create(cursor, limit);

        if (!page.IsSuccess)
        {
            return Result<CursorPage<ClientSummary>>.Failure(page.Error).ToHttpResult();
        }

        ClientListQuery query = new(search, deviceId, vlanId, seenSince, onlyActive ?? false);

        return (await handler.HandleAsync(query, page.Value, cancellationToken)).ToHttpResult();
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        GetClientHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();

    private static async Task<IResult> IpHistoryAsync(
        Guid id,
        GetClientIpHistoryHandler handler,
        CancellationToken cancellationToken,
        string? cursor = null,
        int? limit = null)
    {
        Result<PageRequest> page = PageRequest.Create(cursor, limit);

        return !page.IsSuccess
            ? Result<CursorPage<ClientIpBindingSummary>>.Failure(page.Error).ToHttpResult()
            : (await handler.HandleAsync(id, page.Value, cancellationToken)).ToHttpResult();
    }

    private static async Task<IResult> PortHistoryAsync(
        Guid id,
        GetClientPortHistoryHandler handler,
        CancellationToken cancellationToken,
        string? cursor = null,
        int? limit = null)
    {
        Result<PageRequest> page = PageRequest.Create(cursor, limit);

        return !page.IsSuccess
            ? Result<CursorPage<ClientPortBindingSummary>>.Failure(page.Error).ToHttpResult()
            : (await handler.HandleAsync(id, page.Value, cancellationToken)).ToHttpResult();
    }

    /// <summary>
    /// <c>ResolveAssetAt</c> over HTTP: what held an address at an instant.
    /// </summary>
    /// <remarks>
    /// <paramref name="at"/> defaults to now, so the bare form answers "who is on this address"
    /// — which is what somebody typing an address into the box actually wants, and the history
    /// is what they reach for next.
    /// </remarks>
    private static async Task<IResult> ResolveAsync(
        ResolveAssetHandler handler,
        CancellationToken cancellationToken,
        string? ipAddress = null,
        DateTimeOffset? at = null) =>
        (await handler.HandleAsync(ipAddress, at, cancellationToken)).ToHttpResult();
}
