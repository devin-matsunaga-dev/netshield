using System.ComponentModel.DataAnnotations;

namespace NetShield.Identity.Passwords;

/// <summary>
/// What NetShield will accept as a password. Bound from <c>Identity:PasswordPolicy</c>.
/// </summary>
/// <remarks>
/// Length is the rule that carries the weight; the character-class requirement is here because
/// it is the one an auditor asks for, and it is deliberately "three of four" rather than "all
/// four" so it cannot be satisfied only by putting a digit and an exclamation mark on the end.
/// </remarks>
public sealed class PasswordPolicyOptions
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "Identity:PasswordPolicy";

    /// <summary>Shortest accepted password.</summary>
    [Range(8, 256)]
    public int MinimumLength { get; set; } = 12;

    /// <summary>
    /// Longest accepted password. A bound is required: Argon2id will happily hash a megabyte,
    /// and an unbounded input is a cheap way to spend the server's memory.
    /// </summary>
    [Range(64, 1024)]
    public int MaximumLength { get; set; } = 256;

    /// <summary>
    /// How many of the four classes — lowercase, uppercase, digit, everything else — must appear.
    /// </summary>
    [Range(1, 4)]
    public int RequiredCharacterClasses { get; set; } = 3;
}
