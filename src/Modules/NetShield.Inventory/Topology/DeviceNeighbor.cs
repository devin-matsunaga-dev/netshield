using System.Net;

using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Topology;

/// <summary>
/// One protocol's account of what is on the other end of one interface — the raw observation an
/// adjacency is reconciled from.
/// </summary>
/// <remarks>
/// <para>
/// The observation-and-conclusion split WP-1.8 used for client bindings, applied to topology.
/// This table is what each device actually said; <see cref="DeviceAdjacency"/> is what NetShield
/// concluded from all of them. Keeping both means a conclusion can be re-derived, and an
/// operator asking "why does it think that" has an answer that is not a log line.
/// </para>
/// <para>
/// <strong>A row is withdrawn rather than deleted.</strong> Unlike a client binding, an
/// observation here <em>does</em> close on an absence: an LLDP table is the device's complete
/// current statement about what it can see and its agent expires its own entries, so a
/// successful, untruncated reading that no longer contains an entry is evidence the link has
/// gone. That is the WP-2.1 criterion "a removed link ages out". A truncated reading withdraws
/// nothing, because it saw part of a table.
/// </para>
/// </remarks>
internal sealed class DeviceNeighbor
{
    /// <summary>UUID v7.</summary>
    public Guid Id { get; init; }

    /// <summary>The device that reported this. Never null — somebody observed it.</summary>
    public Guid DeviceId { get; init; }

    /// <summary>Which protocol reported it.</summary>
    public NeighborSource Source { get; init; }

    /// <summary>The interface on the observing device, as an <c>ifIndex</c>.</summary>
    public int LocalIfIndex { get; init; }

    /// <summary>What the observing device calls that interface, where a walk recorded a name.</summary>
    public string? LocalInterfaceName { get; set; }

    /// <summary>
    /// The identity the far end advertised, normalised.
    /// </summary>
    /// <remarks>
    /// A chassis id for LLDP, a device id for CDP, the gateway's address for a routing hop. It is
    /// never null: an observation that names nothing could never be reconciled with another, and
    /// the reader drops such an entry before it reaches here.
    /// </remarks>
    public string RemoteChassisId { get; init; } = string.Empty;

    /// <summary>What kind of identifier that is.</summary>
    public NeighborIdKind RemoteChassisIdKind { get; init; }

    /// <summary>The far end's port, as it advertised it.</summary>
    public string? RemotePortId { get; set; }

    /// <summary>What kind of identifier the port is.</summary>
    public NeighborIdKind RemotePortIdKind { get; set; }

    /// <summary>The far end's description of that port, where it gave one.</summary>
    public string? RemotePortDescription { get; set; }

    /// <summary>What the far end calls itself.</summary>
    public string? RemoteSystemName { get; set; }

    /// <summary>What the far end says it is.</summary>
    public string? RemoteSystemDescription { get; set; }

    /// <summary>The platform CDP reports, where it does.</summary>
    public string? RemotePlatform { get; set; }

    /// <summary>The management address the far end advertised, where it did.</summary>
    public IPAddress? RemoteManagementAddress { get; set; }

    /// <summary>
    /// The device this far end resolved to, where NetShield monitors it.
    /// </summary>
    /// <remarks>
    /// Re-resolved on every walk rather than settled once: a neighbour that was a stranger last
    /// week may have been promoted from a discovery candidate since, and the edge should become
    /// a device-to-device one without anything having to notice.
    /// </remarks>
    public Guid? RemoteDeviceId { get; set; }

    /// <summary>The interface on the far device, where its port id resolved to one.</summary>
    public int? RemoteIfIndex { get; set; }

    /// <summary>What the far device calls that interface, where the inventory has a name for it.</summary>
    public string? RemoteInterfaceName { get; set; }

    /// <summary>
    /// The identity this observation is matched on, within its own source and port.
    /// </summary>
    /// <remarks>
    /// Built by <see cref="RemoteIdentity"/> and never null, which is what lets the open-row
    /// unique index be a plain one rather than needing <c>NULLS NOT DISTINCT</c> over a port that
    /// may be absent.
    /// </remarks>
    public string RemoteKey { get; init; } = string.Empty;

    /// <summary>
    /// The identity this observation is <em>grouped</em> by once the far end has been resolved.
    /// </summary>
    /// <remarks>
    /// <c>device:{id}</c> where the far end is a device NetShield monitors, and
    /// <see cref="RemoteKey"/> otherwise. It is what merges an LLDP account naming a chassis
    /// address and a CDP account naming a host name into one edge — see <see cref="RemoteIdentity"/>
    /// for why the two keys are not the same key.
    /// </remarks>
    public string AdjacencyKey { get; set; } = string.Empty;

    /// <summary>
    /// The edge this observation supports, where it supports one.
    /// </summary>
    /// <remarks>
    /// Null for an observation the reconciler declined to make an edge from — a CDP account of a
    /// port an LLDP account disagrees about. That is not an error and the row is not deleted: it
    /// is the evidence for a decision, and an operator asking why NetShield believes what it
    /// believes should be able to see the account it set aside.
    /// </remarks>
    public Guid? AdjacencyId { get; set; }

    /// <summary>The capability bits the far end advertised, where it did.</summary>
    public int? Capabilities { get; set; }

    /// <summary>
    /// How much evidence there was: the number of routes naming this gateway, for a routing
    /// observation. Null for a neighbour protocol, where an entry is an entry.
    /// </summary>
    public int? EvidenceCount { get; set; }

    /// <summary>When this protocol first reported it. UTC.</summary>
    public DateTimeOffset FirstDiscoveredAt { get; init; }

    /// <summary>When it last reported it. UTC.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>When a complete reading stopped containing it, if one has. UTC.</summary>
    public DateTimeOffset? WithdrawnAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
