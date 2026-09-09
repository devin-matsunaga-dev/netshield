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
/// The estate's VLAN inventory, under <c>/api/v1/vlans</c>.
/// </summary>
/// <remarks>
/// <para>
/// A resource of its own rather than a device sub-resource, because that is what a VLAN is: it
/// spans switches, and the whole point of WP-2.2 is that a VLAN on six of them appears once. The
/// per-device view lives at <c>/api/v1/devices/{id}/vlans</c> and answers the other question.
/// </para>
/// <para>
/// Both routes are behind <see cref="Permission.TopologyRead"/>, whose own definition reads "read
/// the topology graph and the VLAN inventory" — so nothing new is being granted and the RBAC
/// table is untouched, the same answer WP-2.1 reached for the adjacency routes.
/// </para>
/// <para>
/// There is no write route and there is not meant to be. A VLAN exists in NetShield because a
/// switch reported it; every column is reconstructible by reading the estate again, and a
/// hand-created VLAN would be a claim about a network with no evidence behind it. Naming one is
/// the switch's business and NetShield reads what the operator typed there.
/// </para>
/// </remarks>
public static class VlanEndpoints
{
    /// <summary>Where this resource lives.</summary>
    internal const string RoutePrefix = "/api/v1/vlans";

    /// <summary>
    /// Maps the VLAN routes. Called by
    /// <see cref="InventoryEndpoints.MapInventoryEndpoints"/>, the module's single registration
    /// point (CONVENTIONS.md §2).
    /// </summary>
    public static IEndpointRouteBuilder MapVlanEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints
            .MapGroup(RoutePrefix)
            .WithTags("Topology");

        group.MapGet("/", ListAsync)
            .RequirePermission(Permission.TopologyRead)
            .WithName("ListVlans")
            .Produces<CursorPage<VlanSummary>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{vlanId:int}", GetAsync)
            .RequirePermission(Permission.TopologyRead)
            .WithName("GetVlan")
            .Produces<VlanDetail>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        string? cursor,
        int? limit,
        GetVlanListHandler handler,
        CancellationToken cancellationToken)
    {
        Result<PageRequest> page = PageRequest.Create(cursor, limit);

        if (!page.IsSuccess)
        {
            return page.ToHttpResult();
        }

        Result<CursorPage<VlanSummary>> result =
            await handler.HandleAsync(page.Value, cancellationToken);

        return result.ToHttpResult();
    }

    private static async Task<IResult> GetAsync(
        int vlanId,
        GetVlanHandler handler,
        CancellationToken cancellationToken)
    {
        Result<VlanDetail> result = await handler.HandleAsync(vlanId, cancellationToken);

        return result.ToHttpResult();
    }
}
