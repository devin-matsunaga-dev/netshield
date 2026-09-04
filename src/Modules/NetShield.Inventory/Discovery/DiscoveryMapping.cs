using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Discovery;

/// <summary>
/// Turns the discovery entities into the shapes that leave the module. The one place the
/// boundary in ARCHITECTURE.md §4 is crossed for them.
/// </summary>
internal static class DiscoveryMapping
{
    /// <summary>
    /// A seed as the list renders it. The address count is computed rather than stored, so it
    /// cannot disagree with the ranges beside it — and a seed whose ranges will not parse is
    /// reported as zero rather than throwing, because every write path parses them first and a
    /// read is the wrong place to discover that somebody edited the database by hand.
    /// </summary>
    internal static DiscoverySeedSummary ToSummary(this DiscoverySeed seed) =>
        new(
            seed.Id,
            seed.Name,
            seed.Enabled,
            seed.Ranges.Count,
            seed.CountAddresses(),
            seed.IntervalMinutes,
            seed.Enabled ? seed.NextRunAt : null,
            seed.LastRunAt,
            seed.UpdatedAt);

    internal static DiscoverySeedDetail ToDetail(this DiscoverySeed seed) =>
        new(
            seed.Id,
            seed.Name,
            seed.Description,
            seed.Enabled,
            seed.Ranges,
            seed.Exclusions,
            seed.CountAddresses(),
            seed.IntervalMinutes,
            seed.Enabled ? seed.NextRunAt : null,
            seed.LastRunAt,
            seed.CreatedAt,
            seed.UpdatedAt);

    internal static DiscoveryRunSummary ToSummary(this DiscoveryRun run) =>
        new(
            run.Id,
            run.SeedId,
            run.SeedName,
            run.Trigger,
            run.Status,
            run.AddressCount,
            run.RespondedCount,
            run.NewCandidateCount,
            run.StartedAt,
            run.CompletedAt);

    internal static DiscoveryRunDetail ToDetail(this DiscoveryRun run) =>
        new(
            run.Id,
            run.SeedId,
            run.SeedName,
            run.Trigger,
            run.Status,
            run.Ranges,
            run.Exclusions,
            run.AddressCount,
            run.JobCount,
            run.JobsCompleted,
            run.JobsFailed,
            run.RespondedCount,
            run.NewCandidateCount,
            run.KnownCandidateCount,
            run.ExistingDeviceCount,
            run.IgnoredCount,
            run.StartedAt,
            run.CompletedAt);

    internal static DiscoveryRunHostResult ToContract(this DiscoveryRunHost host) =>
        new(
            host.Id,
            host.RunId,
            host.Address.ToString(),
            host.RttMilliseconds,
            host.Outcome,
            host.CandidateId,
            host.DeviceId,
            host.ObservedAt);

    internal static DiscoveryCandidateSummary ToSummary(this DiscoveryCandidate candidate) =>
        new(
            candidate.Id,
            candidate.Address.ToString(),
            candidate.Status,
            candidate.TimesSeen,
            candidate.LastRttMilliseconds,
            candidate.FirstSeenAt,
            candidate.LastSeenAt,
            candidate.FirstSeenRunId,
            candidate.LastSeenRunId,
            candidate.PromotedDeviceId);

    internal static DiscoveryIgnoreEntry ToContract(this DiscoveryIgnore ignore) =>
        new(ignore.Id, ignore.Cidr, ignore.Reason, ignore.CreatedAt);

    /// <summary>What an audit row records about a seed.</summary>
    /// <remarks>
    /// Every key is chosen so that <c>SecretRedactor</c> leaves it alone. A seed carries no
    /// secret — it names no credential, which is the point of
    /// <c>DiscoveryOptions.CredentialKindOrder</c> — so there is nothing here to blank.
    /// </remarks>
    internal static IReadOnlyDictionary<string, object?> ToAuditSnapshot(this DiscoverySeed seed) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = seed.Name,
            ["description"] = seed.Description,
            ["enabled"] = seed.Enabled,
            ["ranges"] = string.Join(", ", seed.Ranges),
            ["exclusions"] = string.Join(", ", seed.Exclusions),
            ["intervalMinutes"] = seed.IntervalMinutes
        };

    /// <summary>What an audit row records about an ignore entry.</summary>
    internal static IReadOnlyDictionary<string, object?> ToAuditSnapshot(this DiscoveryIgnore ignore) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["cidr"] = ignore.Cidr,
            ["reason"] = ignore.Reason
        };

    /// <summary>How many addresses one run of this seed would probe, after exclusions.</summary>
    private static long CountAddresses(this DiscoverySeed seed) =>
        SweepPlan.Create(seed.Ranges, seed.Exclusions) is { IsSuccess: true } plan
            ? plan.Value.AddressCount
            : 0;
}
