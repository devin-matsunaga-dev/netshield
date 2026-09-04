using FluentValidation;

using Microsoft.Extensions.Options;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Discovery;

namespace NetShield.Inventory.Endpoints;

/// <summary>Shape validation for <see cref="CreateDiscoverySeedRequest"/> (CONVENTIONS.md §4).</summary>
public sealed class CreateDiscoverySeedRequestValidator : AbstractValidator<CreateDiscoverySeedRequest>
{
    public CreateDiscoverySeedRequestValidator(IOptions<DiscoveryOptions> options)
    {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(DiscoveryLimits.SeedNameLength);
        RuleFor(request => request.Description).MaximumLength(DiscoveryLimits.ReasonLength);
        RuleFor(request => request.IntervalMinutes).InclusiveBetween(1, 44_640);

        RuleFor(request => new SeedRanges(request.Ranges, request.Exclusions))
            .SetValidator(new DiscoverySeedRangesValidator(options));
    }
}
