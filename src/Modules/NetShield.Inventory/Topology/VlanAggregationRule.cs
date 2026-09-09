namespace NetShield.Inventory.Topology;

/// <summary>
/// The decision WP-2.2 turns on, as one pure function over values: what several switches carrying
/// one VLAN add up to.
/// </summary>
/// <remarks>
/// <para>
/// <em>"A VLAN present on multiple switches appears once with aggregated membership"</em> is the
/// criterion this package is measured against, and everything around it — the walk, the table,
/// the schedule, the endpoints — is machinery that can be exercised end to end. The merge itself
/// is arithmetic, and it lives here so it can be tested as arithmetic. It is the shape WP-1.8
/// gave <c>AssetResolutionRule</c> and WP-2.1 gave <c>AdjacencyRule</c>, for the same reason.
/// </para>
/// <para>
/// <strong>The identity is the VLAN id and nothing else.</strong> SPEC.md §1 describes one
/// single-tenant estate, and within one estate a VLAN id is unique by convention — that is what a
/// VLAN id is for. So two switches reporting VLAN 20 are reporting the same VLAN, and this merges
/// them. Scoping a VLAN by site would be a different identity model rather than better discovery,
/// and it is not introduced without a requirement that asks for it (WP-2.2, at the human's
/// instruction).
/// </para>
/// <para>
/// <strong>Membership is summed, not unioned.</strong> A port belongs to exactly one device, so
/// two switches carrying VLAN 20 on four ports each carry it on eight ports between them. There
/// is nothing to de-duplicate, which is what makes the merge as simple as it is — and is worth
/// saying out loud, because the neighbour case one package earlier was the opposite.
/// </para>
/// <para>
/// Nothing here reads a database, a clock or a configuration value. Given the same memberships it
/// returns the same answer on any machine, in any order they arrive.
/// </para>
/// </remarks>
internal static class VlanAggregationRule
{
    /// <summary>One device's account of a VLAN, reduced to what the merge depends on.</summary>
    /// <param name="DeviceId">The device that reported it.</param>
    /// <param name="Name">What that device calls it, where it said.</param>
    /// <param name="PortCount">How many of its ports carry it.</param>
    /// <param name="FirstDiscoveredAt">When it was first seen there. UTC.</param>
    /// <param name="LastSeenAt">When it was last confirmed there. UTC.</param>
    internal readonly record struct Membership(
        Guid DeviceId,
        string? Name,
        int PortCount,
        DateTimeOffset FirstDiscoveredAt,
        DateTimeOffset LastSeenAt);

    /// <summary>What one VLAN's memberships add up to.</summary>
    /// <param name="Name">The name to show, or nothing when no device named it.</param>
    /// <param name="NameDisputed">Whether the devices disagree about that name.</param>
    /// <param name="Names">Every distinct spelling, most used first, then alphabetically.</param>
    /// <param name="DeviceCount">How many devices carry it.</param>
    /// <param name="PortCount">How many ports carry it, across all of them.</param>
    /// <param name="FirstDiscoveredAt">The earliest first-seen. UTC.</param>
    /// <param name="LastSeenAt">The latest last-seen. UTC.</param>
    internal sealed record Aggregated(
        string? Name,
        bool NameDisputed,
        IReadOnlyList<string> Names,
        int DeviceCount,
        int PortCount,
        DateTimeOffset FirstDiscoveredAt,
        DateTimeOffset LastSeenAt);

    /// <summary>
    /// Merges every device's account of one VLAN into the one row the estate shows for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The name is chosen, not invented.</strong> The spelling the most devices use wins;
    /// where two spellings are used equally often the alphabetically first wins, so the answer
    /// depends on the <em>set</em> of memberships and not on the order they were read in. A device
    /// that named nothing is not a vote for anonymity — it simply does not vote, which is what
    /// keeps one switch answering the nameless fallback table from blanking a VLAN five others
    /// named.
    /// </para>
    /// <para>
    /// <strong>Case is not a disagreement.</strong> <c>Users VLAN</c> and <c>USERS VLAN</c> are one
    /// operator typing the same word twice, and flagging that would make the flag mean nothing on
    /// a real estate. The comparison is case-insensitive and whitespace-trimmed; the spelling
    /// reported is one somebody actually typed.
    /// </para>
    /// </remarks>
    /// <param name="memberships">Every device's account. Must not be empty.</param>
    internal static Aggregated Aggregate(IReadOnlyCollection<Membership> memberships)
    {
        ArgumentNullException.ThrowIfNull(memberships);

        if (memberships.Count == 0)
        {
            throw new ArgumentException(
                "A VLAN with no memberships is not a VLAN NetShield has observed.",
                nameof(memberships));
        }

        IReadOnlyList<string> names = Names(memberships);

        return new Aggregated(
            names.Count == 0 ? null : names[0],
            names.Count > 1,
            names,
            memberships.Select(membership => membership.DeviceId).Distinct().Count(),
            memberships.Sum(membership => membership.PortCount),
            memberships.Min(membership => membership.FirstDiscoveredAt),
            memberships.Max(membership => membership.LastSeenAt));
    }

    /// <summary>
    /// Every distinct name the estate uses, most used first and then alphabetically.
    /// </summary>
    /// <remarks>
    /// Grouped case-insensitively and reported in the most-used casing within each group, again
    /// broken alphabetically — so the whole answer is a function of the set and two runs over the
    /// same estate produce byte-identical output.
    /// </remarks>
    private static IReadOnlyList<string> Names(IReadOnlyCollection<Membership> memberships)
    {
        return
        [
            .. memberships
                .Select(membership => membership.Name?.Trim())
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => name!)
                .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    Count = group.Count(),
                    Spelling = group
                        .GroupBy(name => name, StringComparer.Ordinal)
                        .OrderByDescending(spelling => spelling.Count())
                        .ThenBy(spelling => spelling.Key, StringComparer.Ordinal)
                        .First().Key
                })
                .OrderByDescending(entry => entry.Count)
                .ThenBy(entry => entry.Spelling, StringComparer.Ordinal)
                .Select(entry => entry.Spelling)
        ];
    }
}
