using System.Text.Json.Serialization;

using NetShield.Contracts.Collector;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Collector.Contract;

namespace NetShield.Inventory.Collector;

/// <summary>
/// The source-generated serialiser for the internal collector contract.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <c>InventorySerializerContext</c>, and internal, because the shapes it writes
/// include an opened credential. The public context describes the API the SPA is generated
/// from; this one describes a contract that never appears in the OpenAPI document and never
/// reaches a browser. Keeping them apart is what stops a later package adding a response type to
/// the wrong one by reflex.
/// </para>
/// <para>
/// It is registered on the host's JSON options, which is unavoidable — the endpoints have to be
/// able to write their responses. The guarantee is made elsewhere and structurally: the types
/// are internal, they are absent from the document, and an architecture test fails the build if
/// any of them becomes public.
/// </para>
/// <para>
/// An absent member is written as <c>null</c> rather than omitted. A source-generated context
/// used as one resolver among several contributes its naming policy and its converters but not
/// its ignore condition, so declaring one here would say something that is not true of the
/// output; the collector's models read a missing member and an explicit null identically, so
/// there is nothing to gain by making it true a member at a time.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = [typeof(JsonStringEnumConverter<CollectorJobKind>),
        typeof(JsonStringEnumConverter<CollectorJobOutcome>),
        typeof(JsonStringEnumConverter<CredentialKind>),
        typeof(JsonStringEnumConverter<SnmpAuthAlgorithm>),
        typeof(JsonStringEnumConverter<SnmpPrivacyAlgorithm>),
        typeof(JsonStringEnumConverter<DeviceVendor>)])]
[JsonSerializable(typeof(CollectorJobBatch))]
[JsonSerializable(typeof(CollectorResultsRequest))]
[JsonSerializable(typeof(CollectorResultsAck))]
[JsonSerializable(typeof(CollectorHeartbeatRequest))]
[JsonSerializable(typeof(CollectorHeartbeatAck))]
internal sealed partial class CollectorSerializerContext : JsonSerializerContext;
