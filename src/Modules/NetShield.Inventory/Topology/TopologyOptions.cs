using System.ComponentModel.DataAnnotations;

namespace NetShield.Inventory.Topology;

/// <summary>
/// How often NetShield reads the estate's neighbour protocols, routing tables and VLAN tables,
/// how much of each it will take, and how long an edge survives without being seen
/// (ARCHITECTURE.md §7: the API owns scheduling).
/// </summary>
/// <remarks>
/// <para>
/// The read values travel to the collector in the job's parameters rather than being configured
/// on the collector, for the reason every other job's do: a collector holding its own copy of a
/// number the API also holds is one that can drift out of step with it.
/// </para>
/// <para>
/// <strong>The three walks are configured separately because they are shaped differently.</strong>
/// A neighbour table is one entry per cable, so tens of rows on a big switch; a routing table can
/// be tens of thousands, and a route walk therefore gets a longer timeout, a far higher row
/// ceiling and a much lower report ceiling — read a great many, report the handful of distinct
/// gateways behind them. It also runs less often: what a device is cabled to changes when
/// somebody re-cables it, and what it routes through changes when somebody re-designs the
/// network.
/// </para>
/// <para>
/// The VLAN read is bounded by the standard rather than by the estate — a bridge has at most
/// 4,094 VLANs and almost always has a few dozen — so it needs no unusual ceilings. What it does
/// need is the *longest* interval of the three: a VLAN is a design decision, and reading one every
/// fifteen minutes would be five hundred devices' worth of SNMP to confirm something that changes
/// a few times a year.
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

    /// <summary>
    /// How long one request waits during a VLAN walk.
    /// </summary>
    /// <remarks>
    /// Between the other two. A Q-BRIDGE table is small, but it is the table set an old or
    /// half-implemented agent is likeliest to be slow on, and the neighbour walk's threshold
    /// would fail those devices on every pass.
    /// </remarks>
    [Range(0.1, 120)]
    public double VlanRequestTimeoutSeconds { get; set; } = 10;

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

    /// <summary>
    /// The most VLANs one result will carry.
    /// </summary>
    /// <remarks>
    /// The ceiling is 4,094 because that is every VLAN IEEE 802.1Q admits, so a device cannot be
    /// truncated by a limit lower than the standard's own unless an operator sets one. The
    /// default is lower: a switch carrying more than five hundred VLANs is one an operator should
    /// hear about rather than one NetShield should quietly page through.
    /// </remarks>
    [Range(1, 4_094)]
    public int MaxVlans { get; set; } = 512;

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

    /// <summary>
    /// How often each device's VLAN tables are read.
    /// </summary>
    /// <remarks>
    /// An hour by default, matching the route walk and four times the neighbour interval. The
    /// fact it establishes moves the most slowly of the three, and the human set this default
    /// (WP-2.2).
    /// </remarks>
    [Range(60, 86_400)]
    public int VlanWalkIntervalSeconds { get; set; } = 3_600;

    /// <summary>How often the scheduler looks for devices whose next walk has fallen due.</summary>
    [Range(1, 3_600)]
    public int ScanIntervalSeconds { get; set; } = 60;

    /// <summary>The most walks one scan will queue, across all three kinds.</summary>
    [Range(1, 5_000)]
    public int MaxJobsPerScan { get; set; } = 100;

    /// <summary>
    /// How many learned MAC addresses make a port an uplink rather than an access port.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A default classification threshold, not a protocol truth.</strong> Nothing on the
    /// network says "this is an uplink"; a MAC address is learned by every bridge on the path to
    /// it, so the port facing the rest of the estate has learned every host beyond it while an
    /// access port has learned one or two. Eight is high enough that an IP phone with a PC
    /// daisy-chained behind it, or a small hypervisor with a handful of guests, is still read as
    /// an access port, and low enough to catch a real uplink long before the two-hundred-address
    /// case. It is configuration precisely because the right number depends on the estate.
    /// </para>
    /// <para>
    /// It is only ever consulted when nothing on the port said what it is:
    /// <c>PortOccupancyRule</c> takes a monitored device or a self-declared bridge or router
    /// first, so a threshold an operator sets badly cannot contradict something the network
    /// actually stated.
    /// </para>
    /// </remarks>
    [Range(2, 4_096)]
    public int UplinkAddressThreshold { get; set; } = 8;
}
