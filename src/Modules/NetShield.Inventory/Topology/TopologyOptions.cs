using System.ComponentModel.DataAnnotations;

namespace NetShield.Inventory.Topology;

/// <summary>
/// How often NetShield reads the estate's neighbour protocols and routing tables, how much of
/// each it will take, and how long an edge survives without being seen (ARCHITECTURE.md §7: the
/// API owns scheduling).
/// </summary>
/// <remarks>
/// <para>
/// The read values travel to the collector in the job's parameters rather than being configured
/// on the collector, for the reason every other job's do: a collector holding its own copy of a
/// number the API also holds is one that can drift out of step with it.
/// </para>
/// <para>
/// <strong>The two walks are configured separately because they are shaped differently.</strong>
/// A neighbour table is one entry per cable, so tens of rows on a big switch; a routing table can
/// be tens of thousands, and a route walk therefore gets a longer timeout, a far higher row
/// ceiling and a much lower report ceiling — read a great many, report the handful of distinct
/// gateways behind them. It also runs less often: what a device is cabled to changes when
/// somebody re-cables it, and what it routes through changes when somebody re-designs the
/// network.
/// </para>
/// </remarks>
public sealed class TopologyOptions
{
    /// <summary>The configuration section these are bound from.</summary>
    public const string SectionName = "Inventory:Topology";

    /// <summary>
    /// Whether the topology schedule runs at all.
    /// </summary>
    /// <remarks>
    /// A switch rather than an absent registration, for the reason
    /// <c>ClientOptions.Enabled</c> is one: turning topology collection off should be one
    /// configuration value rather than a different composition root.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>How long one SNMP request waits for an answer during a neighbour walk.</summary>
    [Range(0.1, 120)]
    public double RequestTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// How long one request waits during a route walk.
    /// </summary>
    /// <remarks>
    /// Longer than the neighbour walk's. A device assembling a GETBULK response over a large
    /// routing table takes measurably more time than one answering a handful of LLDP rows, and
    /// the two failing at the same threshold would mean the bigger read timed out first on every
    /// device where it mattered.
    /// </remarks>
    [Range(0.1, 120)]
    public double RouteRequestTimeoutSeconds { get; set; } = 15;

    /// <summary>How many times a request is repeated before it is given up on.</summary>
    [Range(0, 10)]
    public int Retries { get; set; } = 1;

    /// <summary>How many rows one GETBULK asks for.</summary>
    [Range(1, 100)]
    public int MaxRepetitions { get; set; } = 25;

    /// <summary>The most objects one neighbour subtree walk will read.</summary>
    [Range(1, 200_000)]
    public int MaxRowsPerSubtree { get; set; } = 20_000;

    /// <summary>The most entries one neighbour result will carry, per protocol.</summary>
    [Range(1, 50_000)]
    public int MaxNeighbors { get; set; } = 2_000;

    /// <summary>
    /// The most objects one routing-table walk will read.
    /// </summary>
    /// <remarks>
    /// High, because the whole table has to be seen for the reduction to be honest — a partial
    /// read would under-count how many routes name each gateway — and because
    /// <c>MaxNextHops</c> is what actually bounds the payload. It is still a ceiling: an agent
    /// carrying a full transit table is not a device SPEC.md §1 designs for, and the walk stops
    /// rather than letting one job consume the collector.
    /// </remarks>
    [Range(1, 500_000)]
    public int MaxRouteRows { get; set; } = 200_000;

    /// <summary>The most distinct gateways one route result will carry.</summary>
    [Range(1, 10_000)]
    public int MaxNextHops { get; set; } = 500;

    /// <summary>How often each device's neighbour protocols are read.</summary>
    [Range(60, 86_400)]
    public int NeighborWalkIntervalSeconds { get; set; } = 900;

    /// <summary>
    /// How often each device's routing table is read.
    /// </summary>
    /// <remarks>
    /// An hour by default, four times the neighbour interval. The read is much more expensive and
    /// the fact it establishes moves much more slowly.
    /// </remarks>
    [Range(60, 86_400)]
    public int RouteWalkIntervalSeconds { get; set; } = 3_600;

    /// <summary>How often the scheduler looks for devices whose next walk has fallen due.</summary>
    [Range(1, 3_600)]
    public int ScanIntervalSeconds { get; set; } = 60;

    /// <summary>The most walks one scan will queue, across both kinds.</summary>
    [Range(1, 5_000)]
    public int MaxJobsPerScan { get; set; } = 100;
}
