using System.Security.Claims;

using NetShield.Contracts.Identity;

using NetShield.Identity.Users;

namespace NetShield.Identity.Authentication;

/// <summary>
/// What a NetShield session cookie carries, and the only place a principal is built from a user.
/// </summary>
/// <remarks>
/// The claim set is deliberately thin. Anything that can change while a session is open — the
/// display name, whether a password change is still owed, whether the account was disabled — is
/// read from the database when it is needed rather than trusted from a cookie minted earlier
/// (ARCHITECTURE.md §8: never trust the client's claim of role).
/// </remarks>
public static class SessionClaims
{
    /// <summary>The chain the session's refresh tokens belong to, so logout can end it.</summary>
    public const string SessionId = "netshield:sid";

    /// <summary>Builds the principal the session cookie is written from.</summary>
    public static ClaimsPrincipal CreatePrincipal(User user, Guid sessionId, string authenticationScheme)
    {
        ArgumentNullException.ThrowIfNull(user);

        ClaimsIdentity identity = new(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim(SessionId, sessionId.ToString())
            ],
            authenticationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role);

        return new ClaimsPrincipal(identity);
    }

    /// <summary>The signed-in user's id, or <see langword="null"/> when there is no session.</summary>
    public static Guid? UserIdOf(ClaimsPrincipal? principal) =>
        Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out Guid id) ? id : null;

    /// <summary>The session's refresh-token chain, or <see langword="null"/> when there is none.</summary>
    public static Guid? SessionIdOf(ClaimsPrincipal? principal) =>
        Guid.TryParse(principal?.FindFirstValue(SessionId), out Guid id) ? id : null;

    /// <summary>The role the session was minted with. Nothing enforces it before WP-0.5.</summary>
    public static UserRole? RoleOf(ClaimsPrincipal? principal) =>
        Enum.TryParse(principal?.FindFirstValue(ClaimTypes.Role), out UserRole role) ? role : null;
}
