using System.Net;

using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Topology;

/// <summary>
/// One protocol's account of a far end, normalised, before anything has been resolved or stored.
/// </summary>
/// <remarks>
/// <para>
/// The shape the two result handlers reduce their very different payloads to, so that everything
/// after them — resolution, reconciliation, the applier — is written once rather than twice. An
/// LLDP entry, a CDP cache entry and a routing next hop are three unrelated documents on the
/// wire and three statements of the same kind here: <em>on this interface, there is something
/// I can name</em>.
/// </para>
/// <para>
/// It is a value, not a row. <see cref="RemoteDeviceId"/> and <see cref="RemoteIfIndex"/> are
/// filled in by <see cref="TopologyResolver"/> after construction, because resolution is a
/// database question and this type asks none.
/// </para>
/// </remarks>
internal sealed record NeighborObservation
{
    /// <summary>Which protocol reported it.</summary>
    public required NeighborSource Source { get; init; }

    /// <summary>The interface on the observing device.</summary>
    public required int LocalIfIndex { get; init; }

    /// <summary>What the observing device calls that interface.</summary>
    public string? LocalInterfaceName { get; init; }

    /// <summary>The far end's identifier, as advertised. Never empty by construction.</summary>
    public required string RemoteChassisId { get; init; }

    /// <summary>What kind of identifier that is.</summary>
    public required NeighborIdKind RemoteChassisIdKind { get; init; }

    /// <summary>The far end's port, as advertised.</summary>
    public string? RemotePortId { get; init; }

    /// <summary>What kind of identifier the port is.</summary>
    public NeighborIdKind RemotePortIdKind { get; init; } = NeighborIdKind.Unknown;

    /// <summary>The far end's description of its port.</summary>
    public string? RemotePortDescription { get; init; }

    /// <summary>What the far end calls itself.</summary>
    public string? RemoteSystemName { get; init; }

    /// <summary>What the far end says it is.</summary>
    public string? RemoteSystemDescription { get; init; }

    /// <summary>The platform CDP reported.</summary>
    public string? RemotePlatform { get; init; }

    /// <summary>The management address it advertised.</summary>
    public IPAddress? RemoteManagementAddress { get; init; }

    /// <summary>The capability bits it advertised.</summary>
    public int? Capabilities { get; init; }

    /// <summary>How much evidence there was — the route count, for a routing observation.</summary>
    public int? EvidenceCount { get; init; }

    /// <summary>The device the far end resolved to. Filled in by the resolver.</summary>
    public Guid? RemoteDeviceId { get; init; }

    /// <summary>The interface on that device. Filled in by the resolver.</summary>
    public int? RemoteIfIndex { get; init; }

    /// <summary>What that device calls the interface. Filled in by the resolver.</summary>
    public string? RemoteInterfaceName { get; init; }

    /// <summary>The identity this observation is unique by, within its source and port.</summary>
    public string RemoteKey => RemoteIdentity.Raw(RemoteChassisIdKind, RemoteChassisId, RemotePortId);

    /// <summary>The identity the reconciler groups it by, once the far end has been resolved.</summary>
    public string AdjacencyKey => RemoteIdentity.Adjacency(RemoteDeviceId, RemoteKey);
}
