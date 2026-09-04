using System.ComponentModel.DataAnnotations;

namespace NetShield.Inventory.Collector;

/// <summary>
/// How the API paces the collector fleet (ARCHITECTURE.md §7: the API owns scheduling).
/// </summary>
/// <remarks>
/// Every one of these reaches the collector in a heartbeat acknowledgement rather than being
/// configured on the collector, so a deployment cannot end up with a collector whose idea of a
/// lease is longer than the server's — which is the configuration mistake that turns a visibility
/// timeout into two collectors doing one job.
/// </remarks>
public sealed class CollectorJobOptions
{
    /// <summary>The configuration section these are bound from.</summary>
    public const string SectionName = "Collector:Jobs";

    /// <summary>
    /// How long a lease lasts before the job becomes claimable again, in seconds.
    /// </summary>
    /// <remarks>
    /// It has to be longer than the slowest job of any kind, or a job that is merely slow gets
    /// run twice. Five minutes covers an SSH configuration fetch from a device that is thinking
    /// about it; a kind that needs longer will say so in the package that adds it.
    /// </remarks>
    [Range(30, 3600)]
    public int LeaseSeconds { get; set; } = 300;

    /// <summary>How often a collector should ask for work, in seconds.</summary>
    [Range(1, 300)]
    public int PollSeconds { get; set; } = 15;

    /// <summary>The most jobs one lease call will hand over.</summary>
    /// <remarks>
    /// A ceiling on how much work one collector can take out of the queue at once, so that a
    /// collector which then dies strands a bounded amount of it for one lease duration.
    /// </remarks>
    [Range(1, 200)]
    public int MaxJobsPerLease { get; set; } = 25;

    /// <summary>
    /// How many times a job is leased before it is abandoned as failed.
    /// </summary>
    /// <remarks>
    /// A job that has been claimed this many times without a result is not going to produce one:
    /// something about it wedges whoever picks it up, and re-queueing it for ever would let one
    /// bad job consume a slot in every batch from now on.
    /// </remarks>
    [Range(1, 20)]
    public int MaxAttempts { get; set; } = 3;
}
