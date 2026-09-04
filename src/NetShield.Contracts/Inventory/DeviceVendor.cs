namespace NetShield.Contracts.Inventory;

/// <summary>
/// The platforms NetShield knows how to talk to (SPEC.md §4). Anything else is
/// <see cref="GenericSnmp"/> with a reduced feature set.
/// </summary>
/// <remarks>
/// Serialised as its name rather than its ordinal, so that inserting a member cannot renumber
/// what a stored row, a generated client or a saved fixture already means.
/// </remarks>
public enum DeviceVendor
{
    /// <summary>
    /// Nobody has identified this device yet. Distinct from <see cref="GenericSnmp"/>: that is a
    /// device fingerprinting has examined and found no CLI features for, this is one it has not
    /// reached. A manually added device starts here until WP-1.5 walks it.
    /// </summary>
    Unknown,

    /// <summary>Cisco IOS and IOS-XE.</summary>
    CiscoIos,

    /// <summary>Cisco NX-OS.</summary>
    CiscoNxOs,

    /// <summary>Juniper JunOS.</summary>
    JuniperJunOs,

    /// <summary>Arista EOS.</summary>
    AristaEos,

    /// <summary>Fortinet FortiOS.</summary>
    FortinetFortiOs,

    /// <summary>MikroTik RouterOS.</summary>
    MikroTikRouterOs,

    /// <summary>Identified, but with no CLI support — SNMP reads only.</summary>
    GenericSnmp
}
