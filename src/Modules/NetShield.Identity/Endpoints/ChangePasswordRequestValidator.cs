using FluentValidation;

using NetShield.Contracts.Identity;

namespace NetShield.Identity.Endpoints;

/// <summary>
/// Shape validation for <see cref="ChangePasswordRequest"/>. The password policy itself is
/// applied by the handler, which answers <c>422</c> and can say which rules were missed.
/// </summary>
public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(request => request.CurrentPassword)
            .NotEmpty().MaximumLength(LoginRequestValidator.MaximumLength);

        RuleFor(request => request.NewPassword)
            .NotEmpty().MaximumLength(LoginRequestValidator.MaximumLength);
    }
}
