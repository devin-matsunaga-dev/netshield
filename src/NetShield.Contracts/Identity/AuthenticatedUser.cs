namespace NetShield.Contracts.Identity;

/// <summary>
/// Who the current session belongs to. Returned by login, refresh and
/// <c>GET /api/v1/auth/me</c>.
/// </summary>
/// <remarks>
/// Carries no hash, no token and no lockout state. This shape is the whole of what the client
/// is told about an account, and SPEC.md §5 admits nothing else into an API response.
/// </remarks>
/// <param name="Id">The user's identifier.</param>
/// <param name="Username">The account name.</param>
/// <param name="DisplayName">The name to show in the header's user block.</param>
/// <param name="Role">The role claim carried by the session.</param>
/// <param name="MustChangePassword">
/// Whether the client must send the user to a password change before anything else. Set for a
/// seeded first-run administrator and for any password an administrator resets.
/// </param>
public sealed record AuthenticatedUser(
    Guid Id,
    string Username,
    string DisplayName,
    UserRole Role,
    bool MustChangePassword);
