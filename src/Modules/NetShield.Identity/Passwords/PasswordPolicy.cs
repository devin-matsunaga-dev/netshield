using Microsoft.Extensions.Options;

using NetShield.Platform.Results;

namespace NetShield.Identity.Passwords;

/// <summary>
/// Decides whether a proposed password may be stored. Applied wherever a password is set — the
/// change-password endpoint and the first-run seeder alike — so there is one answer rather than
/// one per call site.
/// </summary>
/// <remarks>
/// This is a semantic rejection, not a malformed request: the body parsed, the field was present,
/// and the value was understood and refused. CONVENTIONS.md §4 answers that with <c>422</c>.
/// </remarks>
public sealed class PasswordPolicy(IOptions<PasswordPolicyOptions> options)
{
    /// <summary>The error code every policy rejection carries.</summary>
    public const string RejectionCode = "identity.password-policy";

    /// <summary>The options in force, so a caller can describe the rules without restating them.</summary>
    public PasswordPolicyOptions Options => options.Value;

    /// <summary>
    /// Checks <paramref name="password"/> against the policy.
    /// </summary>
    /// <param name="password">The proposed password.</param>
    /// <param name="username">The account it is for, which it may not simply repeat.</param>
    /// <param name="email">The account's email address, likewise. Optional.</param>
    public Result Check(string password, string username, string? email)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(username);

        PasswordPolicyOptions policy = Options;
        List<string> failures = [];

        if (password.Length < policy.MinimumLength)
        {
            failures.Add($"It must be at least {policy.MinimumLength} characters long.");
        }

        if (password.Length > policy.MaximumLength)
        {
            failures.Add($"It must be no more than {policy.MaximumLength} characters long.");
        }

        if (CountCharacterClasses(password) < policy.RequiredCharacterClasses)
        {
            failures.Add(
                $"It must use at least {policy.RequiredCharacterClasses} of lowercase letters, "
                + "uppercase letters, digits and symbols.");
        }

        if (Matches(password, username) || Matches(password, email))
        {
            failures.Add("It must not repeat the username or the email address.");
        }

        return failures.Count == 0
            ? Result.Success
            : Error.Unprocessable(RejectionCode, "That password does not meet the password policy.") with
            {
                // Keyed to the request field so the client can render the list beside the input.
                Failures = new Dictionary<string, string[]> { ["newPassword"] = [.. failures] }
            };
    }

    private static bool Matches(string password, string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && string.Equals(password, value, StringComparison.OrdinalIgnoreCase);

    private static int CountCharacterClasses(string password)
    {
        bool lower = false;
        bool upper = false;
        bool digit = false;
        bool other = false;

        foreach (char character in password)
        {
            if (char.IsLower(character))
            {
                lower = true;
            }
            else if (char.IsUpper(character))
            {
                upper = true;
            }
            else if (char.IsDigit(character))
            {
                digit = true;
            }
            else
            {
                other = true;
            }
        }

        return (lower ? 1 : 0) + (upper ? 1 : 0) + (digit ? 1 : 0) + (other ? 1 : 0);
    }
}
