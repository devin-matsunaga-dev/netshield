using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Discovery;

/// <summary>
/// Turns what a walk recorded into the shapes that leave the module — the fingerprint row and
/// the interface rows beside it.
/// </summary>
/// <remarks>
/// Separate from <see cref="DiscoveryMapping"/>, which maps the seeds, runs and candidates a
/// sweep produces. These two are what a walk of one device produces, and they are read by the
/// device screen rather than by the discovery one.
/// </remarks>
internal static class FingerprintMapping
{
    internal static DeviceFingerprintDetail ToDetail(this DeviceFingerprint fingerprint) =>
        new(
            fingerprint.DeviceId,
            fingerprint.Vendor,
            fingerprint.ReducedCapability,
            fingerprint.SysObjectId,
            fingerprint.SysDescr,
            fingerprint.SysName,
            fingerprint.SysContact,
            fingerprint.SysLocation,
            fingerprint.UptimeSeconds,
            fingerprint.Model,
            fingerprint.OsVersion,
            fingerprint.SerialNumber,
            fingerprint.InterfaceCount,
            fingerprint.InterfacesTruncated,
            fingerprint.OverriddenFields,
            fingerprint.LastWalkAt,
            fingerprint.LastError,
            fingerprint.UpdatedAt);

    internal static DeviceInterfaceSummary ToSummary(this DeviceInterface row) =>
        new(
            row.Id,
            row.IfIndex,
            row.Name,
            row.Description,
            row.Alias,
            row.InterfaceType,
            row.Mtu,
            row.SpeedBitsPerSecond,
            row.PhysicalAddress,
            ToAdminStatus(row.AdminStatus),
            ToOperStatus(row.OperStatus),
            row.FirstSeenAt,
            row.LastSeenAt);

    /// <summary>
    /// Names an <c>ifAdminStatus</c>. The MIB defines three values and nothing else; anything
    /// else the device said is <see cref="InterfaceStatus.Unknown"/>, and the raw integer stays
    /// on the row for whoever is diagnosing why.
    /// </summary>
    private static InterfaceStatus ToAdminStatus(int? value) => value switch
    {
        1 => InterfaceStatus.Up,
        2 => InterfaceStatus.Down,
        3 => InterfaceStatus.Testing,
        _ => InterfaceStatus.Unknown
    };

    /// <summary>
    /// Names an <c>ifOperStatus</c>. Its <c>unknown(4)</c> and a value the MIB does not define
    /// both land on <see cref="InterfaceStatus.Unknown"/>: the device saying it does not know and
    /// the device saying something nobody can read are not distinctions a screen can act on
    /// differently.
    /// </summary>
    private static InterfaceStatus ToOperStatus(int? value) => value switch
    {
        1 => InterfaceStatus.Up,
        2 => InterfaceStatus.Down,
        3 => InterfaceStatus.Testing,
        5 => InterfaceStatus.Dormant,
        6 => InterfaceStatus.NotPresent,
        7 => InterfaceStatus.LowerLayerDown,
        _ => InterfaceStatus.Unknown
    };
}
