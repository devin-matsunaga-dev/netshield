using NetShield.Platform.Results;

namespace NetShield.Inventory.Devices;

/// <summary>
/// Every refusal the device handlers can return, in one place, so the codes a client branches on
/// are visible together rather than spread across five files (CONVENTIONS.md §4).
/// </summary>
internal static class DeviceErrors
{
    /// <summary>The code a caller sees when the address is already taken.</summary>
    internal const string DuplicateAddressCode = "device.duplicate-primary-ip";

    /// <summary>The code a caller sees when the device is not there.</summary>
    internal const string NotFoundCode = "device.not-found";

    /// <summary>The code a caller sees when a sort field is not one this endpoint offers.</summary>
    internal const string UnknownSortCode = "device.unknown-sort";

    internal static Error NotFound(Guid id) =>
        Error.NotFound(NotFoundCode, $"No device with id {id}.");

    /// <summary>
    /// A live device already holds the address. The message names the address the caller sent and
    /// nothing about the device holding it — knowing that one exists is inventory read access.
    /// </summary>
    internal static Error DuplicateAddress(string address) =>
        Error.Conflict(DuplicateAddressCode, $"Another device is already at {address}.");

    internal static Error UnknownSort(string field, IEnumerable<string> permitted) =>
        Error.Validation(
            UnknownSortCode,
            $"Cannot sort by '{field}'.",
            new Dictionary<string, string[]>
            {
                ["sort"] = [$"Must be one of: {string.Join(", ", permitted)}."]
            });
}
