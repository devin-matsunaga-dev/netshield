using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Paging;

using NetShield.Inventory.Topology.Handlers;

using NetShield.Platform.Authorization;
using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// A device's edges and the state of its topology collection, under
/// <c>/api/v1/devices/{id}/adjacencies</c> and <c>/topology-scan</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two device sub-resources, in the shape WP-1.7 gave the fingerprint, interfaces and
/// reachability: what a machine observed lives beside the device rather than on it. Both are
/// behind <see cref="Permission.TopologyRead"/>, whose own definition reads "read the topology
/// graph and the VLAN inventory" — so nothing new is being granted and the RBAC table is
/// untouched.
/// </para>
/// <para>
/// <strong>This is not the graph endpoint.</strong> WP-2.3 owns that: nodes and edges filtered by
/// site, VLAN and depth from a root, with a layout hint and subgraph paging. These two routes
/// answer a narrower question — "what is <em>this</em> device connected to, and has anything
/// asked it?" — which is what the device screen needs and what proves the collection works.
/// </para>
/// <para>
/// There is no write route and there is not meant to be. An edge is something NetShield observed;
/// every column is reconstructible by reading the estate again, and a hand-drawn link would be a
/// claim about cabling with no evidence behind it.
/// </para>
/// </remarks>
public static class DeviceAdjacencyEndpoints
{
    /// <summary>
    /// Maps the adjacency routes. Called by
    /// <see cref="InventoryEndpoints.MapInventoryEndpoints"/>, the module's single registration
    /// point (CONVENTIONS.md §2).
    /// </summary>
    public static IEndpointRouteBuilder MapDeviceAdjacencyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints
            .MapGroup(DeviceEndpoints.RoutePrefix)
            .WithTags("Devices");

        group.MapGet("/{id:guid}/adjacencies", ListAsync)
            .RequirePermission(Permission.TopologyRead)
            .WithName("ListDeviceAdjacencies")
            .Produces<CursorPage<DeviceAdjacencySummary>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/topology-scan", ScanAsync)
            .RequirePermission(Permission.TopologyRead)
            .WithName("GetDeviceTopologyScan")
            .Produces<DeviceTopologyScanDetail>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        Guid id,
        string? cursor,
        int? limit,
        GetDeviceAdjacencyListHandler handler,
        CancellationToken cancellationToken)
    {
        Result<PageRequest> page = PageRequest.Create(cursor, limit);

        if (!page.IsSuccess)
        {
            return page.ToHttpResult();
        }

        Result<CursorPage<DeviceAdjacencySummary>> result =
            await handler.HandleAsync(id, page.Value, cancellationToken);

        return result.ToHttpResult();
    }

    private static async Task<IResult> ScanAsync(
        Guid id,
        GetDeviceTopologyScanHandler handler,
        CancellationToken cancellationToken)
    {
        Result<DeviceTopologyScanDetail> result = await handler.HandleAsync(id, cancellationToken);

        return result.ToHttpResult();
    }
}
