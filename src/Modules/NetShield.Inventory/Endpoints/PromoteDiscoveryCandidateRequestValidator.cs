using FluentValidation;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Devices;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// Shape validation for <see cref="PromoteDiscoveryCandidateRequest"/> (CONVENTIONS.md §4).
/// </summary>
/// <remarks>
/// The same rules <c>CreateDeviceRequestValidator</c> applies to the members the two shapes
/// share, because promotion creates a device and a device created this way must not be able to
/// hold something a device created by hand could not.
/// </remarks>
public sealed class PromoteDiscoveryCandidateRequestValidator
    : AbstractValidator<PromoteDiscoveryCandidateRequest>
{
    public PromoteDiscoveryCandidateRequestValidator()
    {
        RuleFor(request => request.Hostname).NotEmpty().MaximumLength(DeviceLimits.HostnameLength);
        RuleFor(request => request.Site).MaximumLength(DeviceLimits.AttributeLength);
        RuleFor(request => request.Owner).MaximumLength(DeviceLimits.AttributeLength);
        RuleFor(request => request.Notes).MaximumLength(DeviceLimits.NotesLength);

        RuleFor(request => request.Role).IsInEnum();
        RuleFor(request => request.Criticality).IsInEnum();
        RuleFor(request => request.Environment).IsInEnum();

        RuleFor(request => request.Tags!)
            .SetValidator(new DeviceTagsValidator())
            .When(request => request.Tags is not null);
    }
}
