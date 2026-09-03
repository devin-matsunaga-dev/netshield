using System.Security.Claims;

using NetShield.Contracts.Identity;

namespace NetShield.Platform.Authorization;

/// <summary>
/// The claims the platform's authorization pipeline reads out of a session principal.
/// </summary>
/// <remarks>
/// They are declared in Platform rather than in Identity because Platform is what reads them and
/// a module may not reference another module (ARCHITECTURE.md §4). Identity mints them; anything
/// that later mints a session — SSO in Phase 8 — mints the same ones.
/// </remarks>
public static class AuthorizationClaims
{
    /// <summary>
    /// Present, with the value <see cref="PasswordChangeRequiredValue"/>, while the account still
    /// owes a password change. Absent otherwise.
    /// </summary>
    /// <remarks>
    /// A claim rather than a database read, because the handler that enforces it lives in
    /// Platform and Platform cannot see the <c>users</c> table. It is re-minted on every sign-in
    /// and on the password change that clears it, which are the two moments the value can
    /// change for the holder of the session.
    /// </remarks>
    public const string PasswordChangeRequired = "netshield:pwd-change-required";

    /// <summary>The only value <see cref="PasswordChangeRequired"/> is ever written with.</summary>
    public const string PasswordChangeRequiredValue = "true";

    /// <summary>The role the session was minted with, or <see langword="null"/> if it carries none.</summary>
    public static UserRole? RoleOf(ClaimsPrincipal? principal) =>
        Enum.TryParse(principal?.FindFirstValue(ClaimTypes.Role), ignoreCase: false, out UserRole role)
            ? role
            : null;

    /// <summary>Whether this session still owes a password change.</summary>
    public static bool PasswordChangeIsPending(ClaimsPrincipal? principal) =>
        string.Equals(
            principal?.FindFirstValue(PasswordChangeRequired),
            PasswordChangeRequiredValue,
            StringComparison.Ordinal);
}
