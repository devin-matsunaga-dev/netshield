using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Topology;

/// <summary>
/// The two decisions WP-2.1 turns on, as pure functions over values.
/// </summary>
/// <remarks>
/// <para>
/// <em>"Conflicting LLDP/CDP data resolves deterministically"</em> and <em>"edge confidence when
/// sources disagree"</em> are the criteria this package is measured against, and everything
/// around them — the tables, the applier, the schedule, the endpoints — is machinery that can be
/// exercised end to end. The decisions themselves are arithmetic, and they live here so they can
/// be tested as arithmetic. It is the shape WP-1.8 gave <c>AssetResolutionRule</c>, for the same
/// reason.
/// </para>
/// <para>
/// Nothing here reads a database, a clock or a configuration value. Given the same observations
/// it returns the same answer on any machine, in any order they arrive.
/// </para>
/// </remarks>
internal static class AdjacencyRule
{
    /// <summary>
    /// One protocol's account of a far end, reduced to what the decision actually depends on.
    /// </summary>
    /// <param name="Source">Which protocol said it.</param>
    /// <param name="AdjacencyKey">The far end it resolved to, after device resolution.</param>
    /// <param name="Resolved">Whether that far end is a device NetShield monitors.</param>
    internal readonly record struct Candidate(
        NeighborSource Source,
        string AdjacencyKey,
        bool Resolved);

    /// <summary>
    /// Which of the accounts of one local port survive to become edges.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>LLDP outranks CDP on a port where the two disagree.</strong> An LLDP chassis
    /// identifier is an identity; a CDP device id is usually a host name, and WP-1.1 settled that
    /// a host name is not one — DHCP naming, cloned systems, split DNS and reused defaults all
    /// produce duplicates. So where a port has an LLDP account, an account of that same port
    /// supported by CDP alone is not made into an edge.
    /// </para>
    /// <para>
    /// It is narrow on purpose. Only a group whose <em>only</em> support is CDP is dropped: a
    /// group both protocols agree on has already merged into one key and is kept, and a group
    /// carrying routing evidence is kept because layer 3 is answering a different question and is
    /// not in the argument. The cost is that a second neighbour on a hub which speaks CDP and not
    /// LLDP is suppressed where an LLDP neighbour shares the port — recorded as a known trade,
    /// and its observation is still on <c>device_neighbors</c> either way, so nothing is lost from
    /// the record.
    /// </para>
    /// <para>
    /// Returns the surviving adjacency keys. Deterministic in the mathematical sense: the answer
    /// depends on the <em>set</em> of candidates and not on the order they were read in.
    /// </para>
    /// </remarks>
    internal static IReadOnlySet<string> Survivors(IReadOnlyCollection<Candidate> onOnePort)
    {
        ArgumentNullException.ThrowIfNull(onOnePort);

        Dictionary<string, HashSet<NeighborSource>> grouped = [];

        foreach (Candidate candidate in onOnePort)
        {
            if (!grouped.TryGetValue(candidate.AdjacencyKey, out HashSet<NeighborSource>? sources))
            {
                sources = [];
                grouped[candidate.AdjacencyKey] = sources;
            }

            sources.Add(candidate.Source);
        }

        bool lldpOnThisPort = grouped.Values.Any(sources => sources.Contains(NeighborSource.Lldp));

        if (!lldpOnThisPort)
        {
            return grouped.Keys.ToHashSet(StringComparer.Ordinal);
        }

        return grouped
            .Where(group => !IsCdpOnly(group.Value))
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// How much an edge is worth, from what supports it and whether both ends said so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A three-rung ladder, and every rung is a fact rather than a judgement:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <see cref="AdjacencyConfidence.Confirmed"/> — a neighbour protocol, from both ends. Two
    /// independent devices agreeing about one cable is the strongest thing an SNMP read can
    /// establish about topology, and it is the only rung that rules out a stale entry on one
    /// side.
    /// </item>
    /// <item>
    /// <see cref="AdjacencyConfidence.Probable"/> — a neighbour protocol from one end, or routing
    /// from both. One end is the ordinary case for anything that is not a monitored device, and
    /// two routers each routing through the other is a real mutual observation that is
    /// nonetheless about layer 3.
    /// </item>
    /// <item>
    /// <see cref="AdjacencyConfidence.Possible"/> — a routing next hop, from one end. It says the
    /// two are one hop apart at layer 3, which need not be one cable: a gateway reached through a
    /// switch NetShield also monitors is still one hop away.
    /// </item>
    /// </list>
    /// </remarks>
    internal static AdjacencyConfidence Confidence(
        IReadOnlyCollection<NeighborSource> sources,
        bool bidirectional)
    {
        ArgumentNullException.ThrowIfNull(sources);

        bool neighborProtocol =
            sources.Contains(NeighborSource.Lldp) || sources.Contains(NeighborSource.Cdp);

        return (neighborProtocol, bidirectional) switch
        {
            (true, true) => AdjacencyConfidence.Confirmed,
            (true, false) => AdjacencyConfidence.Probable,
            (false, true) => AdjacencyConfidence.Probable,
            (false, false) => AdjacencyConfidence.Possible
        };
    }

    /// <summary>
    /// The two endpoints of an edge in canonical order, so both ends' walks land on one row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ordered by device id, then by interface index. Both devices compute the same pair and so
    /// write the same row, which is what makes a link between two monitored switches one edge
    /// rather than two.
    /// </para>
    /// <para>
    /// It applies only where the far end is a device <em>and</em> its interface has been
    /// resolved. Where either is missing, the observing device stays the <c>A</c> end: NetShield
    /// has not established that its account and the far device's are of the same link, and
    /// asserting they are on the strength of a device id alone would collapse a two-cable
    /// aggregate into one edge.
    /// </para>
    /// </remarks>
    internal static bool ShouldSwap(
        Guid localDeviceId,
        int localIfIndex,
        Guid? remoteDeviceId,
        int? remoteIfIndex)
    {
        if (remoteDeviceId is not { } remote || remoteIfIndex is not { } remotePort)
        {
            return false;
        }

        int byDevice = remote.CompareTo(localDeviceId);

        return byDevice < 0 || (byDevice == 0 && remotePort < localIfIndex);
    }

    private static bool IsCdpOnly(HashSet<NeighborSource> sources) =>
        sources.Count == 1 && sources.Contains(NeighborSource.Cdp);
}
