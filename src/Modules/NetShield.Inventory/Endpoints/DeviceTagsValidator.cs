using FluentValidation;

using NetShield.Inventory.Devices;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// Bounds the tag list. The values themselves are normalised rather than rejected — case and
/// surrounding whitespace are not a caller's mistake to be told about — but the count and the
/// length are bounds the column has to be able to hold.
/// </summary>
public sealed class DeviceTagsValidator : AbstractValidator<IReadOnlyList<string>>
{
    public DeviceTagsValidator()
    {
        RuleFor(tags => tags.Count)
            .LessThanOrEqualTo(DeviceTags.MaximumCount)
            .WithName("tags")
            .WithMessage($"A device carries at most {DeviceTags.MaximumCount} tags.");

        RuleForEach(tags => tags)
            .MaximumLength(DeviceTags.MaximumLength)
            .WithName("tags");
    }
}
