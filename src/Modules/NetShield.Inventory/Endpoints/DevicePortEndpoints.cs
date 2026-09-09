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
/// What is connected to each of a device's ports, under <c>/api/v1/devices/{id}/ports</c>.
/// </summary>
/// <remarks>
/// <para>
/// A device sub-resource in the shape WP-1.7 gave the fingerprint, the interfaces and the
/// reachability, and WP-2.1 gave the adjacencies: what a machine observed lives beside the device
/// rather than on it.
/// </para>
/// <para>
/// <strong>This is not the interface list.</strong> <c>/interfaces</c> answers what a fingerprint
/// walk read — speed, MTU, type, physical address — for every interface the device has, loopbacks
/// and switch virtual interfaces included. This answers a different question about an overlapping
/// set: what is on the other end. The two share an <c>ifIndex</c> and nothing else, and merging
/// them would have widened a WP-1.7 contract to carry occupancy that means nothing on three
/// quarters of its rows.
/// </para>
/// <para>
/// Behind <see cref="Permission.TopologyRead"/>, whose own definition reads "read the topology
/// graph and the VLAN inventory" — so nothing new is granted and the RBAC table is untouched. It
/// is the honest permission for the join even though every role holding <c>InventoryRead</c> also
/// holds this one: the discriminating content is the adjacency and the neighbour, and both are
/// topology.
/// </para>
/// <para>
/// There is no write route and there is not meant to be. What is plugged into a port is observed,
/// not declared, and a hand-entered occupant would be a claim about cabling with no evidence
/// behind it.
/// </para>
/// </remarks>
public static class DevicePortEndpoints
{
    /// <summary>
    /// Maps the port route. Called by
    /// <see cref="InventoryEndpoints.MapInventoryEndpoints"/>, the module's single registration
    /// point (CONVENTIONS.md §2).
    /// </summary>
    public static IEndpointRouteBuilder MapDevicePortEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints
            .MapGroup(DeviceEndpoints.RoutePrefix)
            .WithTags("Devices");

        group.MapGet("/{id:guid}/ports", ListAsync)
            .RequirePermission(Permission.TopologyRead)
            .WithName("ListDevicePorts")
            .Produces<CursorPage<DevicePortSummary>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        Guid id,
        string? cursor,
        int? limit,
        GetDevicePortListHandler handler,
        CancellationToken cancellationToken)
    {
        Result<PageRequest> page = PageRequest.Create(cursor, limit);

        if (!page.IsSuccess)
        {
            return page.ToHttpResult();
        }

        Result<CursorPage<DevicePortSummary>> result =
            await handler.HandleAsync(id, page.Value, cancellationToken);

        return result.ToHttpResult();
    }
}
