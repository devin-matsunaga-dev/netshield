namespace NetShield.Inventory.Discovery;

/// <summary>
/// The lengths the fingerprint columns agree on.
/// </summary>
/// <remarks>
/// One place, for the reason <c>DeviceLimits</c>, <c>CredentialLimits</c> and
/// <c>CollectorLimits</c> exist. Nothing here is validated at an endpoint — every value arrives
/// from a device rather than from a caller — so these are the ceilings the applier truncates to
/// rather than refuses at. A device that answers with something absurdly long is a device with a
/// strange agent, not a reason to lose the rest of the walk.
/// </remarks>
internal static class DiscoveryLimits
{
    /// <summary>A dotted OID. ``sysObjectID`` is short; the ceiling is generous rather than tight.</summary>
    internal const int ObjectIdLength = 255;

    /// <summary>``sysDescr``. Cisco's runs to several hundred characters on a good day.</summary>
    internal const int DescriptionLength = 1024;

    /// <summary>``sysName``, ``sysContact``, ``sysLocation``, and the three vendor facts.</summary>
    internal const int NameLength = 255;

    /// <summary>An interface's name, description or alias.</summary>
    internal const int InterfaceTextLength = 255;

    /// <summary>A physical address as colon-separated hex. Longer than any MAC, for InfiniBand.</summary>
    internal const int PhysicalAddressLength = 64;
}
