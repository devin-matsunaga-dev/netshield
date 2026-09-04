namespace NetShield.Contracts.Inventory;

/// <summary>One discovery run in full.</summary>
/// <remarks>
/// <see cref="Ranges"/> is the run's own copy of what it swept, not a reference to the seed's.
/// A seed is editable, and a run that reported "254 addresses" has to keep saying which 254 they
/// were after somebody widens the seed — otherwise the history stops describing what happened.
/// </remarks>
/// <param name="Id">The run.</param>
/// <param name="SeedId">The seed it swept.</param>
/// <param name="SeedName">What that seed was called when the run started.</param>
/// <param name="Trigger">Whether the schedule started it or a person did.</param>
/// <param name="Status">How far it has got, and how much of it got through.</param>
/// <param name="Ranges">The CIDR ranges this run swept.</param>
/// <param name="Exclusions">What it left alone inside them.</param>
/// <param name="AddressCount">How many addresses it set out to sweep, after exclusions.</param>
/// <param name="JobCount">How many sweep jobs it was split into.</param>
/// <param name="JobsCompleted">How many of those have reported.</param>
/// <param name="JobsFailed">How many of those reported a failure.</param>
/// <param name="RespondedCount">How many addresses answered.</param>
/// <param name="NewCandidateCount">How many of those nobody had seen before.</param>
/// <param name="KnownCandidateCount">How many were already candidates awaiting review.</param>
/// <param name="ExistingDeviceCount">How many already belong to a device in the inventory.</param>
/// <param name="IgnoredCount">How many are on the permanent ignore list.</param>
/// <param name="StartedAt">When it was queued. UTC.</param>
/// <param name="CompletedAt">When its last sweep job reported. UTC.</param>
public sealed record DiscoveryRunDetail(
    Guid Id,
    Guid SeedId,
    string SeedName,
    DiscoveryRunTrigger Trigger,
    DiscoveryRunStatus Status,
    IReadOnlyList<string> Ranges,
    IReadOnlyList<string> Exclusions,
    long AddressCount,
    int JobCount,
    int JobsCompleted,
    int JobsFailed,
    int RespondedCount,
    int NewCandidateCount,
    int KnownCandidateCount,
    int ExistingDeviceCount,
    int IgnoredCount,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);
