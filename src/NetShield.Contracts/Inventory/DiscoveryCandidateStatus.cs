using System.Text.Json.Serialization;

namespace NetShield.Contracts.Inventory;

/// <summary>Where a discovered address stands with the person reviewing it.</summary>
/// <remarks>
/// <para>
/// A candidate is never promoted on its own. SPEC.md §2 puts discovery results in front of an
/// operator, and WP-1.6's own criterion is that results appear as reviewable candidates rather
/// than auto-created devices — so <see cref="New"/> is where every candidate starts and the
/// other two are the two things a person can decide.
/// </para>
/// <para>
/// Serialised as its name rather than its ordinal (WP-0.4).
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<DiscoveryCandidateStatus>))]
public enum DiscoveryCandidateStatus
{
    /// <summary>Found, and nobody has decided anything about it yet.</summary>
    New,

    /// <summary>It is a device in the inventory now.</summary>
    Promoted,

    /// <summary>It is on the ignore list and will not be offered again.</summary>
    Ignored
}
