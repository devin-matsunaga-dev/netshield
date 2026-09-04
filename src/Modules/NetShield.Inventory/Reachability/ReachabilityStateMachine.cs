using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Reachability;

/// <summary>
/// The whole of the device state machine: how one probe is read, and when a run of readings is
/// enough to change what NetShield says about a device.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a pure function of what it is handed. Nothing here touches the database, the
/// clock, or the job row — which is what lets every transition, every threshold and the flapping
/// case be a unit test rather than an integration test that has to arrange a queue to reach them.
/// </para>
/// <para>
/// <strong>An observation is evidence about the device, never about the collector.</strong> This
/// is only ever reached for a probe that actually ran; a job the collector could not perform —
/// no ICMP socket, an address it could not use, a timeout inside its own process — is a failed
/// job, and a failed job is recorded against the reachability row without being classified. If
/// it were classified, a collector that lost its privileges would take the entire estate offline
/// and the alert would name five hundred devices instead of the one process that broke.
/// </para>
/// </remarks>
internal static class ReachabilityStateMachine
{
    /// <summary>
    /// Reads one completed probe.
    /// </summary>
    /// <remarks>
    /// Three outcomes, and no configurable loss threshold between them. Every reply came back,
    /// or some did, or none did — the middle case is the definition of degraded, and a knob
    /// saying how much loss is acceptable would only move where the same argument happens.
    /// A single stray lost packet does not move a device on its own: that is what
    /// <see cref="ReachabilityOptions.SuccessThreshold"/> is for.
    /// </remarks>
    /// <param name="sent">How many echo requests the probe sent. Always at least one.</param>
    /// <param name="received">How many replies came back.</param>
    internal static DeviceState Classify(int sent, int received)
    {
        if (sent <= 0)
        {
            // A probe that sent nothing observed nothing. It cannot be evidence either way, and
            // the caller treats Unknown as "do not apply this".
            return DeviceState.Unknown;
        }

        if (received <= 0)
        {
            return DeviceState.Offline;
        }

        return received >= sent ? DeviceState.Online : DeviceState.Warning;
    }

    /// <summary>
    /// Folds one observation into what is already known, and says whether the device's published
    /// state moves as a result.
    /// </summary>
    /// <param name="current">The state the device is published as being in now.</param>
    /// <param name="pendingState">The observation the run so far is made of.</param>
    /// <param name="pendingObservations">How long that run is.</param>
    /// <param name="observed">What this probe saw.</param>
    /// <param name="options">The thresholds a run has to reach to be adopted.</param>
    internal static ReachabilityTransition Apply(
        DeviceState current,
        DeviceState pendingState,
        int pendingObservations,
        DeviceState observed,
        ReachabilityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (observed == DeviceState.Unknown)
        {
            // Nothing was observed, so nothing is known that was not known before. The run is
            // left exactly as it was rather than being broken, because a probe that observed
            // nothing is not a contradiction of the ones that did.
            return new ReachabilityTransition(pendingState, pendingObservations, current, Changed: false);
        }

        // A run is consecutive identical observations. Anything else starts a new one, which is
        // why an alternating device never reaches a threshold: each probe resets the other's
        // count to one.
        int run = observed == pendingState ? pendingObservations + 1 : 1;

        int required = observed == DeviceState.Offline
            ? options.FailureThreshold
            : options.SuccessThreshold;

        bool adopted = run >= required && observed != current;

        return new ReachabilityTransition(
            observed,
            run,
            adopted ? observed : current,
            adopted);
    }
}

/// <summary>What one observation did to a device's reachability.</summary>
/// <param name="PendingState">The observation the run is now made of.</param>
/// <param name="PendingObservations">How many consecutive times it has now been seen.</param>
/// <param name="State">The state the device should be published as, after this observation.</param>
/// <param name="Changed">
/// Whether <paramref name="State"/> differs from what the device was published as before. It is
/// what decides whether an event is raised, so that a device that stays offline for a week
/// produces one event and not ten thousand.
/// </param>
internal sealed record ReachabilityTransition(
    DeviceState PendingState,
    int PendingObservations,
    DeviceState State,
    bool Changed);
