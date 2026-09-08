using System.Text.Json.Serialization;

using NetShield.Contracts.Inventory;
using NetShield.Contracts.Paging;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// The source-generated serialiser for the inventory contract (CONVENTIONS.md §4).
/// </summary>
/// <remarks>
/// <para>
/// It lists the request and response shapes and nothing else. The entity is absent because it is
/// internal and has no path to a response.
/// </para>
/// <para>
/// It names no enum converter. It used to name eight, and they never took effect: a converter in
/// <c>JsonSourceGenerationOptions</c> applies while that context is the resolver, and this one is
/// one resolver among several on the host's JSON options — so five inventory enums travelled as
/// integers for six packages while this list said otherwise. Each enum now carries
/// <c>[JsonConverter]</c> on the type, which is the only placement that holds wherever the type
/// is serialised, and <c>ContractEnumTests</c> fails the build if a contract enum is ever added
/// without one.
/// </para>
/// <para>
/// <c>CredentialMaterial</c> is here because it is on two requests. Its counterpart at rest,
/// <c>CredentialMaterialPayload</c>, is deliberately absent — that type is what plaintext
/// credentials are shaped as inside the sealed blob, it has a serialiser of its own that only the
/// encrypt and decrypt paths can reach, and a test fails the build if this context ever learns to
/// write one.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CreateDeviceRequest))]
[JsonSerializable(typeof(UpdateDeviceRequest))]
[JsonSerializable(typeof(DeviceDetail))]
[JsonSerializable(typeof(DeviceSummary))]
[JsonSerializable(typeof(DeviceWalkQueued))]
[JsonSerializable(typeof(CursorPage<DeviceSummary>))]
[JsonSerializable(typeof(DeviceFingerprintDetail))]
[JsonSerializable(typeof(DeviceInterfaceSummary))]
[JsonSerializable(typeof(CursorPage<DeviceInterfaceSummary>))]
[JsonSerializable(typeof(DeviceReachabilityDetail))]
[JsonSerializable(typeof(CreateCredentialProfileRequest))]
[JsonSerializable(typeof(UpdateCredentialProfileRequest))]
[JsonSerializable(typeof(ReplaceCredentialMaterialRequest))]
[JsonSerializable(typeof(SetDeviceCredentialProfilesRequest))]
[JsonSerializable(typeof(CredentialProfileDetail))]
[JsonSerializable(typeof(CredentialProfileSummary))]
[JsonSerializable(typeof(IReadOnlyList<CredentialProfileSummary>))]
[JsonSerializable(typeof(CursorPage<CredentialProfileSummary>))]
public sealed partial class InventorySerializerContext : JsonSerializerContext;
