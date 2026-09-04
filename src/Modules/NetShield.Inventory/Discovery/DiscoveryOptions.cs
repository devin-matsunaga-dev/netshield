using System.ComponentModel.DataAnnotations;

using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Discovery;

/// <summary>
/// How NetShield walks a device: what it asks for, how long it waits, and how much of a very
/// large device it is willing to read (ARCHITECTURE.md §7: the API owns scheduling).
/// </summary>
/// <remarks>
/// <para>
/// Every value here travels to the collector in the job's parameters rather than being
/// configured on the collector, for the reason the reachability probe's do: a collector holding
/// its own copy of a number the API also holds can drift out of step with it, and each of these
/// is a decision about the estate rather than about the process doing the reading.
/// </para>
/// <para>
/// The two ceilings are about a device that is much bigger than expected. A chassis with a
/// thousand ports would otherwise produce one result document large enough to matter, and a
/// misbehaving agent that never stops answering would produce an unbounded one.
/// </para>
/// <para>
/// How many times a walk is retried is not here. <c>CollectorJobOptions.MaxAttempts</c> governs
/// every queued job, and giving one kind its own count would mean widening the queue's own
/// contract — which WP-1.5 has no instruction to do.
/// </para>
/// </remarks>
public sealed class DiscoveryOptions
{
    /// <summary>The configuration section these are bound from.</summary>
    public const string SectionName = "Inventory:Discovery";

    /// <summary>How long one SNMP request waits for an answer.</summary>
    [Range(0.1, 120)]
    public double RequestTimeoutSeconds { get; set; } = 5;

    /// <summary>How many times a request is repeated before it is given up on.</summary>
    [Range(0, 10)]
    public int Retries { get; set; } = 2;

    /// <summary>How many rows one GETBULK asks for.</summary>
    [Range(1, 100)]
    public int MaxRepetitions { get; set; } = 25;

    /// <summary>The most objects one subtree walk will read.</summary>
    [Range(1, 100_000)]
    public int MaxRowsPerSubtree { get; set; } = 20_000;

    /// <summary>
    /// The most interfaces one walk will report.
    /// </summary>
    /// <remarks>
    /// Tied to <c>CollectorLimits.ResultLength</c>, which is the largest payload the API will
    /// store for one job: a result that exceeded it would be refused at submission and the walk
    /// wasted. Five hundred interfaces is a little over a hundred kilobytes of JSON, which
    /// leaves the ceiling a comfortable margin. Raising this means checking that one.
    /// </remarks>
    [Range(1, 10_000)]
    public int MaxInterfaces { get; set; } = 512;

    /// <summary>
    /// Which kind of SNMP credential a device is walked with, best first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the "credential profile order" WP-1.6's entry names. WP-1.5 chose a device's SNMP
    /// credential by a rule written into the handler — SNMPv3, then SNMPv2c — and recorded that
    /// WP-1.6 owned making it configurable. This is that, without moving the choice anywhere a
    /// caller can reach: an installation that has retired v2c can say so here, and one that has
    /// not keeps the default.
    /// </para>
    /// <para>
    /// It is deliberately an order over <em>kinds</em> rather than a list of profile ids. A list
    /// of ids would let whoever edits it decide which credential a collector is handed, and
    /// WP-1.2 put that decision behind <c>CredentialsManage</c>; a kind order says how to choose
    /// among the profiles a device has already been assigned, which is scheduling policy.
    /// </para>
    /// <para>
    /// It starts empty rather than at <see cref="DefaultCredentialKindOrder"/>, and
    /// <see cref="ResolvedCredentialKindOrder"/> is what anything reads. The configuration binder
    /// <em>adds</em> to a collection property that already holds something rather than replacing
    /// it, so a property carrying its own default cannot be overridden — only appended to, which
    /// would leave the default's first entry winning whatever an operator wrote.
    /// </para>
    /// </remarks>
    public IList<CredentialKind> CredentialKindOrder { get; } = [];

    /// <summary>The order used when the configuration names none.</summary>
    /// <remarks>
    /// The WP-1.5 rule exactly: SNMPv3 before SNMPv2c, because v3 is the one that authenticates
    /// and encrypts. It has to stay the default, or an installation that never set this would
    /// silently change which credential its walks use.
    /// </remarks>
    public static IReadOnlyList<CredentialKind> DefaultCredentialKindOrder { get; } =
        [CredentialKind.SnmpV3, CredentialKind.SnmpV2c];

    /// <summary>The order actually in force, which is the default when none is configured.</summary>
    public IReadOnlyList<CredentialKind> ResolvedCredentialKindOrder =>
        CredentialKindOrder.Count == 0 ? DefaultCredentialKindOrder : [.. CredentialKindOrder];

    /// <summary>
    /// Whether the discovery schedule runs at all.
    /// </summary>
    /// <remarks>
    /// A switch rather than an absent registration, for the reason
    /// <c>ReachabilityOptions.Enabled</c> is one. It stops the schedule; it does not stop a
    /// person asking for a run, which is a deliberate act with a permission of its own.
    /// </remarks>
    public bool ScheduleEnabled { get; set; } = true;

    /// <summary>How often the schedule looks for seeds whose next run has fallen due.</summary>
    [Range(1, 3600)]
    public int ScanIntervalSeconds { get; set; } = 60;

    /// <summary>The most runs one scan will start.</summary>
    /// <remarks>
    /// The ceiling the reachability schedule has as <c>MaxJobsPerScan</c>, at the grain a
    /// discovery schedule works at: a run is already a fan-out of many jobs, so the thing worth
    /// bounding per pass is how many of them can begin at once.
    /// </remarks>
    [Range(1, 100)]
    public int MaxRunsPerScan { get; set; } = 2;

    /// <summary>The most addresses one sweep job probes.</summary>
    /// <remarks>
    /// What makes a job a bounded piece of work: a /24 is one job, and a /16 is 256 of them
    /// spread across whatever collectors are asking. Raising it lengthens the time one lease is
    /// held, which has to stay comfortably inside <c>CollectorJobOptions.LeaseSeconds</c>.
    /// </remarks>
    [Range(1, 4096)]
    public int MaxAddressesPerJob { get; set; } = 256;

    /// <summary>The most addresses one run will sweep, and so the most one seed may name.</summary>
    /// <remarks>
    /// A seed whose ranges exceed this is refused at the endpoint rather than truncated at run
    /// time, so that an operator who typed a /8 by mistake finds out when they save it.
    /// </remarks>
    [Range(1, 16_777_216)]
    public long MaxAddressesPerRun { get; set; } = 65_536;

    /// <summary>The most sweep jobs one run will queue.</summary>
    /// <remarks>
    /// The second half of the same ceiling, at the grain the collector queue feels: it is what
    /// stops one run from filling the queue with work no collector will reach for hours.
    /// </remarks>
    [Range(1, 10_000)]
    public int MaxJobsPerRun { get; set; } = 512;

    /// <summary>How many echo requests one address gets before it is called silent.</summary>
    /// <remarks>
    /// One, by default, and deliberately lower than a reachability probe's four. A sweep is
    /// asking whether anything is there at all and a false negative is corrected by the next
    /// run, whereas a reachability probe decides whether a known device is down.
    /// </remarks>
    [Range(1, 10)]
    public int SweepProbeCount { get; set; } = 1;

    /// <summary>How long one address's replies are waited for.</summary>
    [Range(0.1, 30)]
    public double SweepTimeoutSeconds { get; set; } = 1;

    /// <summary>How long to wait between one address's requests.</summary>
    [Range(0, 10)]
    public double SweepIntervalSeconds { get; set; }

    /// <summary>How many addresses are probed at once within one job.</summary>
    /// <remarks>
    /// What decides whether "a run over a /24 completes within the configured window" holds: 256
    /// addresses at a one-second timeout take four seconds at this concurrency and four minutes
    /// at one.
    /// </remarks>
    [Range(1, 512)]
    public int SweepConcurrency { get; set; } = 64;

    /// <summary>
    /// The most responders one job will report.
    /// </summary>
    /// <remarks>
    /// Tied to <c>CollectorLimits.ResultLength</c>, the largest payload the API will store for
    /// one job, exactly as <see cref="MaxInterfaces"/> is: a result that exceeded it would be
    /// refused at submission and the sweep wasted. It can never bite below
    /// <see cref="MaxAddressesPerJob"/>, and the result says when it did.
    /// </remarks>
    [Range(1, 4096)]
    public int MaxRespondersPerJob { get; set; } = 1024;
}
