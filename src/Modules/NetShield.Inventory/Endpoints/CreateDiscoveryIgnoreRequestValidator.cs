using FluentValidation;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Discovery;

namespace NetShield.Inventory.Endpoints;

/// <summary>Shape validation for <see cref="CreateDiscoveryIgnoreRequest"/> (CONVENTIONS.md §4).</summary>
/// <remarks>
/// Whether the block is already ignored is a question about the list rather than about the
/// request, and it is answered by the handler and finally by the unique index.
/// </remarks>
public sealed class CreateDiscoveryIgnoreRequestValidator
    : AbstractValidator<CreateDiscoveryIgnoreRequest>
{
    public CreateDiscoveryIgnoreRequestValidator()
    {
        RuleFor(request => request.Cidr)
            .NotEmpty()
            .MaximumLength(DiscoveryLimits.CidrLength)
            .Must(value => AddressRange.Parse(value).IsSuccess)
            .WithMessage("Must be an IP address or a CIDR block, such as 10.0.0.0/24.");

        RuleFor(request => request.Reason).MaximumLength(DiscoveryLimits.ReasonLength);
    }
}
