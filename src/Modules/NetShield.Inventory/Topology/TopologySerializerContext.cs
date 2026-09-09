using System.Text.Json.Serialization;

namespace NetShield.Inventory.Topology;

/// <summary>
/// Serialises the topology-walk payloads: the parameters going into a job row, and the results
/// coming back out of one.
/// </summary>
/// <remarks>
/// Internal, and never added to <c>ConfigureHttpJsonOptions</c>, for the reason
/// <c>ClientSerializerContext</c> is not: none of these shapes is part of any contract a caller
/// can reach. Every member names its JSON property explicitly because a column written today has
/// to still parse in five years, whatever a later refactor calls the C# member.
/// </remarks>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(NeighborWalkParameters))]
[JsonSerializable(typeof(NeighborWalkResult))]
[JsonSerializable(typeof(RouteWalkParameters))]
[JsonSerializable(typeof(RouteWalkResult))]
[JsonSerializable(typeof(VlanWalkParameters))]
[JsonSerializable(typeof(VlanWalkResult))]
internal sealed partial class TopologySerializerContext : JsonSerializerContext;
