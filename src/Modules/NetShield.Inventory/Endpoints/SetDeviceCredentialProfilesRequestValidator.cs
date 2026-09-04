using FluentValidation;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Credentials;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// Shape validation for <see cref="SetDeviceCredentialProfilesRequest"/> (CONVENTIONS.md §4).
/// </summary>
/// <remarks>
/// The count is bounded here as well as in the handler, so the list can never be an unbounded
/// collection arriving at a query. Whether each id names a live profile is a question about the
/// estate and the handler answers it.
/// </remarks>
public sealed class SetDeviceCredentialProfilesRequestValidator
    : AbstractValidator<SetDeviceCredentialProfilesRequest>
{
    public SetDeviceCredentialProfilesRequestValidator()
    {
        RuleFor(request => request.CredentialProfileIds)
            .NotNull()
            .WithMessage("credentialProfileIds is required; send an empty list to unassign everything.");

        RuleFor(request => request.CredentialProfileIds)
            .Must(ids => ids.Count <= CredentialLimits.MaximumAssignmentsPerDevice)
            .WithMessage($"A device may be assigned at most {CredentialLimits.MaximumAssignmentsPerDevice} "
                + "credential profiles.")
            .When(request => request.CredentialProfileIds is not null);

        RuleForEach(request => request.CredentialProfileIds)
            .NotEmpty()
            .WithMessage("A credential profile id must not be empty.")
            .When(request => request.CredentialProfileIds is not null);
    }
}
