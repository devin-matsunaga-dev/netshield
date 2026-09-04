using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Reachability;

/// <summary>
/// What NetShield knows about one device's reachability, and when it will ask again.
/// </summary>
/// <remarks>
/// <para>
/// A table of its own rather than columns on <c>devices</c>. <c>devices</c> is the inventory an
/// operator maintains; this is machinery that writes on every probe, and putting a counter that
/// changes every sixty seconds beside the notes field would mean every reachability probe
/// touching the row that the device list sorts by <c>updated_at</c>. The one thing that does
/// belong on the device is the answer — <c>devices.state</c> stays the published state, and it is
/// written only on a transition.
/// </para>
/// <para>
/// One row per device, created lazily by the scheduler the first time a device falls due. A
/// device with no row here has never been considered for a probe; a device whose row says
/// <see cref="PendingState"/> is <see cref="DeviceState.Unknown"/> has been queued but has not
/// yet produced an observation.
/// </para>
/// </remarks>
internal sealed class DeviceReachability
{
    /// <summary>UUID v7, so the primary key is also the order rows were created in.</summary>
    public Guid Id { get; init; }

    /// <summary>The device this is about. Unique — a device has one reachability row.</summary>
    public Guid DeviceId { get; init; }

    /// <summary>
    /// The observation the current run is made of, or <see cref="DeviceState.Unknown"/> before
    /// the first probe has reported.
    /// </summary>
    public DeviceState PendingState { get; set; } = DeviceState.Unknown;

    /// <summary>
    /// How many consecutive times <see cref="PendingState"/> has been observed. Together with the
    /// thresholds in <see cref="ReachabilityOptions"/> this is the whole of the hysteresis.
    /// </summary>
    public int PendingObservations { get; set; }

    /// <summary>The earliest the next probe should be queued. UTC.</summary>
    public DateTimeOffset NextProbeAt { get; set; }

    /// <summary>When a probe last reported an observation, successful or not. UTC.</summary>
    public DateTimeOffset? LastProbeAt { get; set; }

    /// <summary>When the device's published state last moved. UTC.</summary>
    public DateTimeOffset? LastChangedAt { get; set; }

    /// <summary>The mean round trip of the answered requests in the last probe that ran.</summary>
    public double? LastRttMilliseconds { get; set; }

    /// <summary>What proportion of the last probe's requests went unanswered, 0 to 100.</summary>
    public double? LastLossPercent { get; set; }

    /// <summary>
    /// The job whose result was last applied to this row.
    /// </summary>
    /// <remarks>
    /// Outbox delivery is at-least-once, and every counter above is the kind of thing that a
    /// second delivery of one event would quietly corrupt — a redelivered failure would advance a
    /// run towards a threshold that only one probe supports. This is what makes the handler safe
    /// to run twice: a result whose job has already been applied is dropped rather than folded in
    /// again.
    /// </remarks>
    public Guid? LastAppliedJobId { get; set; }

    /// <summary>
    /// Why the last probe could not be performed, or <see langword="null"/> if the last one ran.
    /// </summary>
    /// <remarks>
    /// This is the collector's health, not the device's, and it deliberately does not reach
    /// <c>devices.state</c>. It is here so that "this device has not been probed since Tuesday"
    /// has an answer an operator can read, rather than presenting as a device that is somehow
    /// still Online with a stale <see cref="LastProbeAt"/>.
    /// </remarks>
    public string? LastError { get; set; }

    /// <summary>When the row was created. UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the row last changed. UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
