namespace NetShield.Contracts.Inventory;

/// <summary>
/// One period during which one client held one address, as a half-open interval.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The intervals for an address never overlap and never gap.</strong> When an
/// observation finds an address on a different MAC, the binding that held it is closed at
/// exactly the instant the new one opens — so every timestamp falls inside exactly one binding,
/// which is what makes <c>ResolveAssetAt</c> answer unambiguously on both sides of a handover.
/// The interval is half-open: <see cref="ObservedFrom"/> is inside it and
/// <see cref="ObservedTo"/> is not.
/// </para>
/// <para>
/// <strong>A boundary is an observation, not an event.</strong> NetShield learns an address by
/// reading a device's ARP table on a schedule, so the true handover happened somewhere in the
/// window before the observation that noticed it. Closing the old binding where the new one
/// opens means a timestamp inside that window resolves to the <em>previous</em> holder. That is
/// a deliberate convention: the previous holder is the one NetShield had actually observed
/// holding the address at that moment, and inventing an earlier boundary would be asserting a
/// handover nobody saw.
/// </para>
/// </remarks>
/// <param name="Id">The binding row.</param>
/// <param name="IpAddress">The address.</param>
/// <param name="Source">Which kind of observation established it.</param>
/// <param name="DeviceId">The device that reported it — whose ARP table this was read from.</param>
/// <param name="DeviceHostname">That device's hostname.</param>
/// <param name="ObservedFrom">When the binding opened. UTC, inclusive.</param>
/// <param name="ObservedTo">
/// When it closed, or <see langword="null"/> while it is the current binding for this address.
/// UTC, exclusive.
/// </param>
/// <param name="LastSeenAt">
/// When an observation last confirmed it. On an open binding this keeps moving; on a closed one
/// it is the last time the address was seen on this MAC, which need not be when it closed.
/// </param>
public sealed record ClientIpBindingSummary(
    Guid Id,
    string IpAddress,
    ClientObservationSource Source,
    Guid? DeviceId,
    string? DeviceHostname,
    DateTimeOffset ObservedFrom,
    DateTimeOffset? ObservedTo,
    DateTimeOffset LastSeenAt);
