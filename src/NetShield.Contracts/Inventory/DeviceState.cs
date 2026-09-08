using System.Text.Json.Serialization;

namespace NetShield.Contracts.Inventory;

/// <summary>
/// Whether a device is answering. DESIGN.md §3 fixes how each renders: Online → <c>success</c>,
/// Warning → <c>warning</c>, Offline → <c>danger</c>, Unknown → <c>text-muted</c>.
/// </summary>
/// <remarks>
/// <para>
/// Never set over the API. The reachability work in WP-1.4 owns every transition, driven by
/// consecutive success and failure thresholds; a device created by hand starts
/// <see cref="Unknown"/> and stays there until something has probed it.
/// </para>
/// <para>
/// Serialised as its name rather than its ordinal, so that inserting a member cannot renumber
/// what a stored row, a generated client or a saved fixture already means (WP-0.4). The
/// attribute has to sit on the type: naming a converter in a serializer context's
/// <c>JsonSourceGenerationOptions</c> has no effect once that context is one resolver among
/// several on the host's JSON options.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<DeviceState>))]
public enum DeviceState
{
    /// <summary>Nothing has probed this device yet.</summary>
    Unknown,

    /// <summary>Answering.</summary>
    Online,

    /// <summary>Answering, degraded.</summary>
    Warning,

    /// <summary>Not answering.</summary>
    Offline
}
