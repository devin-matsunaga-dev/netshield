using System.Text.Json.Serialization;

namespace NetShield.Inventory.Clients;

/// <summary>
/// Serialises the client-walk payloads: the parameters going into a job row, and the result
/// coming back out of one.
/// </summary>
/// <remarks>
/// Internal, and never added to <c>ConfigureHttpJsonOptions</c>, for the reason
/// <c>DiscoverySerializerContext</c> is not: neither shape is part of any contract a caller can
/// reach. Every member names its JSON property explicitly because a column written today has to
/// still parse in five years, whatever a later refactor calls the C# member.
/// </remarks>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(ClientWalkParameters))]
[JsonSerializable(typeof(ClientWalkResult))]
internal sealed partial class ClientSerializerContext : JsonSerializerContext;
