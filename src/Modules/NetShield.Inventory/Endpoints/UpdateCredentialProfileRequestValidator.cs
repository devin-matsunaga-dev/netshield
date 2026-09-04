using FluentValidation;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Credentials;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// Shape validation for <see cref="UpdateCredentialProfileRequest"/> (CONVENTIONS.md §4).
/// </summary>
public sealed class UpdateCredentialProfileRequestValidator
    : AbstractValidator<UpdateCredentialProfileRequest>
{
    public UpdateCredentialProfileRequestValidator()
    {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(CredentialLimits.NameLength);
        RuleFor(request => request.Description).MaximumLength(CredentialLimits.DescriptionLength);
        RuleFor(request => request.Username).MaximumLength(CredentialLimits.UsernameLength);

        RuleFor(request => request.AuthAlgorithm!).IsInEnum().When(request => request.AuthAlgorithm is not null);
        RuleFor(request => request.PrivacyAlgorithm!).IsInEnum()
            .When(request => request.PrivacyAlgorithm is not null);
    }
}
