using System.Text.Json.Serialization;

namespace NetShield.Contracts.Inventory;

/// <summary>Which environment a device belongs to. A manual attribute (SPEC.md §2).</summary>
/// <remarks>
/// Serialised as its name rather than its ordinal, so that inserting a member cannot renumber
/// what a stored row, a generated client or a saved fixture already means (WP-0.4). The
/// attribute has to sit on the type: naming a converter in a serializer context's
/// <c>JsonSourceGenerationOptions</c> has no effect once that context is one resolver among
/// several on the host's JSON options.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<DeviceEnvironment>))]
public enum DeviceEnvironment
{
    /// <summary>Carries live traffic.</summary>
    Production,

    /// <summary>Pre-production, shaped like production.</summary>
    Staging,

    /// <summary>Where changes are built.</summary>
    Development,

    /// <summary>A test bench. Not expected to be stable.</summary>
    Lab
}
