using FluentValidation;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Devices;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// Shape validation for <see cref="UpdateDeviceRequest"/> (CONVENTIONS.md §4).
/// </summary>
/// <remarks>
/// The same rules as the create shape. They are written out rather than shared through a base
/// validator because the two requests are separate contracts that happen to agree today, and the
/// first time one of them gains a member the shared base is what would have to be unpicked.
/// </remarks>
public sealed class UpdateDeviceRequestValidator : AbstractValidator<UpdateDeviceRequest>
{
    public UpdateDeviceRequestValidator()
    {
        RuleFor(request => request.Hostname).NotEmpty().MaximumLength(DeviceLimits.HostnameLength);

        RuleFor(request => request.PrimaryIpAddress)
            .NotEmpty()
            .Must(DeviceLimits.IsAddress)
            .WithMessage("Must be an IPv4 or IPv6 address.");

        RuleFor(request => request.Model).MaximumLength(DeviceLimits.AttributeLength);
        RuleFor(request => request.OsVersion).MaximumLength(DeviceLimits.AttributeLength);
        RuleFor(request => request.SerialNumber).MaximumLength(DeviceLimits.AttributeLength);
        RuleFor(request => request.Site).MaximumLength(DeviceLimits.AttributeLength);
        RuleFor(request => request.Owner).MaximumLength(DeviceLimits.AttributeLength);
        RuleFor(request => request.Notes).MaximumLength(DeviceLimits.NotesLength);

        RuleFor(request => request.Vendor).IsInEnum();
        RuleFor(request => request.Role).IsInEnum();
        RuleFor(request => request.Criticality).IsInEnum();
        RuleFor(request => request.Environment).IsInEnum();

        RuleFor(request => request.Tags!).SetValidator(new DeviceTagsValidator()).When(r => r.Tags is not null);
    }
}
