using System.Text.Json.Serialization;

namespace NetShield.Contracts.Inventory;

/// <summary>What kind of thing an address resolved to at a point in time.</summary>
/// <remarks>
/// <para>
/// The three answers <c>ResolveAssetAt</c> can give. <see cref="Unresolved"/> is a real answer
/// rather than an error: an address NetShield has never observed is the ordinary state of every
/// address outside the estate, and ARCHITECTURE.md §6 requires enrichment to land an event with a
/// null asset reference rather than to fail the write.
/// </para>
/// <para>
/// Serialised as its name rather than its ordinal (WP-0.4).
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<AssetKind>))]
public enum AssetKind
{
    /// <summary>Nothing in the inventory held this address then.</summary>
    Unresolved,

    /// <summary>A monitored device — the inventory an operator maintains.</summary>
    Device,

    /// <summary>A tracked client — an endpoint NetShield observed, identified by its MAC.</summary>
    Client
}
