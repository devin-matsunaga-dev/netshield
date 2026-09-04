using System.Net;

using FluentValidation;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Devices;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// Shape validation for <see cref="CreateDeviceRequest"/> (CONVENTIONS.md §4).
/// </summary>
/// <remarks>
/// Shape only. Whether the address is already taken is a question about the estate, not about
/// the request, and it is answered by the handler and finally by the unique index — a validator
/// that queried the database would be making a promise it cannot keep past the next millisecond.
/// </remarks>
public sealed class CreateDeviceRequestValidator : AbstractValidator<CreateDeviceRequest>
{
    public CreateDeviceRequestValidator()
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
