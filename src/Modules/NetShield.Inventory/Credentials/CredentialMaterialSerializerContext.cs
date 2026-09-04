using System.Text.Json.Serialization;

namespace NetShield.Inventory.Credentials;

/// <summary>
/// Serialises the plaintext material on its way into the sealed blob, and back out of it.
/// </summary>
/// <remarks>
/// Internal, and never added to <c>ConfigureHttpJsonOptions</c>. It exists so that the one type
/// carrying plaintext credentials has a serialiser that is reachable from the encrypt and
/// decrypt paths and from nowhere else — in particular, not from anything that writes a response
/// body.
/// </remarks>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CredentialMaterialPayload))]
internal sealed partial class CredentialMaterialSerializerContext : JsonSerializerContext;
