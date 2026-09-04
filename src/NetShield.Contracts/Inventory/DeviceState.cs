namespace NetShield.Contracts.Inventory;

/// <summary>
/// Whether a device is answering. DESIGN.md §3 fixes how each renders: Online → <c>success</c>,
/// Warning → <c>warning</c>, Offline → <c>danger</c>, Unknown → <c>text-muted</c>.
/// </summary>
/// <remarks>
/// Never set over the API. The reachability work in WP-1.4 owns every transition, driven by
/// consecutive success and failure thresholds; a device created by hand starts
/// <see cref="Unknown"/> and stays there until something has probed it.
/// </remarks>
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
