using System.Text.Json.Serialization;

namespace NetShield.Inventory.Resolution;

/// <summary>
/// Serialises the cached address history.
/// </summary>
/// <remarks>
/// Source-generated rather than reflection-based, unlike <c>OutboxPayload</c> and
/// <c>AuditPayload</c>: this runs on the resolution path that ARCHITECTURE.md §6 puts inside the
/// ingest pipeline, and the whole point of the cache is that the warm path is cheap.
///
/// Internal, and never added to <c>ConfigureHttpJsonOptions</c>: nothing here is part of a
/// contract a caller can reach. Every member names its JSON property explicitly, so that renaming
/// a C# member cannot make every entry already in Redis unreadable — a cache that silently stops
/// deserialising is a cache that silently stops being a cache.
/// </remarks>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(AddressHistory))]
internal sealed partial class ResolutionSerializerContext : JsonSerializerContext;
