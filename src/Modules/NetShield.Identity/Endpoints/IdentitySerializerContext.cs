using System.Text.Json.Serialization;

using NetShield.Contracts.Identity;

namespace NetShield.Identity.Endpoints;

/// <summary>
/// The source-generated serialiser for the identity contract (CONVENTIONS.md §4).
/// </summary>
/// <remarks>
/// It lists the request and response shapes and nothing else. <c>SessionGrant</c> is absent by
/// design: it carries a refresh token, and an endpoint that returned it by mistake fails on the
/// first request rather than putting the token in a response body.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(ChangePasswordRequest))]
[JsonSerializable(typeof(AuthenticatedUser))]
public sealed partial class IdentitySerializerContext : JsonSerializerContext;
