using FluentValidation;

using Microsoft.Extensions.Options;

using NetShield.Inventory.Discovery;

using NetShield.Platform.Results;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// Validates the ranges and exclusions of a discovery seed, which the create and update requests
/// both carry.
/// </summary>
/// <remarks>
/// <para>
/// One validator for both, because the two shapes say the same thing about a seed and two copies
/// of these rules would be two places for them to drift. It is the only validator in the module
/// that reads configuration: how large a seed may be is a property of the installation rather
/// than of the request shape, and refusing a /8 at the endpoint is what stops a typing mistake
/// from becoming sixty-five thousand queued jobs.
/// </para>
/// <para>
/// Shape and size only. Whether the name is taken is a question about the estate and is answered
/// by the handler, the way <c>CreateDeviceRequestValidator</c> leaves the duplicate address to
/// the handler and the index.
/// </para>
/// </remarks>
internal sealed class DiscoverySeedRangesValidator : AbstractValidator<SeedRanges>
{
    internal DiscoverySeedRangesValidator(IOptions<DiscoveryOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        RuleFor(seed => seed.Ranges)
            .NotEmpty()
            .WithMessage("A seed must name at least one range to sweep.")
            .Must(values => values is null || values.Count <= DiscoveryLimits.MaxRangesPerSeed)
            .WithMessage($"A seed may name at most {DiscoveryLimits.MaxRangesPerSeed} ranges.");

        RuleFor(seed => seed.Exclusions)
            .Must(values => values is null || values.Count <= DiscoveryLimits.MaxExclusionsPerSeed)
            .WithMessage($"A seed may name at most {DiscoveryLimits.MaxExclusionsPerSeed} exclusions.");

        RuleForEach(seed => seed.Ranges)
            .MaximumLength(DiscoveryLimits.CidrLength)
            .Must(BeABlock)
            .WithMessage("Must be an IP address or a CIDR block, such as 10.0.0.0/24.");

        RuleForEach(seed => seed.Exclusions)
            .MaximumLength(DiscoveryLimits.CidrLength)
            .Must(BeABlock)
            .WithMessage("Must be an IP address or a CIDR block, such as 10.0.0.128/25.");

        // The two rules below need every value to have parsed, so they run only once the ones
        // above have passed. Reporting "these ranges overlap" about a list that holds something
        // which is not a range at all would send the reader looking in the wrong place.
        RuleFor(seed => seed)
            .Must(NotOverlap)
            .WithName(nameof(SeedRanges.Ranges))
            .WithMessage("Two of these ranges cover the same addresses. Ranges must not overlap.")
            .Must(seed => WithinCeiling(seed, options.Value))
            .WithName(nameof(SeedRanges.Ranges))
            .WithMessage(
                "These ranges hold more addresses than one discovery run may sweep "
                + $"({options.Value.MaxAddressesPerRun}). Narrow them, or exclude part of them.")
            .When(EveryValueParses);
    }

    private static bool BeABlock(string? value) => AddressRange.Parse(value).IsSuccess;

    private static bool EveryValueParses(SeedRanges seed) => Plan(seed).IsSuccess;

    private static bool NotOverlap(SeedRanges seed) =>
        Plan(seed) is { IsSuccess: true } plan && plan.Value.FirstOverlap() is null;

    private static bool WithinCeiling(SeedRanges seed, DiscoveryOptions options) =>
        Plan(seed) is { IsSuccess: true } plan && plan.Value.AddressCount <= options.MaxAddressesPerRun;

    private static Result<SweepPlan> Plan(SeedRanges seed) =>
        SweepPlan.Create(seed.Ranges ?? [], seed.Exclusions);
}
