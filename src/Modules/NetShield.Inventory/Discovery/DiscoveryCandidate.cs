using System.Net;

using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Discovery;

/// <summary>
/// An address a sweep found that is not a device, waiting for somebody to decide about it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Keyed by address.</strong> That is what makes a re-run update rather than duplicate:
/// the second run to see 10.0.0.5 moves <see cref="LastSeenAt"/> and
/// <see cref="TimesSeen"/> and leaves <see cref="FirstSeenAt"/> where it was.
/// </para>
/// <para>
/// A candidate is an address and nothing else. The sweep asks one question — does anything
/// answer here — and what a host turns out to be is established by the SNMP walk WP-1.5 built,
/// which runs against a device once this candidate has been promoted into one.
/// </para>
/// <para>
/// There is no soft delete. A candidate is machine output, and the two things a person can do
/// with one are recorded as a status rather than as a removal — an ignored candidate has to stay
/// visible enough to explain why the address is not being offered again.
/// </para>
/// </remarks>
internal sealed class DiscoveryCandidate
{
    /// <summary>UUID v7, so the primary key is also the order candidates were first seen in.</summary>
    public Guid Id { get; init; }

    /// <summary>The address that answered. Unique across the table.</summary>
    public IPAddress Address { get; init; } = IPAddress.None;

    /// <summary>Whether anybody has decided about it.</summary>
    public DiscoveryCandidateStatus Status { get; set; }

    /// <summary>How many runs have seen it answer.</summary>
    public int TimesSeen { get; set; }

    /// <summary>The round trip the last time it answered.</summary>
    public double? LastRttMilliseconds { get; set; }

    /// <summary>When a run first saw it. UTC. Never moves.</summary>
    public DateTimeOffset FirstSeenAt { get; init; }

    /// <summary>When a run last saw it. UTC.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>The run that first saw it.</summary>
    public Guid FirstSeenRunId { get; init; }

    /// <summary>The run that last saw it.</summary>
    public Guid LastSeenRunId { get; set; }

    /// <summary>The device it became, once it has been promoted.</summary>
    public Guid? PromotedDeviceId { get; set; }

    /// <summary>When it was promoted or ignored. UTC.</summary>
    public DateTimeOffset? SettledAt { get; set; }

    /// <summary>When the row was created. UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the row last changed. UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
