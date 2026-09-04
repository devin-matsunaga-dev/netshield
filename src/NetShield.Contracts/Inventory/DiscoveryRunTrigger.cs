using System.Text.Json.Serialization;

namespace NetShield.Contracts.Inventory;

/// <summary>Why a discovery run started.</summary>
/// <remarks>
/// Serialised as its name rather than its ordinal, so that inserting a member cannot renumber
/// what a stored response or a generated client already means (WP-0.4). The attribute is on the
/// type, not on a serializer context's converter list — five WP-1.1 enums are written as
/// ordinals today precisely because that list does not take effect when the context is one
/// resolver among several.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<DiscoveryRunTrigger>))]
public enum DiscoveryRunTrigger
{
    /// <summary>The schedule reached the seed's next run.</summary>
    Scheduled,

    /// <summary>A person asked for it, outside the schedule.</summary>
    OnDemand
}
