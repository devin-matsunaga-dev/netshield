using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Credentials.Handlers;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Results;
using NetShield.Platform.Validation;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// Which credential profiles a device may be reached with, under
/// <c>/api/v1/devices/{deviceId}/credential-profiles</c>.
/// </summary>
/// <remarks>
/// A file of its own rather than more routes on <c>DeviceEndpoints</c>: the assignment is its own
/// resource with its own permission, and CONVENTIONS.md §4 groups endpoints one file per
/// resource. It hangs under the device because that is what a caller has in hand — "what may I
/// reach this device with" is the question, and the device is the subject of it.
/// </remarks>
public static class DeviceCredentialProfileEndpoints
{
    /// <summary>The route both verbs hang from.</summary>
    public const string RoutePattern = "/api/v1/devices/{deviceId:guid}/credential-profiles";

    /// <summary>Maps the assignment endpoints. Called once, by the composition root.</summary>
    public static IEndpointRouteBuilder MapDeviceCredentialProfileEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(RoutePattern, ListAsync)
            .WithTags("Credential profiles")
            .RequirePermission(Permission.CredentialsManage)
            .WithName("ListDeviceCredentialProfiles")
            .Produces<IReadOnlyList<CredentialProfileSummary>>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // The audit row is about the device, because the device is what changed. It is recorded
        // under an action of its own so that "who gave this device a credential" is a query over
        // the audit log rather than a diff of two device snapshots.
        endpoints.MapPut(RoutePattern, SetAsync)
            .WithTags("Credential profiles")
            .AddEndpointFilter<ValidationFilter<SetDeviceCredentialProfilesRequest>>()
            .RequirePermission(Permission.CredentialsManage)
            .Audits("inventory.device-credentials-set", "device")
            .WithName("SetDeviceCredentialProfiles")
            .Produces<IReadOnlyList<CredentialProfileSummary>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        Guid deviceId,
        GetDeviceCredentialProfilesHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(deviceId, cancellationToken)).ToHttpResult();

    private static async Task<IResult> SetAsync(
        Guid deviceId,
        SetDeviceCredentialProfilesRequest request,
        SetDeviceCredentialProfilesHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(deviceId, request, cancellationToken)).ToHttpResult();
}
