namespace NetShield.Contracts.Inventory;

/// <summary>
/// A discovery seed as the list renders it: what NetShield sweeps, and when it next will.
/// </summary>
/// <param name="Id">The seed.</param>
/// <param name="Name">What an operator calls it.</param>
/// <param name="Enabled">Whether the schedule runs it. An on-demand run ignores this.</param>
/// <param name="RangeCount">How many CIDR ranges it names.</param>
/// <param name="AddressCount">
/// How many addresses one run of it would sweep, after exclusions. Computed rather than stored,
/// so it cannot disagree with the ranges beside it.
/// </param>
/// <param name="IntervalMinutes">How often the schedule runs it.</param>
/// <param name="NextRunAt">When the schedule will next run it, or nothing if it is disabled. UTC.</param>
/// <param name="LastRunAt">When it last started a run. UTC.</param>
/// <param name="UpdatedAt">When the row last changed. UTC.</param>
public sealed record DiscoverySeedSummary(
    Guid Id,
    string Name,
    bool Enabled,
    int RangeCount,
    long AddressCount,
    int IntervalMinutes,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? LastRunAt,
    DateTimeOffset UpdatedAt);
