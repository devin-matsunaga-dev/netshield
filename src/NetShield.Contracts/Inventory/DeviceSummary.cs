namespace NetShield.Contracts.Inventory;

/// <summary>
/// A device as the list renders it (WP-1.7's table). Deliberately narrower than
/// <see cref="DeviceDetail"/>: a 500-row page carries no notes and no serial.
/// </summary>
/// <param name="Id">The device.</param>
/// <param name="Hostname">The name it is known by. Not unique — see <see cref="DeviceDetail"/>.</param>
/// <param name="PrimaryIpAddress">The address NetShield reaches it on.</param>
/// <param name="Vendor">The platform, once something has identified it.</param>
/// <param name="Model">The hardware model, when known.</param>
/// <param name="Role">What the device is for.</param>
/// <param name="Site">Where it is, as free text.</param>
/// <param name="Criticality">How much its failure matters.</param>
/// <param name="Environment">Which environment it belongs to.</param>
/// <param name="State">Whether it is answering. Never set over the API.</param>
/// <param name="Tags">Free-form labels, lower-cased and sorted.</param>
/// <param name="UpdatedAt">When the row last changed. UTC.</param>
public sealed record DeviceSummary(
    Guid Id,
    string Hostname,
    string PrimaryIpAddress,
    DeviceVendor Vendor,
    string? Model,
    DeviceRole Role,
    string? Site,
    CriticalityTier Criticality,
    DeviceEnvironment Environment,
    DeviceState State,
    IReadOnlyList<string> Tags,
    DateTimeOffset UpdatedAt);
