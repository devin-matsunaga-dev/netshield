using System.Text.Json.Serialization;

namespace NetShield.Inventory.Discovery;

/// <summary>
/// Serialises the discovery payloads: the parameters going into a job row, and the result
/// coming back out of one, for both walks a <c>Discover</c> job can carry.
/// </summary>
/// <remarks>
/// Internal, and never added to <c>ConfigureHttpJsonOptions</c>, for the reason
/// <c>ReachabilitySerializerContext</c> is not: neither shape is part of any contract a caller
/// can reach. The parameters are written by this module and read by the collector out of a
/// lease; the result is written by the collector and read here. Every member names its JSON
/// property explicitly because a column written today has to still parse in five years, whatever
/// a later refactor calls the C# member.
/// </remarks>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(SnmpWalkParameters))]
[JsonSerializable(typeof(SnmpWalkResult))]
[JsonSerializable(typeof(RangeSweepParameters))]
[JsonSerializable(typeof(RangeSweepResult))]
internal sealed partial class DiscoverySerializerContext : JsonSerializerContext;
