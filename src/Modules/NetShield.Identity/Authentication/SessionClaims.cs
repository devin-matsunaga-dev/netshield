using System.Security.Claims;

using NetShield.Contracts.Identity;

using NetShield.Identity.Users;

using NetShield.Platform.Authorization;

namespace NetShield.Identity.Authentication;

/// <summary>
/// What a NetShield session cookie carries, and the only place a principal is built from a user.
/// </summary>
/// <remarks>
/// The claim set is deliberately thin. Anything that can change while a session is open — the
/// display name, whether the account was disabled — is read from the database when it is needed
/// rather than trusted from a cookie minted earlier (ARCHITECTURE.md §8: never trust the
/// client's claim of role).
///
/// The one exception is <see cref="AuthorizationClaims.PasswordChangeRequired"/>, which has to
/// be a claim: the requirement that enforces it lives in <c>NetShield.Platform</c>, and a module
/// may not be reached from there to ask the <c>users</c> table (ARCHITECTURE.md §4). It is
/// re-minted on sign-in, on refresh and on the password change that clears it, so the only way
/// to hold a stale one is to have had the flag set by an administrator mid-session — which
/// nothing can do until user administration exists.
/// </remarks>
public static class SessionClaims
{
    /// <summary>The chain the session's refresh tokens belong to, so logout can end it.</summary>
    public const string SessionId = "netshield:sid";

    /// <summary>Builds the principal the session cookie is written from.</summary>
    public static ClaimsPrincipal CreatePrincipal(User user, Guid sessionId, string authenticationScheme)
    {
        ArgumentNullException.ThrowIfNull(user);

        List<Claim> claims =
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(SessionId, sessionId.ToString())
        ];

        if (user.MustChangePassword)
        {
            claims.Add(new Claim(
                AuthorizationClaims.PasswordChangeRequired,
                AuthorizationClaims.PasswordChangeRequiredValue));
        }

        ClaimsIdentity identity = new(claims, authenticationScheme, ClaimTypes.Name, ClaimTypes.Role);

        return new ClaimsPrincipal(identity);
    }

    /// <summary>The signed-in user's id, or <see langword="null"/> when there is no session.</summary>
    public static Guid? UserIdOf(ClaimsPrincipal? principal) =>
        Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out Guid id) ? id : null;

    /// <summary>The session's refresh-token chain, or <see langword="null"/> when there is none.</summary>
    public static Guid? SessionIdOf(ClaimsPrincipal? principal) =>
        Guid.TryParse(principal?.FindFirstValue(SessionId), out Guid id) ? id : null;

    /// <summary>
    /// The role the session was minted with. <c>NetShield.Platform</c> resolves it to a
    /// permission set on every request; nothing here decides what it may do.
    /// </summary>
    public static UserRole? RoleOf(ClaimsPrincipal? principal) => AuthorizationClaims.RoleOf(principal);
}
