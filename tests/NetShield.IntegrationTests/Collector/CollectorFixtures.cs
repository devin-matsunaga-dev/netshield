using System.Text.Json;

using NetShield.Contracts.Inventory;

using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Inventory;

namespace NetShield.IntegrationTests.Collector;

/// <summary>
/// The estate a collector test needs: a device to talk to, and a credential to talk to it with.
/// </summary>
/// <remarks>
/// Both are created through the API rather than written to the tables, so that the rows a lease
/// reads are the rows the rest of the system actually produces — including the sealed material,
/// which no test could construct by hand without reimplementing the envelope.
/// </remarks>
internal static class CollectorFixtures
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Creates a device and returns its id.</summary>
    public static async Task<Guid> CreateDeviceAsync(
        InventoryHost host,
        string hostname,
        string address,
        CancellationToken cancellationToken)
    {
        ApiResponse created = await host.Client.PostAsync(
            "/api/v1/devices",
            new CreateDeviceRequest(hostname, address, DeviceVendor.CiscoIos, Role: DeviceRole.Switch),
            cancellationToken);

        if (created.Status != 201)
        {
            throw new InvalidOperationException($"Could not create {hostname}: {created.Status} {created.Body}");
        }

        return Read<DeviceDetail>(created).Id;
    }

    /// <summary>Creates an SNMP v2c profile sealing <paramref name="community"/>.</summary>
    public static async Task<Guid> CreateCredentialProfileAsync(
        InventoryHost host,
        string name,
        string community,
        CancellationToken cancellationToken)
    {
        ApiResponse created = await host.Client.PostAsync(
            "/api/v1/credential-profiles",
            new CreateCredentialProfileRequest(
                name,
                CredentialKind.SnmpV2c,
                new CredentialMaterial(Community: community)),
            cancellationToken);

        if (created.Status != 201)
        {
            throw new InvalidOperationException($"Could not create {name}: {created.Status} {created.Body}");
        }

        return Read<CredentialProfileDetail>(created).Id;
    }

    private static T Read<T>(ApiResponse response) =>
        JsonSerializer.Deserialize<T>(response.Body, Json)
        ?? throw new InvalidOperationException($"Could not read a {typeof(T).Name} from {response.Body}.");
}
