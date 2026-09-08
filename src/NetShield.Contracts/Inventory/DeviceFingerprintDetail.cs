namespace NetShield.Contracts.Inventory;

/// <summary>
/// What the last SNMP walk established about a device, and which of those facts an operator has
/// since overruled.
/// </summary>
/// <remarks>
/// <para>
/// A sub-resource of the device rather than members on <see cref="DeviceDetail"/>, for the
/// reason the table behind it is a table of its own: the device is the inventory an operator
/// maintains, and this is what a machine observed. It is absent — 404 — until a walk has reached
/// the device at all.
/// </para>
/// <para>
/// <see cref="Vendor"/>, <see cref="Model"/>, <see cref="OsVersion"/> and
/// <see cref="SerialNumber"/> are the walk's own copies. Where one differs from the device's,
/// the field is named in <see cref="OverriddenFields"/> and the device's value is what an
/// operator set.
/// </para>
/// </remarks>
/// <param name="DeviceId">The device this is about.</param>
/// <param name="Vendor">The vendor the last walk resolved, whatever the device row now says.</param>
/// <param name="ReducedCapability">
/// Whether the last walk landed on the generic-SNMP fallback. SPEC.md §4 requires such a device's
/// reduced feature set to be clearly labelled, and this is the observation that label is drawn
/// from — not something inferred from the vendor name.
/// </param>
/// <param name="SysObjectId">The vendor's own identifier for the platform.</param>
/// <param name="SysDescr">Descriptive text, as the device wrote it.</param>
/// <param name="SysName">The device's own idea of its name, which need not be its hostname.</param>
/// <param name="SysContact">Whoever the device says to contact.</param>
/// <param name="SysLocation">Where the device says it is.</param>
/// <param name="UptimeSeconds">
/// <c>sysUpTime</c> at <see cref="LastWalkAt"/>. A 32-bit counter that wraps after about 497
/// days: this is what the agent said, not a boot time derived from it.
/// </param>
/// <param name="Model">The model the last walk discovered.</param>
/// <param name="OsVersion">The OS version the last walk discovered.</param>
/// <param name="SerialNumber">The chassis serial the last walk discovered.</param>
/// <param name="InterfaceCount">How many interfaces the last walk found.</param>
/// <param name="InterfacesTruncated">Whether it read fewer than the device has.</param>
/// <param name="OverriddenFields">
/// Which of <c>vendor</c>, <c>model</c>, <c>osVersion</c> and <c>serialNumber</c> the device now
/// disagrees with the walk about. Empty is the ordinary case.
/// </param>
/// <param name="LastWalkAt">When a walk last reached the device. UTC.</param>
/// <param name="LastError">
/// Why the last walk could not be performed, or nothing if the last one ran. The collector's
/// problem rather than the device's, and it never erases what a successful walk established.
/// </param>
/// <param name="UpdatedAt">When the row last changed. UTC.</param>
public sealed record DeviceFingerprintDetail(
    Guid DeviceId,
    DeviceVendor Vendor,
    bool ReducedCapability,
    string? SysObjectId,
    string? SysDescr,
    string? SysName,
    string? SysContact,
    string? SysLocation,
    double? UptimeSeconds,
    string? Model,
    string? OsVersion,
    string? SerialNumber,
    int InterfaceCount,
    bool InterfacesTruncated,
    IReadOnlyList<string> OverriddenFields,
    DateTimeOffset? LastWalkAt,
    string? LastError,
    DateTimeOffset UpdatedAt);
