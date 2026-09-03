using NetShield.Contracts.Identity;

namespace NetShield.Identity.Users;

/// <summary>
/// A local NetShield account. SSO-backed identities join it in the Administration work of
/// Phase 8; nothing here assumes local is the only kind.
/// </summary>
/// <remarks>
/// The entity never leaves the module (ARCHITECTURE.md §4). <see cref="PasswordHash"/> exists on
/// exactly one type, and that type has no path to a response shape.
/// </remarks>
public sealed class User
{
    /// <summary>UUID v7, so the primary key is also the creation order (CONVENTIONS.md §3).</summary>
    public Guid Id { get; init; }

    /// <summary>The name typed at sign-in, stored as entered.</summary>
    public required string Username { get; set; }

    /// <summary>
    /// <see cref="Username"/> lower-cased, which is what carries the uniqueness constraint and
    /// what a sign-in looks up by. Two accounts differing only in case are the same account.
    /// </summary>
    public required string NormalizedUsername { get; set; }

    /// <summary>The name shown in the header's user block.</summary>
    public required string DisplayName { get; set; }

    /// <summary>Where notifications addressed to this person go. Optional.</summary>
    public string? Email { get; set; }

    /// <summary>The encoded Argon2id hash. Never rendered, never returned, never logged.</summary>
    public required string PasswordHash { get; set; }

    /// <summary>The role claim the session carries. Nothing enforces it before WP-0.5.</summary>
    public UserRole Role { get; set; }

    /// <summary>Whether every request other than a password change must be refused.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>Whether the account may sign in at all.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Consecutive failed sign-ins since the last success. Reset by a success.</summary>
    public int FailedLoginAttempts { get; set; }

    /// <summary>When the lockout lapses, or <see langword="null"/> when the account is not locked.</summary>
    public DateTimeOffset? LockedOutUntil { get; set; }

    /// <summary>The last successful sign-in. UTC.</summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>When the stored hash was last replaced. UTC.</summary>
    public DateTimeOffset PasswordChangedAt { get; set; }

    /// <summary>When the account was created. UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the row last changed. UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Whether the account is locked at <paramref name="now"/>. A lapsed lockout is simply not a
    /// lockout — the row is left as it is until the next sign-in decides what to do with it.
    /// </summary>
    public bool IsLockedOut(DateTimeOffset now) => LockedOutUntil is { } until && until > now;
}
