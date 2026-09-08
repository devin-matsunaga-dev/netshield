using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Reachability.Handlers;

using NetShield.Platform.Authorization;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// The evidence behind a device's state, under <c>/api/v1/devices/{id}/reachability</c>.
/// </summary>
/// <remarks>
/// <c>DeviceDetail.State</c> is the conclusion and travels on the device. This is the last round
/// trip, the last loss, when the last probe landed, when the state last moved, and why the last
/// probe could not be performed — which is the member that matters, because a device nothing has
/// been able to probe since Tuesday otherwise presents as confidently Online with a stale
/// timestamp.
/// </remarks>
public static class DeviceReachabilityEndpoints
{
    /// <summary>
    /// Maps the reachability route. Called by
    /// <see cref="InventoryEndpoints.MapInventoryEndpoints"/>, the module's single registration
    /// point (CONVENTIONS.md §2).
    /// </summary>
    public static IEndpointRouteBuilder MapDeviceReachabilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints
            .MapGroup(DeviceEndpoints.RoutePrefix)
            .WithTags("Devices");

        group.MapGet("/{id:guid}/reachability", GetAsync)
            .RequirePermission(Permission.InventoryRead)
            .WithName("GetDeviceReachability")
            .Produces<DeviceReachabilityDetail>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        GetDeviceReachabilityHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();
}
