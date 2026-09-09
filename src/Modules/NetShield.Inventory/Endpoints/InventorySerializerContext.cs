using System.Text.Json.Serialization;

using NetShield.Contracts.Collector;
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
/// <c>TopologyGraph</c> names only itself: the generator reaches the node, edge, component and
/// layout shapes through it, so listing them again would be four more lines saying the same thing.
/// The shapes WP-2.1 and WP-2.2 added are meanwhile absent — see the note in <c>STATUS.md</c>;
/// they serialise correctly through reflection today and this list is what <c>CONVENTIONS.md</c>
/// §4 actually asks for.
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
[JsonSerializable(typeof(CollectorJobSummary))]
[JsonSerializable(typeof(CursorPage<CollectorJobSummary>))]
[JsonSerializable(typeof(DeviceReachabilityDetail))]
[JsonSerializable(typeof(ClientSummary))]
[JsonSerializable(typeof(ClientDetail))]
[JsonSerializable(typeof(ClientIpBindingSummary))]
[JsonSerializable(typeof(ClientPortBindingSummary))]
[JsonSerializable(typeof(ClientWalkQueued))]
[JsonSerializable(typeof(AssetResolution))]
[JsonSerializable(typeof(CursorPage<ClientSummary>))]
[JsonSerializable(typeof(CursorPage<ClientIpBindingSummary>))]
[JsonSerializable(typeof(CursorPage<ClientPortBindingSummary>))]
[JsonSerializable(typeof(CreateCredentialProfileRequest))]
[JsonSerializable(typeof(UpdateCredentialProfileRequest))]
[JsonSerializable(typeof(ReplaceCredentialMaterialRequest))]
[JsonSerializable(typeof(SetDeviceCredentialProfilesRequest))]
[JsonSerializable(typeof(CredentialProfileDetail))]
[JsonSerializable(typeof(CredentialProfileSummary))]
[JsonSerializable(typeof(IReadOnlyList<CredentialProfileSummary>))]
[JsonSerializable(typeof(CursorPage<CredentialProfileSummary>))]
[JsonSerializable(typeof(TopologyGraph))]
public sealed partial class InventorySerializerContext : JsonSerializerContext;
