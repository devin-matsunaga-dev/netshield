using NetShield.Contracts.Messaging;

namespace NetShield.Contracts.Inventory.Events;

/// <summary>
/// A device's reachability state changed, and the change survived the hysteresis.
/// </summary>
/// <remarks>
/// <para>
/// Published on a transition and only on one. A device probed every sixty seconds produces one
/// of these when it goes down and one when it comes back, not one an hour for each hour it stays
/// down — the state machine adopts an observation only after it has been seen the configured
/// number of consecutive times, so a flapping device produces no event at all rather than one
/// per probe.
/// </para>
/// <para>
/// It carries the hostname as well as the id, unlike the credential events which carry
/// identifiers alone. The first subscriber this is built for is the alerting in Phase 6, which
/// lives in another module and so cannot read the inventory table to find out what to call the
/// device its notification is about. <c>DeviceUpdated</c> already carries the same field for the
/// same kind of reason.
/// </para>
/// </remarks>
/// <param name="DeviceId">The device.</param>
/// <param name="Hostname">What it is called, as the inventory holds it.</param>
/// <param name="PreviousState">The state it was in until now.</param>
/// <param name="State">The state it is in now. Never <see cref="DeviceState.Unknown"/>: nothing
/// transitions back to never-having-been-probed.</param>
/// <param name="ChangedAt">When the transition was recorded. UTC.</param>
public sealed record DeviceStateChanged(
    Guid DeviceId,
    string Hostname,
    DeviceState PreviousState,
    DeviceState State,
    DateTimeOffset ChangedAt) : IIntegrationEvent;
