using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Topology;

/// <summary>
/// What a port is, decided from what was observed on it. One pure function, and the only place
/// the access-versus-uplink question is answered.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="AdjacencyRule"/>, <see cref="VlanAggregationRule"/> and
/// <see cref="GraphLayoutRule"/> shape: the decision the package is measured against is
/// arithmetic over values, provable without a database. "An uplink says how many addresses it
/// carries rather than listing them as occupants" is the WP-2.6 criterion, and it is decided
/// here.
/// </para>
/// <para>
/// <strong>The order the reasons are considered in is the whole rule.</strong> A far end
/// NetShield monitors is the strongest reading there is and nothing overturns it. An unmanaged
/// neighbour calling itself a bridge or a router is next, because that is the endpoint's own
/// statement about itself. Only then does the address count get a say — it is a threshold
/// somebody chose, and it must not be able to contradict something the network actually said.
/// </para>
/// <para>
/// <strong>Why a count discriminates at all.</strong> A MAC address is learned by every bridge on
/// the path to it, so the port facing the rest of the network has learned every host beyond it
/// while an access port has learned one or two. That is the same evidence WP-1.8 recorded on the
/// binding and never used.
/// </para>
/// </remarks>
internal static class PortOccupancyRule
{
    /// <summary>What a port turned out to be.</summary>
    /// <param name="Role">Access, uplink or empty.</param>
    /// <param name="Reason">What decided it.</param>
    /// <param name="ClientsListed">
    /// Whether the port's clients are its occupants. False on an uplink, where they are hosts
    /// behind it rather than on it.
    /// </param>
    internal readonly record struct PortClassification(
        PortRole Role,
        PortRoleReason Reason,
        bool ClientsListed);

    /// <summary>One neighbour, reduced to the two things the classification depends on.</summary>
    /// <param name="Managed">Whether the far end is a device NetShield monitors.</param>
    /// <param name="Capabilities">What the far end advertised itself as.</param>
    internal readonly record struct NeighborFacts(
        bool Managed,
        IReadOnlyList<SystemCapability> Capabilities);

    /// <summary>
    /// Classifies one port.
    /// </summary>
    /// <param name="neighbors">Everything that announced itself on the port.</param>
    /// <param name="learnedAddressCount">
    /// How many addresses the switch itself said the port had learned, where it said. Null on an
    /// agent that answers only the VLAN-unaware forwarding database.
    /// </param>
    /// <param name="clientCount">How many open client bindings NetShield holds on the port.</param>
    /// <param name="uplinkAddressThreshold">
    /// The count at or above which a port is read as an uplink. Configuration, not protocol.
    /// </param>
    internal static PortClassification Classify(
        IReadOnlyList<NeighborFacts> neighbors,
        int? learnedAddressCount,
        int clientCount,
        int uplinkAddressThreshold)
    {
        ArgumentNullException.ThrowIfNull(neighbors);

        // The far end is a device NetShield monitors. It is not that a lot of addresses were
        // learned here — it is the topology, established from both ends where both were walked,
        // and no count is going to improve on it.
        if (neighbors.Any(neighbor => neighbor.Managed))
        {
            return new PortClassification(PortRole.Uplink, PortRoleReason.ManagedDevice, false);
        }

        // An unmanaged switch or router said what it is. NetShield does not monitor it, so there
        // is no edge to another node — but everything behind it still reaches the network through
        // this port, which is the only thing the classification cares about.
        if (neighbors.Any(neighbor => SystemCapabilities.IsInfrastructure(neighbor.Capabilities)))
        {
            return new PortClassification(
                PortRole.Uplink,
                PortRoleReason.InfrastructureNeighbor,
                false);
        }

        // Nothing said what it was, so the evidence is how much of it there is. `clientCount` is
        // the fallback for an agent that gave no count of its own: NetShield's own bindings
        // under-report — they are the addresses it has resolved to clients rather than every
        // address the port learned — so a port that reaches the threshold on that reading has
        // certainly reached it on the device's.
        int addresses = learnedAddressCount ?? clientCount;

        if (addresses >= uplinkAddressThreshold)
        {
            return new PortClassification(
                PortRole.Uplink,
                PortRoleReason.LearnedAddressCount,
                false);
        }

        if (neighbors.Count > 0 || clientCount > 0)
        {
            return new PortClassification(PortRole.Access, PortRoleReason.Endpoints, true);
        }

        // An empty port still lists its clients, of which there are none. The distinction between
        // "nothing here" and "not listed" belongs to a port that has something to withhold.
        return new PortClassification(PortRole.Empty, PortRoleReason.NoEvidence, true);
    }
}
