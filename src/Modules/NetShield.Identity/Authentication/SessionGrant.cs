using System.Security.Claims;

using NetShield.Contracts.Identity;

namespace NetShield.Identity.Authentication;

/// <summary>
/// Everything a successful sign-in or refresh produced, handed to the endpoint layer to turn
/// into cookies.
/// </summary>
/// <remarks>
/// This type is never serialised. It is absent from <c>IdentitySerializerContext</c> on purpose,
/// so that an endpoint returning it by mistake fails at the first request rather than shipping a
/// refresh token in a response body.
/// </remarks>
/// <param name="User">The shape the caller is told about.</param>
/// <param name="Principal">The identity the session cookie is written from.</param>
/// <param name="RefreshToken">The opaque token, in plaintext, for the refresh cookie only.</param>
/// <param name="RefreshExpiresAt">When that token stops being accepted. UTC.</param>
internal sealed record SessionGrant(
    AuthenticatedUser User,
    ClaimsPrincipal Principal,
    string RefreshToken,
    DateTimeOffset RefreshExpiresAt);
