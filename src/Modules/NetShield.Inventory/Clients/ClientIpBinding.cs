using System.Net;

using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Clients;

/// <summary>
/// One period during which one client held one address, as a half-open interval.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This table is the whole of time-accurate resolution.</strong> Everything downstream —
/// every Phase 4 flow record and every Phase 5 log event — resolves the address it carries
/// through the intervals here, and an interval that is wrong at a handover boundary is wrong
/// silently, in data nobody re-reads. Two invariants make the answer unambiguous, and both are
/// enforced by the database rather than by the handler that maintains them:
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// <strong>At most one open interval per address.</strong> A partial unique index over
/// <see cref="IpAddress"/> where <see cref="ObservedTo"/> is null. If a handler ever failed to
/// close the previous holder before opening the next, the insert fails rather than producing an
/// address that resolves to two clients.
/// </description>
/// </item>
/// <item>
/// <description>
/// <strong>No gaps.</strong> A binding is closed at exactly the instant its successor opens, so
/// every timestamp between the first observation of an address and now falls inside exactly one
/// interval.
/// </description>
/// </item>
/// </list>
/// <para>
/// <strong>A boundary is an observation, not an event.</strong> NetShield learns an address by
/// reading an ARP table on a schedule, so the true handover happened somewhere in the window
/// before the read that noticed it. Closing the old interval where the new one opens means a
/// timestamp inside that window resolves to the <em>previous</em> holder — which is the holder
/// NetShield had actually observed at that moment. Inventing an earlier boundary would be
/// asserting a handover nobody saw.
/// </para>
/// </remarks>
internal sealed class ClientIpBinding
{
    /// <summary>UUID v7.</summary>
    public Guid Id { get; init; }

    /// <summary>The client that held the address.</summary>
    public Guid ClientId { get; init; }

    /// <summary>
    /// The address. PostgreSQL <c>inet</c>, for the reason <c>devices.primary_ip_address</c> is:
    /// it refuses a value that is not an address and normalises the notation, so two spellings
    /// of one address cannot both be stored and both look free.
    /// </summary>
    public required IPAddress IpAddress { get; init; }

    /// <summary>What kind of observation established this binding.</summary>
    public ClientObservationSource Source { get; set; }

    /// <summary>
    /// The device whose table reported it — whose ARP cache this address was read from. Null on
    /// a binding from a source that names no device.
    /// </summary>
    public Guid? DeviceId { get; set; }

    /// <summary>When the interval opened. UTC, inclusive.</summary>
    public DateTimeOffset ObservedFrom { get; init; }

    /// <summary>
    /// When it closed, or <see langword="null"/> while it is the current binding for this
    /// address. UTC, exclusive.
    /// </summary>
    public DateTimeOffset? ObservedTo { get; set; }

    /// <summary>
    /// When an observation last confirmed this binding.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="ObservedTo"/> and deliberately so. An open interval keeps moving
    /// this on every walk that still sees the address on this MAC; a closed one keeps the last
    /// time it was actually seen, which is earlier than when it closed by however long the
    /// address sat unobserved before somebody else took it.
    /// </remarks>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>When the row was created. UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the row last changed. UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
