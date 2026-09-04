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
/// internal and has no path to a response; every enum is written as its name rather than its
/// ordinal, so inserting a member cannot change what a stored response already means.
/// </para>
/// <para>
/// <c>CredentialMaterial</c> is here because it is on two requests. Its counterpart at rest,
/// <c>CredentialMaterialPayload</c>, is deliberately absent — that type is what plaintext
/// credentials are shaped as inside the sealed blob, it has a serialiser of its own that only the
/// encrypt and decrypt paths can reach, and a test fails the build if this context ever learns to
/// write one.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = [typeof(JsonStringEnumConverter<DeviceVendor>),
        typeof(JsonStringEnumConverter<DeviceRole>),
        typeof(JsonStringEnumConverter<DeviceState>),
        typeof(JsonStringEnumConverter<CriticalityTier>),
        typeof(JsonStringEnumConverter<DeviceEnvironment>),
        typeof(JsonStringEnumConverter<CredentialKind>),
        typeof(JsonStringEnumConverter<SnmpAuthAlgorithm>),
        typeof(JsonStringEnumConverter<SnmpPrivacyAlgorithm>)])]
[JsonSerializable(typeof(CreateDeviceRequest))]
[JsonSerializable(typeof(UpdateDeviceRequest))]
[JsonSerializable(typeof(DeviceDetail))]
[JsonSerializable(typeof(DeviceSummary))]
[JsonSerializable(typeof(DeviceWalkQueued))]
[JsonSerializable(typeof(CursorPage<DeviceSummary>))]
[JsonSerializable(typeof(CreateCredentialProfileRequest))]
[JsonSerializable(typeof(UpdateCredentialProfileRequest))]
[JsonSerializable(typeof(ReplaceCredentialMaterialRequest))]
[JsonSerializable(typeof(SetDeviceCredentialProfilesRequest))]
[JsonSerializable(typeof(CredentialProfileDetail))]
[JsonSerializable(typeof(CredentialProfileSummary))]
[JsonSerializable(typeof(IReadOnlyList<CredentialProfileSummary>))]
[JsonSerializable(typeof(CursorPage<CredentialProfileSummary>))]
public sealed partial class InventorySerializerContext : JsonSerializerContext;
