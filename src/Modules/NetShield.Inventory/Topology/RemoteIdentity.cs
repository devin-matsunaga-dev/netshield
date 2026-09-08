using System.Globalization;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Clients;

namespace NetShield.Inventory.Topology;

/// <summary>
/// How a far end is named, so that two accounts of it can be recognised as one — or as two.
/// </summary>
/// <remarks>
/// <para>
/// Two keys, doing two different jobs, and confusing them is the mistake this type exists to
/// prevent.
/// </para>
/// <para>
/// <see cref="Raw"/> is the identity <em>as one protocol reported it</em>: a chassis address and
/// a port, a host name and a port, a gateway address. It is what makes an observation row unique
/// within its source and port, and it is deliberately not merged with anything — LLDP saying
/// "00:1c:73:00:00:01 port Et1" and CDP saying "core-sw-1 port Ethernet1" are two different
/// statements and both are recorded.
/// </para>
/// <para>
/// <see cref="Adjacency"/> is the identity <em>after resolution</em>: <c>device:{id}</c> once the
/// far end has been matched to a device NetShield monitors. That is what merges the two
/// statements above into one edge carrying both sources, and it is why the merge happens only
/// where NetShield actually knows the far end — two accounts of a stranger stay two edges,
/// because nothing has established they are the same stranger.
/// </para>
/// <para>
/// Both are normalised before they are built. A MAC arrives spelled six different ways depending
/// on the vendor and goes through the same normaliser WP-1.8 put in front of every client
/// address, so an endpoint spelled <c>aabb.ccdd.eeff</c> and <c>AA:BB:CC:DD:EE:FF</c> is one far
/// end rather than two each holding half of an edge's history.
/// </para>
/// </remarks>
internal static class RemoteIdentity
{
    /// <summary>The prefix a resolved far end's adjacency key carries.</summary>
    internal const string DevicePrefix = "device:";

    /// <summary>
    /// The identity of a far end as one protocol reported it.
    /// </summary>
    /// <remarks>
    /// The port is part of it because two things on one physical port is real — a phone with a
    /// workstation behind it is the ordinary case — and a key without the port would record one
    /// of them and silently withdraw the other on every walk.
    /// </remarks>
    internal static string Raw(NeighborIdKind kind, string chassisId, string? portId)
    {
        string identity = Normalize(kind, chassisId);
        string port = portId is null ? string.Empty : Fold(portId);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Kind(kind)}:{identity}|port:{port}");
    }

    /// <summary>
    /// The identity an edge is grouped by: the device, once one has been resolved.
    /// </summary>
    /// <remarks>
    /// The far end's own <c>ifIndex</c> is deliberately <em>not</em> part of this. LLDP and CDP
    /// spell the same far port differently — <c>Et1</c> against <c>Ethernet1</c> — so including
    /// it would defeat the merge the key exists to perform. Which cable is which stays
    /// distinguishable because the observing device's own interface is part of the uniqueness
    /// rule beside this key: two links between one pair of switches are two rows because they
    /// leave from two different local ports.
    /// </remarks>
    internal static string Adjacency(Guid? remoteDeviceId, string rawKey) =>
        remoteDeviceId is { } id ? $"{DevicePrefix}{id}" : rawKey;

    /// <summary>
    /// The identifier as it should be stored and compared.
    /// </summary>
    /// <remarks>
    /// A hardware address goes through the client normaliser, so a chassis id and the
    /// <c>device_interfaces.physical_address</c> it will be matched against are in one spelling.
    /// Everything else is trimmed and case-folded, because a host name's case is a typing
    /// accident and not a fact about the network.
    /// </remarks>
    internal static string Normalize(NeighborIdKind kind, string value)
    {
        if (kind == NeighborIdKind.MacAddress && MacAddress.Normalize(value) is { } mac)
        {
            return mac;
        }

        return Fold(value);
    }

    private static string Fold(string value) =>
        value.Trim().ToLowerInvariant();

    private static string Kind(NeighborIdKind kind) =>
        kind.ToString().ToLowerInvariant();
}
