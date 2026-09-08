namespace NetShield.Contracts.Inventory;

/// <summary>
/// One period during which one device's port reported one client, as a half-open interval.
/// </summary>
/// <remarks>
/// <para>
/// Shaped like <see cref="ClientIpBindingSummary"/> and closed by the same rule, with one
/// difference that matters: the uniqueness is per <em>device</em> rather than per port. A MAC is
/// learned by every switch on the path to it, so a client plugged into an access switch appears
/// in that switch's forwarding database on its access port <em>and</em> in every upstream
/// switch's on the uplink. Both are true, both are recorded, and neither is discarded — picking
/// one as "the" port needs the topology Phase 2 builds.
/// </para>
/// <para>
/// <see cref="MacCountOnPort"/> is what makes the list readable in the meantime. It is counted
/// from the same forwarding database the binding came out of, so it is evidence rather than a
/// guess: a port that learned one MAC is where something is plugged in, and a port that learned
/// two hundred is a trunk carrying everything behind it.
/// </para>
/// </remarks>
/// <param name="Id">The binding row.</param>
/// <param name="DeviceId">The device whose forwarding database reported it.</param>
/// <param name="DeviceHostname">That device's hostname.</param>
/// <param name="IfIndex">The device's own index for the port.</param>
/// <param name="InterfaceName">
/// What the device calls that port, when a walk has recorded its interface inventory. Resolved
/// from <c>device_interfaces</c>, so a device nothing has fingerprinted answers with nothing.
/// </param>
/// <param name="VlanId">The VLAN the port learned it on, where the device's table says which.</param>
/// <param name="MacCountOnPort">
/// How many MAC addresses that port had learned when this observation was made.
/// </param>
/// <param name="Source">Which kind of observation established it.</param>
/// <param name="ObservedFrom">When the binding opened. UTC, inclusive.</param>
/// <param name="ObservedTo">When it closed, or <see langword="null"/> while it is current. UTC, exclusive.</param>
/// <param name="LastSeenAt">When an observation last confirmed it. UTC.</param>
public sealed record ClientPortBindingSummary(
    Guid Id,
    Guid DeviceId,
    string? DeviceHostname,
    int IfIndex,
    string? InterfaceName,
    int? VlanId,
    int? MacCountOnPort,
    ClientObservationSource Source,
    DateTimeOffset ObservedFrom,
    DateTimeOffset? ObservedTo,
    DateTimeOffset LastSeenAt);
