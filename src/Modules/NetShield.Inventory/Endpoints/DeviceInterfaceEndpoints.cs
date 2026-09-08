using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Paging;

using NetShield.Inventory.Discovery.Handlers;

using NetShield.Platform.Authorization;
using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// A device's interface inventory, under <c>/api/v1/devices/{id}/interfaces</c>.
/// </summary>
/// <remarks>
/// Paginated, in <c>ifIndex</c> order — the order the device itself presents its ports in. A
/// device that has never been walked answers an empty page rather than a 404: it exists and has
/// no interfaces recorded, which is a different fact from the device not being there, and the
/// fingerprint route is where "nothing has walked this" is said.
/// </remarks>
public static class DeviceInterfaceEndpoints
{
    /// <summary>
    /// Maps the interface route. Called by
    /// <see cref="InventoryEndpoints.MapInventoryEndpoints"/>, the module's single registration
    /// point (CONVENTIONS.md §2).
    /// </summary>
    public static IEndpointRouteBuilder MapDeviceInterfaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints
            .MapGroup(DeviceEndpoints.RoutePrefix)
            .WithTags("Devices");

        group.MapGet("/{id:guid}/interfaces", ListAsync)
            .RequirePermission(Permission.InventoryRead)
            .WithName("ListDeviceInterfaces")
            .Produces<CursorPage<DeviceInterfaceSummary>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        Guid id,
        GetDeviceInterfaceListHandler handler,
        CancellationToken cancellationToken,
        string? cursor = null,
        int? limit = null)
    {
        Result<PageRequest> page = PageRequest.Create(cursor, limit);

        return !page.IsSuccess
            ? Result<CursorPage<DeviceInterfaceSummary>>.Failure(page.Error).ToHttpResult()
            : (await handler.HandleAsync(id, page.Value, cancellationToken)).ToHttpResult();
    }
}
