using FluentValidation;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Credentials;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// Shape validation for <see cref="CreateCredentialProfileRequest"/> (CONVENTIONS.md §4).
/// </summary>
/// <remarks>
/// Shape only. Whether the name is taken is a question about the estate and is answered by the
/// handler and finally by the unique index; whether the material suits the kind is a question
/// about what was asked for, and <see cref="CredentialKindRules"/> answers it with a 422.
/// </remarks>
public sealed class CreateCredentialProfileRequestValidator
    : AbstractValidator<CreateCredentialProfileRequest>
{
    public CreateCredentialProfileRequestValidator()
    {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(CredentialLimits.NameLength);
        RuleFor(request => request.Description).MaximumLength(CredentialLimits.DescriptionLength);
        RuleFor(request => request.Username).MaximumLength(CredentialLimits.UsernameLength);

        RuleFor(request => request.Kind).IsInEnum();
        RuleFor(request => request.AuthAlgorithm!).IsInEnum().When(request => request.AuthAlgorithm is not null);
        RuleFor(request => request.PrivacyAlgorithm!).IsInEnum()
            .When(request => request.PrivacyAlgorithm is not null);

        RuleFor(request => request.Material).NotNull().WithMessage("Material is required.");
        RuleFor(request => request.Material).SetValidator(new CredentialMaterialValidator())
            .When(request => request.Material is not null);
    }
}
