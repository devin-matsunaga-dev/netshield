using NetShield.Contracts.Inventory;

using NetShield.Platform.Paging;

namespace NetShield.Inventory.Devices.Handlers;

/// <summary>
/// The filters and sort a device list request carries, already parsed from the query string.
/// </summary>
/// <param name="Page">The validated cursor and limit.</param>
/// <param name="Sort">Which field orders the page.</param>
/// <param name="Descending">Whether that order runs backwards.</param>
/// <param name="State">Only devices in this state.</param>
/// <param name="Vendor">Only devices on this platform.</param>
/// <param name="Role">Only devices in this role.</param>
/// <param name="Criticality">Only devices at this tier.</param>
/// <param name="Environment">Only devices in this environment.</param>
/// <param name="Site">Only devices at this site. Matched exactly, ignoring case.</param>
/// <param name="Tag">Only devices carrying this tag. Normalised like a stored tag.</param>
/// <param name="Search">Hostname prefix, or an exact address when the text parses as one.</param>
internal sealed record DeviceListQuery(
    PageRequest Page,
    DeviceSortField Sort,
    bool Descending,
    DeviceState? State = null,
    DeviceVendor? Vendor = null,
    DeviceRole? Role = null,
    CriticalityTier? Criticality = null,
    DeviceEnvironment? Environment = null,
    string? Site = null,
    string? Tag = null,
    string? Search = null);
