using System.ComponentModel.DataAnnotations;

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
}
