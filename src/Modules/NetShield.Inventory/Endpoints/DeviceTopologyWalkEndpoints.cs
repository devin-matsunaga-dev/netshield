using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Topology.Handlers;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// The on-demand topology reads, under <c>/api/v1/devices/{id}/neighbor-walk</c>,
/// <c>/route-walk</c> and <c>/vlan-walk</c>.
/// </summary>
/// <remarks>
/// <para>
/// Beside WP-1.5's <c>/walk</c> and WP-1.8's <c>/client-walk</c> and shaped the same way: gated
/// on <see cref="Permission.DiscoveryRun"/> because each makes NetShield open a credential and
/// read a device outside its schedule, and answering <c>202</c> because the API schedules and the
/// collector performs (ARCHITECTURE.md §7).
/// </para>
/// <para>
/// <strong>Three routes rather than one with a parameter</strong>, because they are three jobs
/// with three schedules and three failure modes. A neighbour table is small and predictable, a
/// routing table is neither, and a Q-BRIDGE table is the one an old agent is likeliest to hang
/// on; an operator who wants their edges refreshed should not have to wait on — or be failed by —
/// either of the other two.
/// </para>
/// </remarks>
public static class DeviceTopologyWalkEndpoints
{
    /// <summary>What an audit row from these routes says it acted on.</summary>
    private const string TargetType = "device";

    /// <summary>
    /// Maps the topology-walk routes. Called by
    /// <see cref="InventoryEndpoints.MapInventoryEndpoints"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapDeviceTopologyWalkEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints
            .MapGroup(DeviceEndpoints.RoutePrefix)
            .WithTags("Devices");

        group.MapPost("/{id:guid}/neighbor-walk", NeighborWalkAsync)
            .RequirePermission(Permission.DiscoveryRun)
            .Audits("inventory.device-neighbor-walk", TargetType)
            .WithName("QueueDeviceNeighborWalk")
            .Produces<NeighborWalkQueued>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/route-walk", RouteWalkAsync)
            .RequirePermission(Permission.DiscoveryRun)
            .Audits("inventory.device-route-walk", TargetType)
            .WithName("QueueDeviceRouteWalk")
            .Produces<NeighborWalkQueued>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/vlan-walk", VlanWalkAsync)
            .RequirePermission(Permission.DiscoveryRun)
            .Audits("inventory.device-vlan-walk", TargetType)
            .WithName("QueueDeviceVlanWalk")
            .Produces<NeighborWalkQueued>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static Task<IResult> NeighborWalkAsync(
        Guid id,
        QueueTopologyWalkHandler handler,
        CancellationToken cancellationToken) =>
        WalkAsync(id, TopologyWalkKind.Neighbors, handler, cancellationToken);

    private static Task<IResult> RouteWalkAsync(
        Guid id,
        QueueTopologyWalkHandler handler,
        CancellationToken cancellationToken) =>
        WalkAsync(id, TopologyWalkKind.Routes, handler, cancellationToken);

    private static Task<IResult> VlanWalkAsync(
        Guid id,
        QueueTopologyWalkHandler handler,
        CancellationToken cancellationToken) =>
        WalkAsync(id, TopologyWalkKind.Vlans, handler, cancellationToken);

    private static async Task<IResult> WalkAsync(
        Guid id,
        TopologyWalkKind walk,
        QueueTopologyWalkHandler handler,
        CancellationToken cancellationToken)
    {
        Result<NeighborWalkQueued> result = await handler.HandleAsync(id, walk, cancellationToken);

        // 202, because nothing has been read yet. ToHttpResult answers 200, which is right
        // everywhere else and wrong here.
        return result.IsSuccess
            ? TypedResults.Accepted((string?)null, result.Value)
            : result.ToHttpResult();
    }
}
