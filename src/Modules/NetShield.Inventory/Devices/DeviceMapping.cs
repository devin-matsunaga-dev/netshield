using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Devices;

/// <summary>
/// Turns the entity into the shapes that leave the module. The one place the boundary in
/// ARCHITECTURE.md §4 is crossed, and the reason nothing else needs to see <see cref="Device"/>.
/// </summary>
internal static class DeviceMapping
{
    internal static DeviceDetail ToDetail(this Device device) =>
        new(
            device.Id,
            device.Hostname,
            device.PrimaryIpAddress.ToString(),
            device.Vendor,
            device.Model,
            device.OsVersion,
            device.SerialNumber,
            device.Site,
            device.Role,
            device.Criticality,
            device.Environment,
            device.Owner,
            device.Tags,
            device.Notes,
            device.State,
            device.CreatedAt,
            device.UpdatedAt);

    internal static DeviceSummary ToSummary(this Device device) =>
        new(
            device.Id,
            device.Hostname,
            device.PrimaryIpAddress.ToString(),
            device.Vendor,
            device.Model,
            device.Role,
            device.Site,
            device.Criticality,
            device.Environment,
            device.State,
            device.Tags,
            device.UpdatedAt);

    /// <summary>
    /// What an audit row records about a device. Every key is chosen so that
    /// <c>SecretRedactor</c> leaves it alone; a device carries no secret, and its credential
    /// profile is a separate aggregate that arrives in WP-1.2.
    /// </summary>
    internal static IReadOnlyDictionary<string, object?> ToAuditSnapshot(this Device device) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["hostname"] = device.Hostname,
            ["primaryIpAddress"] = device.PrimaryIpAddress.ToString(),
            ["vendor"] = device.Vendor.ToString(),
            ["model"] = device.Model,
            ["osVersion"] = device.OsVersion,
            ["serialNumber"] = device.SerialNumber,
            ["site"] = device.Site,
            ["role"] = device.Role.ToString(),
            ["criticality"] = device.Criticality.ToString(),
            ["environment"] = device.Environment.ToString(),
            ["owner"] = device.Owner,
            ["tags"] = string.Join(", ", device.Tags),
            ["notes"] = device.Notes,
            ["state"] = device.State.ToString()
        };
}
