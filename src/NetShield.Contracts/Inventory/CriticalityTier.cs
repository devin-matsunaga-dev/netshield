using System.Text.Json.Serialization;

namespace NetShield.Contracts.Inventory;

/// <summary>
/// How much it matters when this device fails. A manual attribute (SPEC.md §2), and one half of
/// the vulnerability prioritisation score in Phase 7.
/// </summary>
/// <remarks>
/// Serialised as its name rather than its ordinal, so that inserting a member cannot renumber
/// what a stored row, a generated client or a saved fixture already means (WP-0.4). The
/// attribute has to sit on the type: naming a converter in a serializer context's
/// <c>JsonSourceGenerationOptions</c> has no effect once that context is one resolver among
/// several on the host's JSON options.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<CriticalityTier>))]
public enum CriticalityTier
{
    /// <summary>Failure is noticed but absorbed.</summary>
    Low,

    /// <summary>Failure degrades a service.</summary>
    Medium,

    /// <summary>Failure takes a service down.</summary>
    High,

    /// <summary>Failure takes the estate down.</summary>
    Critical
}
