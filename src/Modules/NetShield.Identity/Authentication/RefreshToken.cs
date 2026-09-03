using NetShield.Identity.Users;

namespace NetShield.Identity.Authentication;

/// <summary>
/// One issued refresh token. Rotation makes these a chain: each refresh revokes the token it was
/// presented with and records the one that replaced it.
/// </summary>
/// <remarks>
/// The token itself is not here. Only its SHA-256 digest is stored, so a copy of this table is
/// not a set of usable sessions — the same reasoning that puts a hash in the password column.
/// </remarks>
public sealed class RefreshToken
{
    /// <summary>UUID v7.</summary>
    public Guid Id { get; init; }

    /// <summary>The account this token signs in.</summary>
    public Guid UserId { get; init; }

    /// <summary>The account this token signs in.</summary>
    public User? User { get; init; }

    /// <summary>
    /// The SHA-256 digest of the presented token, hex-encoded. A plain digest and no salt is
    /// right here and wrong for a password: the input is 256 bits of entropy this process
    /// generated, so there is no dictionary to build against it.
    /// </summary>
    public required string TokenHash { get; init; }

    /// <summary>
    /// The chain this token belongs to — the id of the token issued at sign-in. Revoking a chain
    /// ends the session it descends from, however many rotations ago that started.
    /// </summary>
    public Guid SessionId { get; init; }

    /// <summary>When the token was issued. UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the token stops being accepted. UTC.</summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>When the token was rotated away, logged out, or revoked. UTC.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>The token issued in its place by a rotation.</summary>
    public Guid? ReplacedByTokenId { get; set; }

    /// <summary>Whether the token may still be presented at <paramref name="now"/>.</summary>
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
