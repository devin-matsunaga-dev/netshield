using Microsoft.AspNetCore.Routing;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// The Inventory module's single registration point (CONVENTIONS.md §2).
/// </summary>
/// <remarks>
/// The module now serves devices, what a walk and a probe established about one, the on-demand
/// fingerprint and client-table walks of one, credential profiles, the assignment between
/// profiles and devices, discovery seeds, runs, candidates and the ignore list, the tracked
/// clients with their address and port history and the resolution over them, and the internal
/// collector contract —
/// and CONVENTIONS.md §2 asks for one file per resource behind one <c>Map{Module}Endpoints</c>
/// extension. This is that extension. The composition root calls it and nothing else, which is
/// also what keeps <c>ApiDocumentParityTests</c> comparing one name per module rather than a
/// list that grows with every resource.
///
/// The collector routes are mapped from here despite not being under <c>/api</c>: they are this
/// module's endpoints, they need this module's services, and giving them a registration call of
/// their own at the composition root would put a second name in the list that parity test
/// compares — for a group the OpenAPI document deliberately does not describe.
/// </remarks>
public static class InventoryEndpoints
{
    /// <summary>Maps every inventory endpoint. Called once, by the composition root.</summary>
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapDeviceEndpoints();
        endpoints.MapDeviceDiscoveryEndpoints();
        endpoints.MapDeviceFingerprintEndpoints();
        endpoints.MapDeviceInterfaceEndpoints();
        endpoints.MapDeviceJobEndpoints();
        endpoints.MapDeviceReachabilityEndpoints();
        endpoints.MapDeviceClientWalkEndpoints();
        endpoints.MapDeviceAdjacencyEndpoints();
        endpoints.MapDeviceVlanEndpoints();
        endpoints.MapDeviceTopologyWalkEndpoints();
        endpoints.MapDiscoverySeedEndpoints();
        endpoints.MapDiscoveryRunEndpoints();
        endpoints.MapDiscoveryCandidateEndpoints();
        endpoints.MapDiscoveryIgnoreEndpoints();
        endpoints.MapClientEndpoints();
        endpoints.MapVlanEndpoints();
        endpoints.MapTopologyGraphEndpoints();
        endpoints.MapCredentialProfileEndpoints();
        endpoints.MapDeviceCredentialProfileEndpoints();
        endpoints.MapCollectorEndpoints();

        return endpoints;
    }
}
