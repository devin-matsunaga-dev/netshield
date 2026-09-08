namespace NetShield.Contracts.Inventory;

/// <summary>
/// What the last ICMP probe of a device found, beside the state it published.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DeviceDetail.State"/> is the conclusion; this is the evidence. A sub-resource
/// rather than members on the device, because a counter that changes every sixty seconds has no
/// business touching the row the device list sorts by.
/// </para>
/// <para>
/// <see cref="LastError"/> is the member that matters most. Without it, a device nothing has
/// been able to probe since Tuesday presents as a device that is confidently Online with a stale
/// <see cref="LastProbeAt"/>.
/// </para>
/// <para>
/// Absent — 404 — until the device has been scheduled for a probe at all.
/// </para>
/// </remarks>
/// <param name="DeviceId">The device this is about.</param>
/// <param name="State">The published state. The same value <see cref="DeviceDetail.State"/> carries.</param>
/// <param name="LastRttMilliseconds">The mean round trip of the last completed probe.</param>
/// <param name="LastLossPercent">How much of the last probe went unanswered.</param>
/// <param name="LastProbeAt">When a probe result was last applied. UTC.</param>
/// <param name="LastChangedAt">When <see cref="State"/> last changed. UTC.</param>
/// <param name="NextProbeAt">When the schedule will next ask for one. UTC.</param>
/// <param name="LastError">
/// Why the last probe could not be performed, or nothing if the last one ran. A collector that
/// cannot open an ICMP socket says so here rather than by reporting the estate offline.
/// </param>
public sealed record DeviceReachabilityDetail(
    Guid DeviceId,
    DeviceState State,
    double? LastRttMilliseconds,
    double? LastLossPercent,
    DateTimeOffset? LastProbeAt,
    DateTimeOffset? LastChangedAt,
    DateTimeOffset NextProbeAt,
    string? LastError);
