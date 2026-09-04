using FluentValidation;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Credentials;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// Shape validation for the secret half of a request: lengths, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Which members are required is not checked here and cannot be. That depends on the profile's
/// kind, which for a rotation is a fact about the stored row rather than about the request — so
/// it is a semantic rule, it lives in <see cref="CredentialKindRules"/>, and it answers 422
/// rather than 400 (CONVENTIONS.md §4).
/// </para>
/// <para>
/// No rule here quotes a value into its message. A FluentValidation message can interpolate the
/// property value by default, and a validation response naming the community string that was too
/// long would be SPEC.md §5 broken by a helpful error.
/// </para>
/// </remarks>
public sealed class CredentialMaterialValidator : AbstractValidator<CredentialMaterial>
{
    public CredentialMaterialValidator()
    {
        RuleFor(material => material.Community)
            .MaximumLength(CredentialLimits.SecretLength)
            .WithMessage("The community string is too long.");

        RuleFor(material => material.AuthPassword)
            .MaximumLength(CredentialLimits.SecretLength)
            .WithMessage("The authentication pass phrase is too long.");

        RuleFor(material => material.PrivacyPassword)
            .MaximumLength(CredentialLimits.SecretLength)
            .WithMessage("The privacy pass phrase is too long.");

        RuleFor(material => material.Password)
            .MaximumLength(CredentialLimits.SecretLength)
            .WithMessage("The password is too long.");

        RuleFor(material => material.PrivateKey)
            .MaximumLength(CredentialLimits.PrivateKeyLength)
            .WithMessage("The private key is too long.");

        RuleFor(material => material.PrivateKeyPassword)
            .MaximumLength(CredentialLimits.SecretLength)
            .WithMessage("The private key pass phrase is too long.");
    }
}
