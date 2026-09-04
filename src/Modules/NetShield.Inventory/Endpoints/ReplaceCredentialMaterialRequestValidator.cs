using FluentValidation;

using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// Shape validation for <see cref="ReplaceCredentialMaterialRequest"/> (CONVENTIONS.md §4).
/// </summary>
public sealed class ReplaceCredentialMaterialRequestValidator
    : AbstractValidator<ReplaceCredentialMaterialRequest>
{
    public ReplaceCredentialMaterialRequestValidator()
    {
        RuleFor(request => request.Material).NotNull().WithMessage("Material is required.");
        RuleFor(request => request.Material).SetValidator(new CredentialMaterialValidator())
            .When(request => request.Material is not null);
    }
}
