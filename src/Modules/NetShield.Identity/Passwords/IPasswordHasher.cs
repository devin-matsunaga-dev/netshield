namespace NetShield.Identity.Passwords;

/// <summary>
/// Turns a password into something safe to store, and checks one against what was stored.
/// </summary>
/// <remarks>
/// The plaintext reaches this interface and goes no further. Nothing here logs, and no caller is
/// given a reason to render either argument (SPEC.md §5).
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>Hashes <paramref name="password"/> with a fresh salt and the configured costs.</summary>
    Task<string> HashAsync(string password, CancellationToken cancellationToken);

    /// <summary>
    /// Checks <paramref name="password"/> against <paramref name="encodedHash"/> in constant time
    /// with respect to the digest.
    /// </summary>
    Task<PasswordVerification> VerifyAsync(string password, string? encodedHash, CancellationToken cancellationToken);
}
