using System.Text.Json.Serialization;

namespace NetShield.Contracts.Inventory;

/// <summary>What NetShield made of one address that answered a sweep.</summary>
/// <remarks>
/// <para>
/// There is no member for an address that did not answer, because there is no row for one. A run
/// records the ranges it swept and how many addresses that was, so "was this address in scope"
/// stays answerable; writing a row per silent address would put tens of thousands of rows per
/// run into a table nothing prunes yet, to say nothing happened.
/// </para>
/// <para>
/// Serialised as its name rather than its ordinal (WP-0.4).
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<DiscoveryHostOutcome>))]
public enum DiscoveryHostOutcome
{
    /// <summary>Nobody had seen this address before. It is now a candidate awaiting review.</summary>
    NewCandidate,

    /// <summary>A candidate for this address already existed, and its last-seen moved.</summary>
    KnownCandidate,

    /// <summary>The address already belongs to a device in the inventory.</summary>
    ExistingDevice,

    /// <summary>The address is on the permanent ignore list, so no candidate was made.</summary>
    Ignored
}
