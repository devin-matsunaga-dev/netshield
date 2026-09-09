using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Topology.Handlers;

using NetShield.Platform.Authorization;
using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// The topology graph, under <c>/api/v1/topology/graph</c>.
/// </summary>
/// <remarks>
/// <para>
/// The estate as nodes and edges, filterable by site, by VLAN and by depth from a root, with the
/// coordinates a canvas draws it at. It is behind <see cref="Permission.TopologyRead"/>, whose
/// own definition reads "read the topology graph and the VLAN inventory" — so nothing new is
/// granted and the RBAC table is untouched, the answer WP-2.1 and WP-2.2 both reached.
/// </para>
/// <para>
/// <strong>A resource of its own rather than a device sub-resource.</strong> A graph spans the
/// estate; <c>GET /api/v1/devices/{id}/adjacencies</c> answers the narrower question of what one
/// switch is connected to and why, and keeps the per-protocol evidence. Centring this endpoint on
/// a device is a filter on the estate-wide graph, not the same question.
/// </para>
/// <para>
/// There is no write route and there is not meant to be. Every node and every edge is
/// reconstructible by reading the estate again, and a hand-drawn link would be a claim about
/// cabling with no evidence behind it. The layout is computed rather than stored for the same
/// reason: a saved arrangement would be a second source of truth about a picture the estate
/// already determines.
/// </para>
/// </remarks>
public static class TopologyGraphEndpoints
{
    /// <summary>Where this resource lives.</summary>
    internal const string RoutePrefix = "/api/v1/topology";

    /// <summary>
    /// Maps the graph route. Called by
    /// <see cref="InventoryEndpoints.MapInventoryEndpoints"/>, the module's single registration
    /// point (CONVENTIONS.md §2).
    /// </summary>
    public static IEndpointRouteBuilder MapTopologyGraphEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints
            .MapGroup(RoutePrefix)
            .WithTags("Topology");

        group.MapGet("/graph", GraphAsync)
            .RequirePermission(Permission.TopologyRead)
            .WithName("GetTopologyGraph")
            .Produces<TopologyGraph>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GraphAsync(
        GetTopologyGraphHandler handler,
        CancellationToken cancellationToken,
        string? cursor = null,
        int? limit = null,
        string? site = null,
        int? vlanId = null,
        Guid? rootDeviceId = null,
        int? depth = null)
    {
        Result<PageRequest> page = PageRequest.Create(cursor, limit);

        if (!page.IsSuccess)
        {
            return Result<TopologyGraph>.Failure(page.Error).ToHttpResult();
        }

        Result<TopologyGraphQuery> query = TopologyGraphQuery.Create(
            page.Value,
            site,
            vlanId,
            rootDeviceId,
            depth);

        if (!query.IsSuccess)
        {
            return Result<TopologyGraph>.Failure(query.Error).ToHttpResult();
        }

        return (await handler.HandleAsync(query.Value, cancellationToken)).ToHttpResult();
    }
}
