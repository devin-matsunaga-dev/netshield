using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Discovery.Handlers;

using NetShield.Platform.Authorization;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// The fingerprint of one device, under <c>/api/v1/devices/{id}/fingerprint</c>.
/// </summary>
/// <remarks>
/// <para>
/// A sub-resource rather than members on <c>DeviceDetail</c>, because <c>device_fingerprints</c>
/// is a table of its own for the same reason: the device is the inventory an operator maintains
/// and this is what a machine observed. Every device row carries the four identity facts
/// already; what only this route can answer is where they came from, when, what else the walk
/// read, why the last one failed — and <c>reducedCapability</c>, which SPEC.md §4 requires the
/// UI to label clearly and which nothing could read until now.
/// </para>
/// <para>
/// <c>InventoryRead</c>, like every other device read. Nothing here is a credential and nothing
/// here says which credential was used.
/// </para>
/// </remarks>
public static class DeviceFingerprintEndpoints
{
    /// <summary>
    /// Maps the fingerprint route. Called by
    /// <see cref="InventoryEndpoints.MapInventoryEndpoints"/>, the module's single registration
    /// point (CONVENTIONS.md §2).
    /// </summary>
    public static IEndpointRouteBuilder MapDeviceFingerprintEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints
            .MapGroup(DeviceEndpoints.RoutePrefix)
            .WithTags("Devices");

        group.MapGet("/{id:guid}/fingerprint", GetAsync)
            .RequirePermission(Permission.InventoryRead)
            .WithName("GetDeviceFingerprint")
            .Produces<DeviceFingerprintDetail>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        GetDeviceFingerprintHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();
}
