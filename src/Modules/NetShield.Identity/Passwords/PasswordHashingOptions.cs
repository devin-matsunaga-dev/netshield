using System.ComponentModel.DataAnnotations;

namespace NetShield.Identity.Passwords;

/// <summary>
/// The Argon2id work factors. Bound from <c>Identity:PasswordHashing</c> so an operator can
/// raise them on faster hardware without a code change; a stored hash carries the parameters it
/// was made with, so raising these rehashes accounts as they next sign in rather than locking
/// anybody out.
/// </summary>
public sealed class PasswordHashingOptions
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "Identity:PasswordHashing";

    /// <summary>
    /// Memory cost in KiB. Defaults to the OWASP minimum for Argon2id at <c>t=2, p=1</c>.
    /// </summary>
    [Range(8 * 1024, 1024 * 1024)]
    public int MemoryKib { get; set; } = 19 * 1024;

    /// <summary>Time cost — the number of passes over the memory block.</summary>
    [Range(1, 16)]
    public int Iterations { get; set; } = 2;

    /// <summary>Lanes computed in parallel.</summary>
    [Range(1, 16)]
    public int Parallelism { get; set; } = 1;

    /// <summary>Salt length in bytes. 128 bits is the Argon2 specification's recommendation.</summary>
    [Range(16, 64)]
    public int SaltBytes { get; set; } = 16;

    /// <summary>Derived-key length in bytes.</summary>
    [Range(32, 64)]
    public int HashBytes { get; set; } = 32;
}
