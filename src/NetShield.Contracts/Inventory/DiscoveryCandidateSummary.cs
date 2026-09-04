namespace NetShield.Contracts.Inventory;

/// <summary>
/// An address a sweep found that is not a device, waiting for somebody to decide about it.
/// </summary>
/// <remarks>
/// <para>
/// A candidate is an address and nothing more. WP-1.6's sweep asks one question — does anything
/// answer here — and identification stays with the SNMP walk WP-1.5 built, which runs against a
/// device after promotion. Trying a device's several credentials against an unknown host during
/// a sweep would mean carrying more than one credential on a lease, which is a change to the
/// collector contract.
/// </para>
/// <para>
/// It is keyed by address, so a re-run updates the row rather than adding a second one:
/// <see cref="TimesSeen"/> and <see cref="LastSeenAt"/> move and <see cref="FirstSeenAt"/> does
/// not.
/// </para>
/// </remarks>
/// <param name="Id">The candidate.</param>
/// <param name="Address">The address that answered.</param>
/// <param name="Status">Whether anybody has decided about it.</param>
/// <param name="TimesSeen">How many runs have seen it answer.</param>
/// <param name="LastRttMilliseconds">The round trip the last time it answered.</param>
/// <param name="FirstSeenAt">When a run first saw it. UTC.</param>
/// <param name="LastSeenAt">When a run last saw it. UTC.</param>
/// <param name="FirstSeenRunId">The run that first saw it.</param>
/// <param name="LastSeenRunId">The run that last saw it.</param>
/// <param name="PromotedDeviceId">The device it became, once it has been promoted.</param>
public sealed record DiscoveryCandidateSummary(
    Guid Id,
    string Address,
    DiscoveryCandidateStatus Status,
    int TimesSeen,
    double? LastRttMilliseconds,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    Guid FirstSeenRunId,
    Guid LastSeenRunId,
    Guid? PromotedDeviceId);
