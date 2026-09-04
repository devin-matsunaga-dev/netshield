namespace NetShield.Contracts.Inventory;

/// <summary>One discovery run as the history list renders it.</summary>
/// <param name="Id">The run.</param>
/// <param name="SeedId">The seed it swept.</param>
/// <param name="SeedName">What that seed is called, as it was named at the time.</param>
/// <param name="Trigger">Whether the schedule started it or a person did.</param>
/// <param name="Status">How far it has got, and how much of it got through.</param>
/// <param name="AddressCount">How many addresses it set out to sweep, after exclusions.</param>
/// <param name="RespondedCount">How many of them answered.</param>
/// <param name="NewCandidateCount">How many of those nobody had seen before.</param>
/// <param name="StartedAt">When it was queued. UTC.</param>
/// <param name="CompletedAt">When its last sweep job reported. UTC.</param>
public sealed record DiscoveryRunSummary(
    Guid Id,
    Guid SeedId,
    string SeedName,
    DiscoveryRunTrigger Trigger,
    DiscoveryRunStatus Status,
    long AddressCount,
    int RespondedCount,
    int NewCandidateCount,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);
