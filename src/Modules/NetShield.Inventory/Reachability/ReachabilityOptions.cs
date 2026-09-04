using System.ComponentModel.DataAnnotations;

namespace NetShield.Inventory.Reachability;

/// <summary>
/// How often NetShield asks whether a device is answering, what it asks, and how much agreement
/// it wants before it changes its mind (ARCHITECTURE.md §7: the API owns scheduling).
/// </summary>
/// <remarks>
/// <para>
/// The three probe values travel to the collector in the job's parameters rather than being
/// configured on the collector, for the reason the lease duration and poll interval do: a
/// collector holding its own copy of a number the API also holds is a collector that can drift
/// out of step with it, and every one of these is a decision about the estate rather than about
/// the process doing the probing.
/// </para>
/// <para>
/// The two thresholds are the hysteresis. They are what make "a flapping device does not emit a
/// transition per probe" a property of the system rather than an aspiration: an observation is
/// adopted only once it has been made this many times in a row, so a device alternating between
/// answering and not never accumulates enough of either to move at all.
/// </para>
/// </remarks>
public sealed class ReachabilityOptions
{
    /// <summary>The configuration section these are bound from.</summary>
    public const string SectionName = "Inventory:Reachability";

    /// <summary>
    /// Whether the schedule runs at all.
    /// </summary>
    /// <remarks>
    /// A switch rather than an absent registration, so that turning reachability off is one
    /// configuration value in an environment that has no collector — a laboratory host, or the
    /// schema step — instead of a different composition root.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>How many echo requests one probe sends.</summary>
    [Range(1, 20)]
    public int ProbeCount { get; set; } = 4;

    /// <summary>How long the probe waits for the last reply after the last request.</summary>
    [Range(0.1, 30)]
    public double ProbeTimeoutSeconds { get; set; } = 2;

    /// <summary>How long the probe waits between requests.</summary>
    [Range(0, 10)]
    public double ProbeIntervalSeconds { get; set; } = 0.25;

    /// <summary>
    /// How often each device is probed. <c>SPEC.md</c> §1 designs for 500 devices on a
    /// sixty-second interval, which is this value.
    /// </summary>
    [Range(10, 86400)]
    public int PollIntervalSeconds { get; set; } = 60;

    /// <summary>How often the scheduler looks for devices whose next probe has fallen due.</summary>
    /// <remarks>
    /// Shorter than <see cref="PollIntervalSeconds"/>, because it is the resolution at which a
    /// due device is noticed rather than a rate of its own — a device due at any instant waits at
    /// most this long to be queued.
    /// </remarks>
    [Range(1, 300)]
    public int ScanIntervalSeconds { get; set; } = 15;

    /// <summary>The most jobs one scan will queue.</summary>
    /// <remarks>
    /// A ceiling on the work a single pass can create, so that a first run over a freshly
    /// imported estate — or a run after an outage during which every device fell due — fills the
    /// queue in several passes rather than in one statement.
    /// </remarks>
    [Range(1, 5000)]
    public int MaxJobsPerScan { get; set; } = 500;

    /// <summary>
    /// How many consecutive unreachable observations it takes to call a device offline.
    /// </summary>
    [Range(1, 20)]
    public int FailureThreshold { get; set; } = 3;

    /// <summary>
    /// How many consecutive reachable observations it takes to call a device online, or degraded.
    /// </summary>
    [Range(1, 20)]
    public int SuccessThreshold { get; set; } = 2;
}
