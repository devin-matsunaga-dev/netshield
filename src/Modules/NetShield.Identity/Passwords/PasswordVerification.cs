namespace NetShield.Identity.Passwords;

/// <summary>What verifying a password against a stored hash established.</summary>
/// <param name="IsMatch">Whether the password produced the stored digest.</param>
/// <param name="NeedsRehash">
/// Whether the stored hash was made with weaker parameters than the ones configured now. Only
/// meaningful when <paramref name="IsMatch"/> is <see langword="true"/> — that is the one moment
/// the plaintext is available to rehash with.
/// </param>
public sealed record PasswordVerification(bool IsMatch, bool NeedsRehash)
{
    /// <summary>The answer for a wrong password, a malformed hash, or an unknown account.</summary>
    public static PasswordVerification Failed { get; } = new(IsMatch: false, NeedsRehash: false);
}
