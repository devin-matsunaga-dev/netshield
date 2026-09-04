namespace NetShield.Contracts.Inventory;

/// <summary>
/// What a caller supplies to add a device by hand.
/// </summary>
/// <remarks>
/// There is no <c>state</c> member and there is deliberately no way to add one: reachability
/// state is something NetShield observes, not something a caller asserts (WP-1.4).
/// </remarks>
/// <param name="Hostname">The name it is known by. Required; not unique.</param>
/// <param name="PrimaryIpAddress">The address NetShield reaches it on. Required; unique.</param>
/// <param name="Vendor">The platform. Defaults to <see cref="DeviceVendor.Unknown"/>.</param>
/// <param name="Model">The hardware model.</param>
/// <param name="OsVersion">The running software version.</param>
/// <param name="SerialNumber">The chassis serial.</param>
/// <param name="Site">Where it is.</param>
/// <param name="Role">What the device is for.</param>
/// <param name="Criticality">How much its failure matters.</param>
/// <param name="Environment">Which environment it belongs to.</param>
/// <param name="Owner">Who is responsible for it.</param>
/// <param name="Tags">Free-form labels. Normalised on the way in.</param>
/// <param name="Notes">Anything worth recording.</param>
public sealed record CreateDeviceRequest(
    string Hostname,
    string PrimaryIpAddress,
    DeviceVendor Vendor = DeviceVendor.Unknown,
    string? Model = null,
    string? OsVersion = null,
    string? SerialNumber = null,
    string? Site = null,
    DeviceRole Role = DeviceRole.Other,
    CriticalityTier Criticality = CriticalityTier.Medium,
    DeviceEnvironment Environment = DeviceEnvironment.Production,
    string? Owner = null,
    IReadOnlyList<string>? Tags = null,
    string? Notes = null);
