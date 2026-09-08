using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Topology;

/// <summary>
/// One edge of the topology graph — the conclusion drawn from every observation that supports it.
/// </summary>
/// <remarks>
/// <para>
/// ARCHITECTURE.md §1: relationships are adjacency tables in PostgreSQL, not a graph store. This
/// is that table. It is one hop and only ever one hop — nothing here traverses, and multi-hop
/// traversal is on the SPEC.md §3 Defer list.
/// </para>
/// <para>
/// <strong>The endpoints are canonically ordered, not ordered by who asked.</strong> When both
/// ends are devices NetShield monitors and each has resolved the other's port, both devices'
/// walks compute the same pair and converge on one row — which is what makes the edge set of a
/// three-switch estate two edges rather than four. Where the far end is not a device, or where
/// its port could not be resolved, the observing device is <c>A</c> and no canonicalisation is
/// attempted: NetShield has not established that the two accounts are of one link, and inventing
/// that they are would be a claim rather than an observation.
/// </para>
/// <para>
/// <see cref="ObservedFromA"/> and <see cref="ObservedFromB"/> are what make
/// <see cref="AdjacencyConfidence.Confirmed"/> mean something. Each side's walk sets its own
/// flag and clears it when its own observations are withdrawn; the edge itself is withdrawn only
/// when both are clear, so one switch going quiet downgrades a link rather than deleting it.
/// </para>
/// </remarks>
internal sealed class DeviceAdjacency
{
    /// <summary>UUID v7.</summary>
    public Guid Id { get; init; }

    /// <summary>The lower-ordered endpoint's device.</summary>
    public Guid ADeviceId { get; init; }

    /// <summary>The interface on it.</summary>
    public int AIfIndex { get; init; }

    /// <summary>What that device calls the interface.</summary>
    public string? AInterfaceName { get; set; }

    /// <summary>The far endpoint's device, when the far end is one NetShield monitors.</summary>
    public Guid? BDeviceId { get; set; }

    /// <summary>The interface on it, when the far end's port id resolved to one.</summary>
    public int? BIfIndex { get; set; }

    /// <summary>What that device calls the interface.</summary>
    public string? BInterfaceName { get; set; }

    /// <summary>The far end's identifier as advertised. Never null.</summary>
    public string BChassisId { get; set; } = string.Empty;

    /// <summary>What kind of identifier that is.</summary>
    public NeighborIdKind BChassisIdKind { get; set; }

    /// <summary>The far end's port as advertised.</summary>
    public string? BPortId { get; set; }

    /// <summary>What the far end calls itself.</summary>
    public string? BSystemName { get; set; }

    /// <summary>
    /// The identity <c>B</c> is matched on, relative to <c>A</c>.
    /// </summary>
    /// <remarks>
    /// <c>device:{id}</c> where the far end resolved to a device — which is exactly what merges
    /// an LLDP account naming a chassis address and a CDP account naming a host name into one
    /// edge — and the raw remote identity otherwise.
    /// </remarks>
    public string AdjacencyKey { get; init; } = string.Empty;

    /// <summary>
    /// The protocols the <c>A</c> device reported this edge over, as enum member names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stored as <c>text[]</c> rather than as a bitmask, for the reason <c>devices.tags</c> is: a
    /// containment test in the database, and readable in <c>psql</c> without a lookup table.
    /// </para>
    /// <para>
    /// <strong>Split by side, and unioned only where the edge is read.</strong> Each device's
    /// walk knows what it itself saw and nothing about what the far end saw, so one shared list
    /// would be overwritten with half its contents every time either end was walked — a link
    /// where the core speaks LLDP and the access switch speaks both would flicker between two
    /// source sets for ever.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> SourcesA { get; set; } = [];

    /// <summary>The protocols the <c>B</c> device reported it over. Empty where B is not a device.</summary>
    public IReadOnlyList<string> SourcesB { get; set; } = [];

    /// <summary>How much the edge is worth. Computed by <see cref="AdjacencyRule"/>.</summary>
    public AdjacencyConfidence Confidence { get; set; }

    /// <summary>Whether the <c>A</c> device has itself reported this link.</summary>
    public bool ObservedFromA { get; set; }

    /// <summary>Whether the <c>B</c> device has.</summary>
    public bool ObservedFromB { get; set; }

    /// <summary>When the edge was first observed. UTC.</summary>
    public DateTimeOffset FirstDiscoveredAt { get; init; }

    /// <summary>When it was last observed, from either end. UTC.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>When both ends stopped reporting it, if they have. UTC.</summary>
    public DateTimeOffset? WithdrawnAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
