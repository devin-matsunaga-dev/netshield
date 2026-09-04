using Microsoft.AspNetCore.Routing;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// The Inventory module's single registration point (CONVENTIONS.md §2).
/// </summary>
/// <remarks>
/// The module now serves three resources — devices, credential profiles, and the assignment
/// between them — and CONVENTIONS.md §2 asks for one file per resource behind one
/// <c>Map{Module}Endpoints</c> extension. This is that extension. The composition root calls it
/// and nothing else, which is also what keeps <c>ApiDocumentParityTests</c> comparing one name
/// per module rather than a list that grows with every resource.
/// </remarks>
public static class InventoryEndpoints
{
    /// <summary>Maps every inventory endpoint. Called once, by the composition root.</summary>
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapDeviceEndpoints();
        endpoints.MapCredentialProfileEndpoints();
        endpoints.MapDeviceCredentialProfileEndpoints();

        return endpoints;
    }
}
