using System.Text.Json.Serialization;

namespace NetShield.Contracts.Inventory;

/// <summary>
/// How much NetShield trusts one edge, from what supports it.
/// </summary>
/// <remarks>
/// <para>
/// WP-2.1's entry asks for "edge confidence when sources disagree". Two things decide it, and
/// both are facts rather than judgements: whether a neighbour protocol saw the edge at all, and
/// whether <em>both</em> ends of it reported the same link. It is computed by one pure function,
/// <c>AdjacencyRule.Confidence</c>, so the decision can be tested as arithmetic rather than
/// through the machinery around it.
/// </para>
/// <para>
/// Serialised as its name rather than its ordinal (WP-0.4).
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<AdjacencyConfidence>))]
public enum AdjacencyConfidence
{
    /// <summary>
    /// Both ends reported it, over a neighbour protocol. Two independent devices agreeing about
    /// one cable is the strongest thing an SNMP read can establish about topology.
    /// </summary>
    Confirmed,

    /// <summary>
    /// One end reported it over a neighbour protocol, or both ends over routing alone. The far
    /// end has not confirmed — most often because it is not a device NetShield monitors, which is
    /// the ordinary case for a server, a phone or an unmanaged switch.
    /// </summary>
    Probable,

    /// <summary>
    /// A routing next hop, from one end, and nothing else. It says the two are one hop apart at
    /// layer 3 and does not say they are cabled together.
    /// </summary>
    Possible
}
