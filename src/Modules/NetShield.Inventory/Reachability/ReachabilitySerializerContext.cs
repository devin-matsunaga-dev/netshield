using System.Text.Json.Serialization;

namespace NetShield.Inventory.Reachability;

/// <summary>
/// Serialises the two reachability payloads: the parameters going into a job row, and the result
/// coming back out of one.
/// </summary>
/// <remarks>
/// Internal, and never added to <c>ConfigureHttpJsonOptions</c>. Neither shape is part of any
/// contract a caller can reach — the parameters are written by the scheduler and read by the
/// collector out of a lease, and the result is written by the collector and read here — so
/// neither belongs in the context describing the API, nor in the one describing the collector
/// contract's envelope. Every member names its JSON property explicitly for the reason
/// <c>CredentialMaterialPayload</c> does: a column written today has to still parse in five
/// years, whatever a later refactor calls the C# member.
/// </remarks>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(IcmpProbeParameters))]
[JsonSerializable(typeof(IcmpProbeResult))]
internal sealed partial class ReachabilitySerializerContext : JsonSerializerContext;
