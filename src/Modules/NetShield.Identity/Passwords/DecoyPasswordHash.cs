using System.Security.Cryptography;

using Microsoft.Extensions.Options;

namespace NetShield.Identity.Passwords;

/// <summary>
/// A real Argon2id hash of a value nobody knows, verified against when the presented username
/// matches no account.
/// </summary>
/// <remarks>
/// Without it, a sign-in for an unknown user returns as soon as the lookup misses while a sign-in
/// for a known user pays for a hash — a difference of tens of milliseconds that enumerates the
/// user table over a few hundred requests. WP-0.4 requires no user enumeration, and a 401 that
/// arrives at a distinguishable time is enumeration however carefully it is worded.
/// </remarks>
public sealed class DecoyPasswordHash
{
    private readonly Task<string> _hash;

    public DecoyPasswordHash(IPasswordHasher hasher, IOptions<PasswordHashingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(options);

        // Computed once, from a value generated at startup and never stored, so that no password
        // in the system — not even a shared default — can be confirmed by comparing against it.
        _hash = hasher.HashAsync(
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            CancellationToken.None);
    }

    /// <summary>The encoded hash to verify against for an account that does not exist.</summary>
    public Task<string> ValueAsync() => _hash;
}
