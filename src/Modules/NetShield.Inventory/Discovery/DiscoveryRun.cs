using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Discovery;

/// <summary>
/// One sweep of one seed: what it set out to probe, and what came of it.
/// </summary>
/// <remarks>
/// <para>
/// A run is a fan-out over <c>collector_jobs</c>. It is created with a row in
/// <c>discovery_run_jobs</c> for every sweep job it queued, and it is finished when every one of
/// those rows has been applied — which is what makes "the run is still going" a fact about rows
/// rather than a guess about time.
/// </para>
/// <para>
/// It keeps its own copy of the ranges and exclusions it swept rather than pointing at the
/// seed's. A seed is editable; a run that reported 254 addresses has to keep saying which 254
/// they were after somebody widens it, or the history stops describing what happened.
/// </para>
/// <para>
/// Every counter here is a count of observations, not of addresses in scope. There is no row and
/// no counter for an address that stayed silent — see <see cref="DiscoveryRunHost"/>.
/// </para>
/// </remarks>
internal sealed class DiscoveryRun
{
    /// <summary>UUID v7, so the primary key is also the order runs started in.</summary>
    public Guid Id { get; init; }

    /// <summary>The seed this run swept.</summary>
    public Guid SeedId { get; init; }

    /// <summary>What that seed was called when the run started.</summary>
    /// <remarks>
    /// Copied rather than joined, for the reason the ranges are: a seed can be renamed or
    /// removed, and the history should still say what was swept.
    /// </remarks>
    public string SeedName { get; init; } = string.Empty;

    /// <summary>Whether the schedule started it or a person did.</summary>
    public DiscoveryRunTrigger Trigger { get; init; }

    /// <summary>How far it has got, and how much of it got through.</summary>
    public DiscoveryRunStatus Status { get; set; }

    /// <summary>The blocks this run swept.</summary>
    public IReadOnlyList<string> Ranges { get; init; } = [];

    /// <summary>What it left alone inside them.</summary>
    public IReadOnlyList<string> Exclusions { get; init; } = [];

    /// <summary>How many addresses it set out to probe, after exclusions.</summary>
    public long AddressCount { get; init; }

    /// <summary>How many sweep jobs it was split into.</summary>
    public int JobCount { get; set; }

    /// <summary>How many of those have reported.</summary>
    public int JobsCompleted { get; set; }

    /// <summary>How many of those reported a failure.</summary>
    public int JobsFailed { get; set; }

    /// <summary>How many addresses answered.</summary>
    public int RespondedCount { get; set; }

    /// <summary>How many of those nobody had seen before.</summary>
    public int NewCandidateCount { get; set; }

    /// <summary>How many were already candidates awaiting review.</summary>
    public int KnownCandidateCount { get; set; }

    /// <summary>How many already belong to a device in the inventory.</summary>
    public int ExistingDeviceCount { get; set; }

    /// <summary>How many are on the permanent ignore list.</summary>
    public int IgnoredCount { get; set; }

    /// <summary>When the run was queued. UTC.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>When its last sweep job was applied, or <see langword="null"/> while it runs. UTC.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>When the row was created. UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the row last changed. UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
