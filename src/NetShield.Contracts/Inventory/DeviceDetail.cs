namespace NetShield.Contracts.Inventory;

/// <summary>
/// Everything the API will say about one device.
/// </summary>
/// <remarks>
/// <see cref="Hostname"/> is not unique and is not an identifier. Duplicates occur legitimately —
/// DHCP naming, reused defaults, split DNS, cloned systems — and discovery has to be able to
/// record the estate as it actually is. <see cref="PrimaryIpAddress"/> carries the uniqueness
/// guarantee; <see cref="Id"/> is the identity.
/// </remarks>
/// <param name="Id">The device.</param>
/// <param name="Hostname">The name it is known by.</param>
/// <param name="PrimaryIpAddress">The address NetShield reaches it on. Unique among live devices.</param>
/// <param name="Vendor">The platform, once something has identified it.</param>
/// <param name="Model">The hardware model, when known.</param>
/// <param name="OsVersion">The running software version, when known.</param>
/// <param name="SerialNumber">The chassis serial, when known.</param>
/// <param name="Site">Where it is, as free text.</param>
/// <param name="Role">What the device is for.</param>
/// <param name="Criticality">How much its failure matters.</param>
/// <param name="Environment">Which environment it belongs to.</param>
/// <param name="Owner">Who is responsible for it, as free text.</param>
/// <param name="Tags">Free-form labels, lower-cased and sorted.</param>
/// <param name="Notes">Anything an operator wanted to record.</param>
/// <param name="State">Whether it is answering. Never set over the API.</param>
/// <param name="CreatedAt">When the device was added. UTC.</param>
/// <param name="UpdatedAt">When the row last changed. UTC.</param>
public sealed record DeviceDetail(
    Guid Id,
    string Hostname,
    string PrimaryIpAddress,
    DeviceVendor Vendor,
    string? Model,
    string? OsVersion,
    string? SerialNumber,
    string? Site,
    DeviceRole Role,
    CriticalityTier Criticality,
    DeviceEnvironment Environment,
    string? Owner,
    IReadOnlyList<string> Tags,
    string? Notes,
    DeviceState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
