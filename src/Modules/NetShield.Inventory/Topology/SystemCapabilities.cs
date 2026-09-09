using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Topology;

/// <summary>
/// The capability map a neighbour advertised, as words.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One vocabulary, two tables.</strong> LLDP and CDP both answer "what are you", and they
/// answer it with different bits meaning different things. IEEE 802.1AB's
/// <c>lldpRemSysCapEnabled</c> is a <c>BITS</c> map — bridge is bit 2, router is bit 4 — while
/// CISCO-CDP-MIB's <c>cdpCacheCapabilities</c> is a 32-bit word in which router is bit 0 and
/// switch is bit 3. Reading either with the other's table names the wrong capabilities rather
/// than failing, so the source picks the table and there is no default.
/// </para>
/// <para>
/// The collector normalises the wire spelling before it gets here: whatever the octets looked
/// like, <c>collector.snmp.octets</c> delivers a mask whose bit <c>N</c> is that protocol's bit
/// <c>N</c>. This class knows what the bits mean and nothing about how they travelled.
/// </para>
/// <para>
/// A routing observation advertises no capabilities at all, and neither does a great deal of
/// older kit. That is an empty list rather than a guess: an endpoint that said nothing about
/// itself has said nothing about itself.
/// </para>
/// </remarks>
internal static class SystemCapabilities
{
    /// <summary>
    /// IEEE 802.1AB <c>LldpSystemCapabilitiesMap</c>, by bit.
    /// </summary>
    private static readonly SystemCapability?[] LldpBits =
    [
        SystemCapability.Other,
        SystemCapability.Repeater,
        SystemCapability.Bridge,
        SystemCapability.WlanAccessPoint,
        SystemCapability.Router,
        SystemCapability.Telephone,
        SystemCapability.DocsisCableDevice,
        SystemCapability.Station
    ];

    /// <summary>
    /// CISCO-CDP-MIB's capability word, by bit, mapped into the same vocabulary.
    /// </summary>
    /// <remarks>
    /// Two bits are deliberately dropped rather than translated. <c>igmpCapable</c> (bit 5) is a
    /// protocol feature rather than a kind of device, and saying it in a list of what something
    /// <em>is</em> would put a capability beside four identities. A source-route bridge (bit 2)
    /// is reported as a <see cref="SystemCapability.Bridge"/>, because the distinction it draws
    /// is about how frames are forwarded rather than about what the thing on the port is.
    /// </remarks>
    private static readonly SystemCapability?[] CdpBits =
    [
        SystemCapability.Router,
        SystemCapability.Bridge,
        SystemCapability.Bridge,
        SystemCapability.Bridge,
        SystemCapability.Station,
        null,
        SystemCapability.Repeater
    ];

    /// <summary>
    /// What <paramref name="capabilities"/> says, read with the table <paramref name="source"/>
    /// belongs to.
    /// </summary>
    /// <remarks>
    /// Ordered by the enum's own declaration and de-duplicated, so that two CDP bits mapping to
    /// one word produce one word and two protocols agreeing about a port produce one list.
    /// </remarks>
    internal static IReadOnlyList<SystemCapability> Decode(NeighborSource source, int? capabilities)
    {
        if (capabilities is not { } bits || bits == 0)
        {
            return [];
        }

        SystemCapability?[] table = source switch
        {
            NeighborSource.Lldp => LldpBits,
            NeighborSource.Cdp => CdpBits,

            // A next hop is a statement about layer 3 and says nothing about what the gateway is.
            _ => []
        };

        SortedSet<SystemCapability> found = [];

        for (int bit = 0; bit < table.Length; bit++)
        {
            if ((bits & (1 << bit)) != 0 && table[bit] is { } capability)
            {
                found.Add(capability);
            }
        }

        return [.. found];
    }

    /// <summary>
    /// Everything a set of observations of one edge said, merged.
    /// </summary>
    /// <remarks>
    /// One edge can be supported by an LLDP account and a CDP account of the same far end, and
    /// each carries its own map in its own encoding. Both are decoded by their own table and then
    /// unioned, because two protocols describing one access point are two witnesses rather than
    /// two access points.
    /// </remarks>
    internal static IReadOnlyList<SystemCapability> Merge(
        IEnumerable<(NeighborSource Source, int? Capabilities)> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);

        SortedSet<SystemCapability> found = [];

        foreach ((NeighborSource source, int? capabilities) in observations)
        {
            foreach (SystemCapability capability in Decode(source, capabilities))
            {
                found.Add(capability);
            }
        }

        return [.. found];
    }

    /// <summary>
    /// Endpoint capabilities: something advertising any of these is a thing on the port rather
    /// than a way through it.
    /// </summary>
    private static readonly SystemCapability[] Endpoints =
    [
        SystemCapability.WlanAccessPoint,
        SystemCapability.Telephone,
        SystemCapability.DocsisCableDevice,
        SystemCapability.Station
    ];

    /// <summary>Capabilities that mean traffic passes through rather than terminates.</summary>
    private static readonly SystemCapability[] Infrastructure =
    [
        SystemCapability.Bridge,
        SystemCapability.Router,
        SystemCapability.Repeater
    ];

    /// <summary>
    /// Whether what advertised itself is infrastructure rather than an endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A bridge or a router on a port means everything behind it reaches the network through that
    /// port, which is the whole of why an uplink must not list what it carries.
    /// </para>
    /// <para>
    /// <strong>An endpoint capability overrules a bridging one, and that is the part that is easy
    /// to get wrong.</strong> An IP phone advertises <c>Bridge</c> because it has a switch in it
    /// for the PC daisy-chained behind it, and an access point advertises <c>Bridge</c> because
    /// bridging is what it does — so a rule that read the bridge bit alone would classify every
    /// desk phone and every AP as an uplink and stop showing what is plugged into them, which is
    /// exactly the question the port view exists to answer. Bare infrastructure is infrastructure;
    /// a thing that says what it is, is that thing.
    /// </para>
    /// <para>
    /// The one case this leaves is a host that bridges and says nothing else about itself — a
    /// hypervisor advertising <c>Bridge</c> and <c>Station</c>. It is read as an endpoint here and
    /// then falls to <c>PortOccupancyRule</c>'s address count, which is the right order: twenty
    /// guests behind it make it an uplink on the evidence, and two do not.
    /// </para>
    /// </remarks>
    internal static bool IsInfrastructure(IReadOnlyList<SystemCapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        if (capabilities.Any(Endpoints.Contains))
        {
            return false;
        }

        return capabilities.Any(Infrastructure.Contains);
    }
}
