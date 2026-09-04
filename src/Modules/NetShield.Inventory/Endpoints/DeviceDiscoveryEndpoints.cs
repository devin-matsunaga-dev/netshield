using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Discovery.Handlers;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// The on-demand fingerprint route, under <c>/api/v1/devices/{id}/walk</c>.
/// </summary>
/// <remarks>
/// <para>
/// One route, and deliberately one. It asks NetShield to find out what a device already in the
/// inventory <em>is</em> — a fingerprint refresh. The discovery runs SPEC.md §2 describes, over
/// CIDR seeds and producing reviewable candidates, are WP-1.6's and are not this.
/// </para>
/// <para>
/// It is gated on <see cref="Permission.DiscoveryRun"/> — "start a discovery run outside its
/// schedule", which is exactly what this is — rather than on <c>InventoryWrite</c>. Reading a
/// device with a credential is a different privilege from editing its notes, and the permission
/// for it already existed.
/// </para>
/// <para>
/// It answers <c>202</c>, because the API schedules and the collector performs
/// (ARCHITECTURE.md §7). There is no job-status route to poll: a caller watches the device.
/// </para>
/// </remarks>
public static class DeviceDiscoveryEndpoints
{
    /// <summary>What an audit row from this route says it acted on.</summary>
    private const string TargetType = "device";

    /// <summary>
    /// Maps the discovery endpoints. Called by
    /// <see cref="InventoryEndpoints.MapInventoryEndpoints"/>, the module's single registration
    /// point (CONVENTIONS.md §2).
    /// </summary>
    public static IEndpointRouteBuilder MapDeviceDiscoveryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints
            .MapGroup(DeviceEndpoints.RoutePrefix)
            .WithTags("Devices");

        group.MapPost("/{id:guid}/walk", WalkAsync)
            .RequirePermission(Permission.DiscoveryRun)
            .Audits("inventory.device-walk", TargetType)
            .WithName("QueueDeviceWalk")
            .Produces<DeviceWalkQueued>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> WalkAsync(
        Guid id,
        QueueDeviceWalkHandler handler,
        CancellationToken cancellationToken)
    {
        Result<DeviceWalkQueued> result = await handler.HandleAsync(id, cancellationToken);

        // 202, because nothing has been walked yet. ToHttpResult answers 200, which is right
        // everywhere else and wrong here, so the accepted case is written out — the same shape
        // DeviceEndpoints uses for its 201.
        return result.IsSuccess
            ? TypedResults.Accepted((string?)null, result.Value)
            : result.ToHttpResult();
    }
}
