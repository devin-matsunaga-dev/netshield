using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Clients.Handlers;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// The on-demand client-table read, under <c>/api/v1/devices/{id}/client-walk</c>.
/// </summary>
/// <remarks>
/// One route, beside WP-1.5's <c>/walk</c> and shaped the same way: gated on
/// <see cref="Permission.DiscoveryRun"/> because it makes NetShield open a credential and read a
/// device outside its schedule, and answering <c>202</c> because the API schedules and the
/// collector performs (ARCHITECTURE.md §7).
///
/// It is a separate route from <c>/walk</c> rather than a parameter on it, because the two read
/// different MIBs for different purposes and on very different intervals — a fingerprint is what
/// a device <em>is</em> and changes when somebody upgrades it, while the client tables are who is
/// attached and change all day.
/// </remarks>
public static class DeviceClientWalkEndpoints
{
    /// <summary>What an audit row from this route says it acted on.</summary>
    private const string TargetType = "device";

    /// <summary>
    /// Maps the client-walk route. Called by
    /// <see cref="InventoryEndpoints.MapInventoryEndpoints"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapDeviceClientWalkEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints
            .MapGroup(DeviceEndpoints.RoutePrefix)
            .WithTags("Devices");

        group.MapPost("/{id:guid}/client-walk", WalkAsync)
            .RequirePermission(Permission.DiscoveryRun)
            .Audits("inventory.device-client-walk", TargetType)
            .WithName("QueueDeviceClientWalk")
            .Produces<ClientWalkQueued>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> WalkAsync(
        Guid id,
        QueueClientWalkHandler handler,
        CancellationToken cancellationToken)
    {
        Result<ClientWalkQueued> result = await handler.HandleAsync(id, cancellationToken);

        // 202, because nothing has been read yet. ToHttpResult answers 200, which is right
        // everywhere else and wrong here.
        return result.IsSuccess
            ? TypedResults.Accepted((string?)null, result.Value)
            : result.ToHttpResult();
    }
}
