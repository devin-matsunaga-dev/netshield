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
/// The VLANs one device is configured with, under <c>/api/v1/devices/{id}/vlans</c>.
/// </summary>
/// <remarks>
/// A device sub-resource in the shape WP-1.7 gave the fingerprint, the interfaces and the
/// reachability and WP-2.1 gave the adjacencies: what a machine observed lives beside the device
/// rather than on it. Behind <see cref="Permission.TopologyRead"/>, which already covers the VLAN
/// inventory by its own definition.
///
/// It is the unmerged half of <see cref="VlanEndpoints"/>: one row per VLAN <em>this switch</em>
/// carries, with the ports it carries it on and whether each is tagged.
/// </remarks>
public static class DeviceVlanEndpoints
{
    /// <summary>
    /// Maps the per-device VLAN route. Called by
    /// <see cref="InventoryEndpoints.MapInventoryEndpoints"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapDeviceVlanEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints
            .MapGroup(DeviceEndpoints.RoutePrefix)
            .WithTags("Devices");

        group.MapGet("/{id:guid}/vlans", ListAsync)
            .RequirePermission(Permission.TopologyRead)
            .WithName("ListDeviceVlans")
            .Produces<CursorPage<DeviceVlanSummary>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        Guid id,
        string? cursor,
        int? limit,
        GetDeviceVlanListHandler handler,
        CancellationToken cancellationToken)
    {
        Result<PageRequest> page = PageRequest.Create(cursor, limit);

        if (!page.IsSuccess)
        {
            return page.ToHttpResult();
        }

        Result<CursorPage<DeviceVlanSummary>> result =
            await handler.HandleAsync(id, page.Value, cancellationToken);

        return result.ToHttpResult();
    }
}
