using System.ComponentModel.DataAnnotations;

namespace NetShield.Inventory.Clients;

/// <summary>
/// How often NetShield reads the estate's ARP and forwarding tables, how much of each it will
/// take, and how long a resolution stays cached (ARCHITECTURE.md §7: the API owns scheduling).
/// </summary>
/// <remarks>
/// <para>
/// The read values travel to the collector in the job's parameters rather than being configured
/// on the collector, for the reason every other job's do: a collector holding its own copy of a
/// number the API also holds is one that can drift out of step with it.
/// </para>
/// <para>
/// The ceilings matter more here than anywhere else in Phase 1. A core router's neighbour cache
/// is tens of thousands of entries and a distribution switch's forwarding database is thousands,
/// where an interface inventory is hundreds — so a walk with no bound is one job that can carry
/// a payload larger than the row it is stored in.
/// </para>
/// </remarks>
public sealed class ClientOptions
{
    /// <summary>The configuration section these are bound from.</summary>
    public const string SectionName = "Inventory:Clients";

    /// <summary>
    /// Whether the client schedule runs at all.
    /// </summary>
    /// <remarks>
    /// A switch rather than an absent registration, for the reason
    /// <c>ReachabilityOptions.Enabled</c> is one: turning client tracking off should be one
    /// configuration value rather than a different composition root.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>How long one SNMP request waits for an answer.</summary>
    [Range(0.1, 120)]
    public double RequestTimeoutSeconds { get; set; } = 5;

    /// <summary>How many times a request is repeated before it is given up on.</summary>
    [Range(0, 10)]
    public int Retries { get; set; } = 1;

    /// <summary>How many rows one GETBULK asks for.</summary>
    [Range(1, 100)]
    public int MaxRepetitions { get; set; } = 25;

    /// <summary>The most objects one subtree walk will read, whatever the device offers.</summary>
    [Range(1, 200_000)]
    public int MaxRowsPerSubtree { get; set; } = 50_000;

    /// <summary>The most ARP entries one result will carry.</summary>
    [Range(1, 50_000)]
    public int MaxNeighbors { get; set; } = 10_000;

    /// <summary>The most forwarding-database entries one result will carry.</summary>
    [Range(1, 50_000)]
    public int MaxForwardingEntries { get; set; } = 10_000;

    /// <summary>
    /// How often each device's tables are read. Much longer than a reachability probe: a
    /// forwarding database is a fact about where things are plugged in, and a switch does not
    /// re-cable itself every minute.
    /// </summary>
    [Range(60, 86_400)]
    public int WalkIntervalSeconds { get; set; } = 900;

    /// <summary>How often the scheduler looks for devices whose next walk has fallen due.</summary>
    [Range(1, 3_600)]
    public int ScanIntervalSeconds { get; set; } = 60;

    /// <summary>The most walks one scan will queue.</summary>
    [Range(1, 5_000)]
    public int MaxJobsPerScan { get; set; } = 100;

    /// <summary>
    /// How long a cached address history stays valid without being invalidated.
    /// </summary>
    /// <remarks>
    /// Every write to a binding for an address drops that address's entry, and every device
    /// address change drops the addresses either side of it, so this is not how stale an answer
    /// can be in the ordinary case. It is the bound on how stale one can be when an invalidation
    /// was itself lost — a Redis that was unreachable for the one second the invalidation ran.
    /// A cache entry with no expiry outlives every invalidation anybody forgot to write.
    /// </remarks>
    [Range(5, 3_600)]
    public int ResolutionCacheSeconds { get; set; } = 300;
}
