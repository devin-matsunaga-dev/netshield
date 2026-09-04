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

    /// <summary>A discovery seed's name.</summary>
    internal const int SeedNameLength = 128;

    /// <summary>A seed's description, or the reason an address is ignored.</summary>
    internal const int ReasonLength = 512;

    /// <summary>
    /// A CIDR block as text. An IPv6 block written in full with a prefix fits comfortably.
    /// </summary>
    internal const int CidrLength = 64;

    /// <summary>The most ranges one seed may name.</summary>
    /// <remarks>
    /// A ceiling on how much work one edit can create, not a statement about the estate: every
    /// range is split into sweep jobs, and a seed naming a thousand of them would queue more
    /// work in one save than the run ceilings are meant to allow through in one pass.
    /// </remarks>
    internal const int MaxRangesPerSeed = 64;

    /// <summary>The most exclusions one seed may name.</summary>
    internal const int MaxExclusionsPerSeed = 256;
}
