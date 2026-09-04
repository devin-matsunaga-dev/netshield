using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Paging;

using NetShield.Inventory.Devices;
using NetShield.Inventory.Devices.Handlers;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Paging;
using NetShield.Platform.Results;
using NetShield.Platform.Validation;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// The device endpoints, under <c>/api/v1/devices</c> (CONVENTIONS.md §4).
/// </summary>
/// <remarks>
/// <para>
/// The endpoint layer owns the HTTP facts and nothing else: it parses the query string, turns a
/// handler's <see cref="Result{T}"/> into a status code, and names the audit action. Every route
/// declares the permission it needs — that is the endpoint half of ARCHITECTURE.md §8, and each
/// handler makes the module-level check again through <c>IResourceGuard</c>, because a handler
/// reached from a second route or a background job has no endpoint to have checked for it.
/// </para>
/// <para>
/// The response metadata is what the OpenAPI document — and so the generated TypeScript client —
/// is built from. It describes; it does not decide.
/// </para>
/// </remarks>
public static class DeviceEndpoints
{
    /// <summary>The group every route below hangs from.</summary>
    public const string RoutePrefix = "/api/v1/devices";

    /// <summary>What an audit row from these routes says it acted on.</summary>
    private const string TargetType = "device";

    /// <summary>Maps the inventory endpoints. Called once, by the composition root.</summary>
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup(RoutePrefix).WithTags("Devices");

        group.MapGet("/", ListAsync)
            .RequirePermission(Permission.InventoryRead)
            .WithName("ListDevices")
            .Produces<CursorPage<DeviceSummary>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(Permission.InventoryRead)
            .WithName("GetDevice")
            .Produces<DeviceDetail>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .AddEndpointFilter<ValidationFilter<CreateDeviceRequest>>()
            .RequirePermission(Permission.InventoryWrite)
            .Audits("inventory.device-create", TargetType)
            .WithName("CreateDevice")
            .Produces<DeviceDetail>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}", UpdateAsync)
            .AddEndpointFilter<ValidationFilter<UpdateDeviceRequest>>()
            .RequirePermission(Permission.InventoryWrite)
            .Audits("inventory.device-update", TargetType)
            .WithName("UpdateDevice")
            .Produces<DeviceDetail>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequirePermission(Permission.InventoryWrite)
            .Audits("inventory.device-delete", TargetType)
            .WithName("DeleteDevice")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        GetDeviceListHandler handler,
        CancellationToken cancellationToken,
        string? cursor = null,
        int? limit = null,
        string? sort = null,
        bool descending = false,
        DeviceState? state = null,
        DeviceVendor? vendor = null,
        DeviceRole? role = null,
        CriticalityTier? criticality = null,
        DeviceEnvironment? environment = null,
        string? site = null,
        string? tag = null,
        string? search = null)
    {
        Result<PageRequest> page = PageRequest.Create(cursor, limit);

        if (!page.IsSuccess)
        {
            return Result<CursorPage<DeviceSummary>>.Failure(page.Error).ToHttpResult();
        }

        Result<DeviceSortField> field = ParseSort(sort);

        if (!field.IsSuccess)
        {
            return Result<CursorPage<DeviceSummary>>.Failure(field.Error).ToHttpResult();
        }

        DeviceListQuery query = new(
            page.Value,
            field.Value,
            descending,
            state,
            vendor,
            role,
            criticality,
            environment,
            site,
            tag,
            search);

        return (await handler.HandleAsync(query, cancellationToken)).ToHttpResult();
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        GetDeviceHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();

    private static async Task<IResult> CreateAsync(
        CreateDeviceRequest request,
        CreateDeviceHandler handler,
        CancellationToken cancellationToken)
    {
        Result<DeviceDetail> result = await handler.HandleAsync(request, cancellationToken);

        // 201 with a Location header (CONVENTIONS.md §4). ToHttpResult answers 200, which is
        // right everywhere else and wrong here, so the created case is written out.
        return result.IsSuccess
            ? TypedResults.Created($"{RoutePrefix}/{result.Value.Id}", result.Value)
            : result.ToHttpResult();
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateDeviceRequest request,
        UpdateDeviceHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToHttpResult();

    private static async Task<IResult> DeleteAsync(
        Guid id,
        DeleteDeviceHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();

    /// <summary>
    /// Reads the <c>sort</c> query parameter. Unrecognised is a 400 naming what is available,
    /// rather than a silent fall back to the default — a caller who misspells a field and is
    /// served a differently ordered page has no way to notice.
    /// </summary>
    private static Result<DeviceSortField> ParseSort(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            return DeviceSortField.CreatedAt;
        }

        return Enum.TryParse(sort, ignoreCase: true, out DeviceSortField field)
            ? field
            : DeviceErrors.UnknownSort(sort, Enum.GetNames<DeviceSortField>());
    }
}
