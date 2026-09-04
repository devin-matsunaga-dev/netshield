namespace NetShield.Contracts.Inventory;

/// <summary>
/// One address that answered a run's sweep, and what NetShield made of it.
/// </summary>
/// <remarks>
/// There is no row for an address that stayed silent. <see cref="DiscoveryHostOutcome"/> says
/// why.
/// </remarks>
/// <param name="Id">The row.</param>
/// <param name="RunId">The run that observed it.</param>
/// <param name="Address">The address that answered.</param>
/// <param name="RttMilliseconds">The round trip of the reply, when one was timed.</param>
/// <param name="Outcome">What NetShield did about it.</param>
/// <param name="CandidateId">The candidate it created or refreshed, when it did either.</param>
/// <param name="DeviceId">The device it already belongs to, when it does.</param>
/// <param name="ObservedAt">When the sweep saw it. UTC.</param>
public sealed record DiscoveryRunHostResult(
    Guid Id,
    Guid RunId,
    string Address,
    double? RttMilliseconds,
    DiscoveryHostOutcome Outcome,
    Guid? CandidateId,
    Guid? DeviceId,
    DateTimeOffset ObservedAt);
