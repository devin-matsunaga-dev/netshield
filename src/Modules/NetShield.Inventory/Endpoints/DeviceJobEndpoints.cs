using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NetShield.Contracts.Collector;
using NetShield.Contracts.Identity;
using NetShield.Contracts.Paging;

using NetShield.Inventory.Collector.Handlers;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// A device's collector queue, under <c>/api/v1/devices/{id}/jobs</c>.
/// </summary>
/// <remarks>
/// <para>
/// What NetShield has asked the collector to do for this device, what it did, and what is still
/// waiting. Until WP-2.5 none of this was readable outside <c>/internal/collector/*</c>, so "I
/// pressed walk — did anything happen?" was a question only the database could answer.
/// </para>
/// <para>
/// <strong>Two different permissions, deliberately.</strong> Reading the queue is
/// <see cref="Permission.InventoryRead"/>, because it is reading about the device. Cancelling is
/// <see cref="Permission.DiscoveryRun"/>, the same privilege as starting a walk, because both
/// decide whether NetShield reads a device outside its schedule. Neither changes the RBAC table.
/// </para>
/// <para>
/// <strong>Cancel is a <c>POST</c> to a sub-resource, not a <c>DELETE</c>.</strong> Nothing is
/// removed: the row moves to <c>Cancelled</c> and stays, because what was queued and then
/// withdrawn is part of the record of what happened. A <c>DELETE</c> answering <c>204</c> would
/// be describing an operation this does not perform.
/// </para>
/// </remarks>
public static class DeviceJobEndpoints
{
    /// <summary>What an audit row from the cancel route says it acted on.</summary>
    private const string TargetType = "device";

    /// <summary>
    /// Maps the queue routes. Called by
    /// <see cref="InventoryEndpoints.MapInventoryEndpoints"/>, the module's single registration
    /// point (CONVENTIONS.md §2).
    /// </summary>
    public static IEndpointRouteBuilder MapDeviceJobEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints
            .MapGroup(DeviceEndpoints.RoutePrefix)
            .WithTags("Devices");

        group.MapGet("/{id:guid}/jobs", ListAsync)
            .RequirePermission(Permission.InventoryRead)
            .WithName("ListDeviceJobs")
            .Produces<CursorPage<CollectorJobSummary>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/jobs/{jobId:guid}/cancel", CancelAsync)
            .RequirePermission(Permission.DiscoveryRun)
            .Audits("inventory.device-job-cancel", TargetType)
            .WithName("CancelDeviceJob")
            .Produces<CollectorJobSummary>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        Guid id,
        GetDeviceJobListHandler handler,
        CancellationToken cancellationToken,
        CollectorJobStatus? status = null,
        string? cursor = null,
        int? limit = null)
    {
        Result<PageRequest> page = PageRequest.Create(cursor, limit);

        return !page.IsSuccess
            ? Result<CursorPage<CollectorJobSummary>>.Failure(page.Error).ToHttpResult()
            : (await handler.HandleAsync(id, status, page.Value, cancellationToken)).ToHttpResult();
    }

    private static async Task<IResult> CancelAsync(
        Guid id,
        Guid jobId,
        CancelDeviceJobHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, jobId, cancellationToken)).ToHttpResult();
}
