using FluentValidation;

using NetShield.Contracts.Identity;

namespace NetShield.Identity.Endpoints;

/// <summary>
/// Shape validation for <see cref="LoginRequest"/> (CONVENTIONS.md §4).
/// </summary>
/// <remarks>
/// Only shape. It does not check the password against the policy, because the policy changes and
/// an account whose password predates the change must still be able to sign in and correct it.
/// The maximum length is a bound on how much Argon2id will be asked to hash, not a rule.
/// </remarks>
public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    /// <summary>The longest credential accepted at the boundary.</summary>
    public const int MaximumLength = 1024;

    public LoginRequestValidator()
    {
        RuleFor(request => request.Username).NotEmpty().MaximumLength(64);
        RuleFor(request => request.Password).NotEmpty().MaximumLength(MaximumLength);
    }
}
