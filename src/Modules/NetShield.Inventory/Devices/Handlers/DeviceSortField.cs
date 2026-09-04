namespace NetShield.Inventory.Devices.Handlers;

/// <summary>
/// The fields the device list can be ordered by.
/// </summary>
/// <remarks>
/// Deliberately short. Every member here needs an index to walk and a keyset comparison to page
/// by, so the list is what the table in WP-1.7 actually sorts on rather than every column it
/// displays.
/// </remarks>
internal enum DeviceSortField
{
    /// <summary>Newest or oldest first. The default, and the only total order that is stable.</summary>
    CreatedAt,

    /// <summary>Alphabetical by name.</summary>
    Hostname
}
